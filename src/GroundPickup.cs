using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarthJML.RoboticInbox
{
    // Ground pickup: an inbox collects loose item drops lying near it (wolf-farm drops and the like).
    //
    // Scope: only ItemDrop objects are touched. Crops, berry bushes and other harvestables are
    // Pickable objects, so they are never collected. Items on item stands / armor stands carry no
    // ItemDrop either. Items a player threw down are marked on their ZDO and never collected. Placed "item pieces" (IsPiece), live fish, quest items, tarred items and
    // drops flagged no-auto-pickup are skipped, matching what a player would auto-pick-up by walking.
    //
    // Multiplayer: only the client that owns the inbox sweeps, so two clients never compete. A drop
    // is taken only once this client owns its ZDO (same RequestOwn path vanilla auto-pickup uses),
    // and the world object is then destroyed or its stack reduced by exactly the amount the inbox
    // gained, so nothing is duplicated or lost.
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnPlayerDrop))]
    static class ItemDrop_OnPlayerDrop_Patch
    {
        static void Postfix(ItemDrop __instance)
        {
            try
            {
                var nview = __instance.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && nview.IsOwner())
                    nview.GetZDO().Set(GroundPickup.PlayerDroppedHash, true);
            }
            catch (Exception e) { Debug.LogWarning("RoboticInbox: could not mark a player-dropped item: " + e.Message); }
        }
    }

    internal static class GroundPickup
    {
        static readonly AccessTools.FieldRef<List<ItemDrop>> Instances =
            AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));

        // Set on the ZDO when a player throws an item down, so every client (not just the one who
        // dropped it) knows to leave it alone. Vanilla only remembers this on the dropper's own client.
        internal static readonly int PlayerDroppedHash = "RoboticInbox_playerDropped".GetStableHashCode();

        static readonly List<ItemDrop> Buffer = new List<ItemDrop>();
        static readonly List<Container> Inboxes = new List<Container>();

        static string _allowRaw, _denyRaw;
        static HashSet<string> _allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static HashSet<string> _deny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static HashSet<string> Parse(string raw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return set;
            foreach (var part in raw.Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0) set.Add(s);
            }
            return set;
        }

        static void RefreshFilters()
        {
            string allow = RoboticInboxPlugin.PickupAllow, deny = RoboticInboxPlugin.PickupDeny;
            if (allow != _allowRaw) { _allowRaw = allow; _allow = Parse(allow); }
            if (deny != _denyRaw) { _denyRaw = deny; _deny = Parse(deny); }
        }

        // Matches the prefab name (e.g. LeatherScraps) or the display name (e.g. Leather scraps).
        static bool PassesFilter(ItemDrop drop)
        {
            if (_allow.Count == 0 && _deny.Count == 0) return true;
            string prefab = Utils.GetPrefabName(drop.gameObject);
            string token = drop.m_itemData.m_shared.m_name;
            string shown = Localization.instance != null ? Localization.instance.Localize(token) : token;
            bool Has(HashSet<string> set) => set.Contains(prefab) || set.Contains(shown) || set.Contains(token);
            if (Has(_deny)) return false;
            return _allow.Count == 0 || Has(_allow);
        }

        static bool Eligible(ItemDrop drop)
        {
            if (drop == null || !drop.m_autoPickup || drop.IsPiece() || drop.InTar()) return false;
            var nview = drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;
            if (nview.GetZDO().GetBool(PlayerDroppedHash)) return false;
            var shared = drop.m_itemData != null ? drop.m_itemData.m_shared : null;
            if (shared == null || shared.m_questItem) return false;
            if (shared.m_icons == null || shared.m_icons.Length == 0) return false;
            if (drop.GetComponent<Fish>() != null) return false;
            return true;
        }

        // Returns how many items were collected this sweep.
        internal static int Sweep(Func<Container, bool> isInbox, Func<Container, bool> canWrite)
        {
            if (ZNetScene.instance == null) return 0;
            Inboxes.Clear();
            foreach (var c in RoboticInboxPlugin.Containers)
                if (c != null && isInbox(c) && canWrite(c)) Inboxes.Add(c);
            if (Inboxes.Count == 0) return 0;

            RefreshFilters();
            float r2 = RoboticInboxPlugin.PickupRange * RoboticInboxPlugin.PickupRange;
            int collected = 0;

            // Destroying a drop edits ItemDrop.s_instances, so walk a snapshot.
            Buffer.Clear();
            Buffer.AddRange(Instances());
            foreach (var drop in Buffer)
            {
                if (!Eligible(drop)) continue;
                var pos = drop.transform.position;

                // Nearest inbox in range.
                Container best = null;
                float bestD = r2;
                foreach (var inbox in Inboxes)
                {
                    float d = (inbox.transform.position - pos).sqrMagnitude;
                    if (d <= bestD) { bestD = d; best = inbox; }
                }
                if (best == null) continue;

                // Not ours yet: ask the owner for it and try again on a later sweep.
                if (!drop.CanPickup()) { drop.RequestOwn(); continue; }

                drop.Load();
                var item = drop.m_itemData;
                if (item == null || item.m_stack <= 0 || !PassesFilter(drop)) continue;

                var inv = best.GetInventory();
                string name = item.m_shared.m_name;
                // Full inbox: leave the item on the ground.
                if (!inv.HaveEmptySlot() && inv.FindFreeStackSpace(name, item.m_worldLevel) <= 0) continue;

                int stack = item.m_stack;
                int before = inv.CountItems(name, -1, false);
                inv.AddItem(item.Clone());
                int gained = inv.CountItems(name, -1, false) - before;
                if (gained <= 0) continue;

                if (gained >= stack) ZNetScene.instance.Destroy(drop.gameObject);
                else drop.SetStack(stack - gained);
                collected += gained;

                if (RoboticInboxPlugin.DebugLog.Value)
                    Debug.Log($"RoboticInbox: picked up {gained}x {name} from the ground ({Mathf.Sqrt(bestD):0.0} m)");
            }
            Buffer.Clear();
            return collected;
        }
    }
}
