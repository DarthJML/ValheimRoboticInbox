using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarthJML.RoboticInbox
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class RoboticInboxPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.darthjml.roboticinbox";
        public const string PluginName = "RoboticInbox";
        public const string PluginVersion = "1.3.0";

        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<int> InboxWidth;
        public static ConfigEntry<int> InboxHeight;
        public static ConfigEntry<bool> DebugLog;
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> GlowIntensity;
        public static ConfigEntry<bool> RequireMod;
        public static ConfigEntry<bool> PickupEnabled;
        public static ConfigEntry<float> PickupRadius;
        public static ConfigEntry<float> PickupInterval;
        public static ConfigEntry<string> PickupAllowList;
        public static ConfigEntry<string> PickupDenyList;

        private float _timer;
        private float _pickupTimer;
        private bool _errorLogged;
        private bool _pickupErrorLogged;
        public const string PrefabName = "RoboticInbox";
        public static GameObject RoboticInboxPrefab;
        private static GameObject _prefabHolder;

        // Every live Container with an inventory (filled by Container.Awake postfix); destroyed
        // entries become Unity-null and are pruned each tick. Avoids FindObjectsByType every second.
        internal static readonly List<Container> Containers = new List<Container>();

        // Values pushed by the server on connect; null = use local config (single-player / host).
        internal static float? SyncedRadius;
        internal static int? SyncedWidth, SyncedHeight;
        internal static float Radius => SyncedRadius ?? ScanRadius.Value;
        internal static int Width => SyncedWidth ?? InboxWidth.Value;
        internal static int Height => SyncedHeight ?? InboxHeight.Value;

        internal static bool? SyncedPickupEnabled;
        internal static float? SyncedPickupRadius;
        internal static string SyncedPickupAllow, SyncedPickupDeny;
        internal static bool PickupOn => SyncedPickupEnabled ?? PickupEnabled.Value;
        internal static float PickupRange => SyncedPickupRadius ?? PickupRadius.Value;
        internal static string PickupAllow => SyncedPickupAllow ?? PickupAllowList.Value;
        internal static string PickupDeny => SyncedPickupDeny ?? PickupDenyList.Value;

        static readonly AccessTools.FieldRef<Container, ZNetView> ContainerNView = AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        static readonly AccessTools.FieldRef<Container, uint> ContainerLastRevision = AccessTools.FieldRefAccess<Container, uint>("m_lastRevision");
        static readonly Func<Container, long, bool> ContainerCheckAccess = AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));

        void Awake()
        {
            ScanRadius = Config.Bind("General", "ScanRadius", 20f, new ConfigDescription("How far (meters) the inbox looks for matching chests. One floor tile = 2 m. Server value is enforced on clients.", new AcceptableValueRange<float>(1f, 64f)));
            InboxWidth = Config.Bind("Inbox", "Columns", 8, new ConfigDescription("Inbox slot columns (wood chest = 5, max 8). Applies on world load; server value is enforced on clients.", new AcceptableValueRange<int>(1, 8)));
            InboxHeight = Config.Bind("Inbox", "Rows", 4, new ConfigDescription("Inbox slot rows (wood chest = 2). Applies on world load; server value is enforced on clients.", new AcceptableValueRange<int>(1, 8)));
            DebugLog = Config.Bind("General", "Debug", false, "Enable debug logging.");
            Enabled = Config.Bind("General", "Enabled", true, "Enable auto-dispersion.");
            GlowIntensity = Config.Bind("Visual", "GlowIntensity", 3f, new ConfigDescription("Brightness of the Cybercrate's cyan runes (0 = off). Client-only, applies live.", new AcceptableValueRange<float>(0f, 10f)));
            GlowIntensity.SettingChanged += (_, __) => Cybercrate.UpdateGlow();
            RequireMod = Config.Bind("Server", "RequireMod", true, "Server/host only: disconnect players who don't have RoboticInbox or have a different version. If false, they may join and only a warning is logged.");

            PickupEnabled = Config.Bind("GroundPickup", "Enabled", true, "The inbox collects loose items lying on the ground near it (for example drops from a wolf farm). It never harvests crops or plants, never takes placed items, and leaves items a player threw down. Server value is enforced on clients.");
            PickupRadius = Config.Bind("GroundPickup", "Radius", 20f, new ConfigDescription("How far (meters) the inbox reaches for items on the ground. One floor tile = 2 m, so 20 m = 10 tiles. Server value is enforced on clients.", new AcceptableValueRange<float>(1f, 64f)));
            PickupInterval = Config.Bind("GroundPickup", "SweepSeconds", 2f, new ConfigDescription("Seconds between ground sweeps.", new AcceptableValueRange<float>(0.5f, 60f)));
            PickupAllowList = Config.Bind("GroundPickup", "OnlyTheseItems", "", "Comma-separated item names. If set, ONLY these are collected. Use prefab names (LeatherScraps, RawMeat, WolfPelt) or the in-game names. Empty = collect everything. Server value is enforced on clients.");
            PickupDenyList = Config.Bind("GroundPickup", "NeverTheseItems", "", "Comma-separated item names that are never collected. Server value is enforced on clients.");

            new Harmony(PluginGuid).PatchAll();
            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        void Update()
        {
            try { ConfigSync.Tick(); }
            catch (Exception e) { Logger.LogWarning(e); }
            if (!Enabled.Value) return;
            if (PickupOn)
            {
                _pickupTimer += Time.deltaTime;
                if (_pickupTimer >= PickupInterval.Value)
                {
                    _pickupTimer = 0f;
                    try { ProcessGroundPickup(); }
                    catch (Exception e)
                    {
                        if (DebugLog.Value || !_pickupErrorLogged) Logger.LogError(e);
                        _pickupErrorLogged = true;
                    }
                }
            }
            _timer += Time.deltaTime;
            if (_timer < 1f) return;
            _timer = 0f;
            try { ProcessInboxes(); }
            catch (Exception e)
            {
                if (DebugLog.Value || !_errorLogged) Logger.LogError(e);
                _errorLogged = true;
            }
        }

        // Called from ZNetScene.Awake: clone the vanilla chest under an inactive holder
        // (so ZNetView/Piece Awake never run on the template), register it so placed
        // inboxes load from saves, and add it to the hammer's piece table.
        internal static void RegisterPrefab(ZNetScene zns)
        {
            if (RoboticInboxPrefab == null)
            {
                var original = zns.GetPrefab("piece_chest_wood");
                if (original == null)
                {
                    Debug.LogWarning("RoboticInbox: could not find piece_chest_wood prefab.");
                    return;
                }

                if (_prefabHolder == null)
                {
                    _prefabHolder = new GameObject("RoboticInbox_PrefabHolder");
                    _prefabHolder.SetActive(false);
                    Object.DontDestroyOnLoad(_prefabHolder);
                }

                var clone = Object.Instantiate(original, _prefabHolder.transform, false);
                clone.name = PrefabName;

                var piece = clone.GetComponent<Piece>();
                piece.m_name = "Robotic Inbox";
                piece.m_description = "Storage box that automatically disperses items to nearby containers containing matching items.";
                piece.m_enabled = true;
                piece.m_category = Piece.PieceCategory.Misc;
                piece.m_craftingStation = null;
                var reqs = new List<Piece.Requirement>();
                foreach (var mat in new[] { "Wood", "Stone" })
                {
                    var drop = zns.GetPrefab(mat)?.GetComponent<ItemDrop>();
                    if (drop != null)
                        reqs.Add(new Piece.Requirement { m_resItem = drop, m_amount = 10, m_recover = true });
                    else
                        Debug.LogWarning("RoboticInbox: material not found: " + mat);
                }
                piece.m_resources = reqs.ToArray();
                piece.m_dlc = "";

                var container = clone.GetComponent<Container>();
                if (container != null) container.m_name = "Robotic Inbox";

                try { Cybercrate.Apply(clone); }
                catch (Exception e) { Debug.LogError("RoboticInbox: Cybercrate visual failed, keeping the wood chest look: " + e); }

                RoboticInboxPrefab = clone;
            }
            ApplyInboxSize();

            int hash = PrefabName.GetStableHashCode();
            var named = AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs")(zns);
            if (!named.ContainsKey(hash))
            {
                zns.m_prefabs.Add(RoboticInboxPrefab);
                named.Add(hash, RoboticInboxPrefab);
            }

            var hammer = zns.GetPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null)
            {
                Debug.LogWarning("RoboticInbox: hammer piece table not found.");
                return;
            }
            table.m_pieces.RemoveAll(go => go == null);
            if (!table.m_pieces.Contains(RoboticInboxPrefab))
                table.m_pieces.Add(RoboticInboxPrefab);

            Debug.Log("RoboticInbox: prefab registered and added to hammer.");
        }

        // Applies the (possibly server-synced) size to the template; inboxes instantiated afterwards use it.
        internal static void ApplyInboxSize()
        {
            var container = RoboticInboxPrefab != null ? RoboticInboxPrefab.GetComponent<Container>() : null;
            if (container == null) return;
            container.m_width = Width;
            container.m_height = Height;
        }

        static bool IsInbox(Container c) => c.name.StartsWith(PrefabName, StringComparison.Ordinal);

        // We may only write a container we own, that nobody has open, and whose local inventory
        // reflects the latest ZDO data (ownership can arrive before Container.CheckForChanges reloads;
        // saving a stale inventory would overwrite another client's changes -> loss/dupe).
        static bool CanWrite(Container c)
        {
            if (!c.IsOwner() || c.IsInUse() || c.GetInventory() == null) return false;
            var nview = ContainerNView(c);
            return nview != null && nview.IsValid() && ContainerLastRevision(c) == nview.GetZDO().DataRevision;
        }

        void ProcessGroundPickup()
        {
            // Same rule as sorting: no local player (dedicated server / menu) means nothing to do here;
            // the client that owns the inbox does the work.
            if (Player.m_localPlayer == null) return;
            Containers.RemoveAll(c => c == null);
            GroundPickup.Sweep(IsInbox, CanWrite);
        }

        void ProcessInboxes()
        {
            // Dedicated server / main menu: no local player. Clients that own the containers do the work.
            var player = Player.m_localPlayer;
            if (player == null) return;
            Containers.RemoveAll(c => c == null);
            float r2 = Radius * Radius;
            long playerId = player.GetPlayerID();

            foreach (var inbox in Containers)
            {
                if (!IsInbox(inbox)) continue;
                // Wait until nobody has it open (sorts on close).
                if (!CanWrite(inbox)) continue;
                var inv = inbox.GetInventory();
                if (inv.NrOfItems() == 0) continue;
                var pos = inbox.transform.position;

                // Nearby non-inbox containers we own, may access, and that aren't open. Never graves.
                var targets = new List<Container>();
                foreach (var c in Containers)
                {
                    if (c == inbox || IsInbox(c)) continue;
                    if ((c.transform.position - pos).sqrMagnitude > r2) continue;
                    if (!CanWrite(c) || !ContainerCheckAccess(c, playerId)) continue;
                    if (c.GetComponentInParent<TombStone>() != null) continue;
                    targets.Add(c);
                }
                if (targets.Count == 0) continue;
                targets.Sort((a, b) => (a.transform.position - pos).sqrMagnitude
                    .CompareTo((b.transform.position - pos).sqrMagnitude));

                foreach (var item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                {
                    if (item == null || item.m_shared == null) continue;
                    foreach (var t in targets)
                    {
                        var tinv = t.GetInventory();
                        if (!tinv.ContainsItemByName(item.m_shared.m_name)) continue;

                        int amount = item.m_stack;
                        if (!tinv.CanAddItem(item, amount))
                        {
                            amount = tinv.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel);
                            if (amount <= 0) continue;
                            amount = Math.Min(amount, item.m_stack);
                        }
                        // AddItem can top up existing stacks and then fail on a full grid while
                        // returning false, so remove exactly what actually arrived (no dupe/loss).
                        string name = item.m_shared.m_name;
                        int before = tinv.CountItems(name, -1, false);
                        var copy = item.Clone();
                        copy.m_stack = amount;
                        tinv.AddItem(copy);
                        amount = tinv.CountItems(name, -1, false) - before;
                        if (amount <= 0) continue;
                        inv.RemoveItem(item, amount);
                        if (DebugLog.Value)
                            Logger.LogInfo($"RoboticInbox: moved {amount}x {item.m_shared.m_name} -> {t.name}");
                        if (item.m_stack <= 0 || !inv.ContainsItem(item)) break;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    public static class Container_Awake_Patch
    {
        // Ghost/placement previews have no ZDO and no inventory; skip them.
        static void Postfix(Container __instance)
        {
            if (__instance.GetInventory() != null) RoboticInboxPlugin.Containers.Add(__instance);
        }
    }

    // Minimal server <-> client version check and server -> client config sync over the
    // vanilla peer RPC (no ServerSync dependency). Vanilla peers silently ignore unknown RPCs.
    //
    // Both sides send their version as soon as the socket exists (OnNewConnection). The client's
    // version therefore reaches the server before its PeerInfo, so the server can decide in
    // RPC_PeerInfo without timers. Rejected clients get the vanilla "Error: incompatible version"
    // (the only reason text a vanilla client can display); modded clients replace that text with
    // the exact RoboticInbox versions.
    public static class ConfigSync
    {
        const string RpcName = "RoboticInbox_Config";
        const string VersionRpcName = "RoboticInbox_Version";
        const float KickDelay = 3f; // lets the Error RPC reach the client before the socket closes

        static string Mine => RoboticInboxPlugin.PluginVersion;

        // Server: version each connecting client reported (absent = client has no RoboticInbox).
        static readonly Dictionary<ZRpc, string> PeerVersions = new Dictionary<ZRpc, string>();
        // Server: rejected clients still connected, with the time to force-disconnect them.
        static readonly Dictionary<ZRpc, float> PendingKicks = new Dictionary<ZRpc, float>();
        // Client: version the current server reported (null = server has no RoboticInbox).
        static string _serverVersion;
        // Client: text for the main-menu error if this server rejects us; survives logout.
        static string _rejectMessage;
        // Client: center-screen warning to show once the player has spawned.
        static string _pendingHudWarning;

        static readonly Func<ZNet, ZRpc, ZNetPeer> GetPeer = AccessTools.MethodDelegate<Func<ZNet, ZRpc, ZNetPeer>>(AccessTools.Method(typeof(ZNet), "GetPeer", new[] { typeof(ZRpc) }));

        static string Endpoint(ZRpc rpc)
        {
            try { return rpc.GetSocket()?.GetEndPointString() ?? "?"; }
            catch { return "?"; }
        }

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        static class OnNewConnection_Patch
        {
            static void Postfix(ZNet __instance, ZNetPeer peer)
            {
                peer.m_rpc.Register<ZPackage>(RpcName, RPC_Config);
                peer.m_rpc.Register<string>(VersionRpcName, RPC_Version);
                if (!__instance.IsServer())
                {
                    _serverVersion = null;
                    _rejectMessage = null;
                    _pendingHudWarning = null;
                }
                peer.m_rpc.Invoke(VersionRpcName, Mine);
            }
        }

        static void RPC_Version(ZRpc rpc, string version)
        {
            var znet = ZNet.instance;
            if (znet == null || rpc == null) return;
            version = version ?? "";
            if (znet.IsServer())
            {
                PeerVersions[rpc] = version;
                if (version != Mine)
                    Debug.LogWarning($"RoboticInbox: client {Endpoint(rpc)} has RoboticInbox v{version}, server has v{Mine}.");
                return;
            }

            _serverVersion = version;
            if (version == Mine)
            {
                Debug.Log($"RoboticInbox: server runs RoboticInbox v{version} (same as ours).");
                return;
            }
            Debug.LogWarning($"RoboticInbox: VERSION MISMATCH - server has RoboticInbox v{version}, you have v{Mine}.");
            _rejectMessage = $"This server requires Robotic Inbox v{version}.\nYou have v{Mine}.\n\nImport the server's r2modman profile code (or install Robotic Inbox v{version}) and try again.";
            _pendingHudWarning = $"Robotic Inbox version mismatch!\nServer v{version}, you v{Mine}. Please update.";
        }

        // Server: reject clients with a missing/different version before vanilla accepts them, then
        // send config after vanilla handled PeerInfo, i.e. before ZDOs/objects are created there.
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        static class RPC_PeerInfo_Patch
        {
            static bool Prefix(ZNet __instance, ZRpc rpc, ref bool __state)
            {
                __state = true;
                try
                {
                    if (!__instance.IsServer() || rpc == null) return true;
                    PeerVersions.TryGetValue(rpc, out var version);
                    PeerVersions.Remove(rpc);
                    if (version == Mine) return true;

                    string who = Endpoint(rpc);
                    string problem = version == null
                        ? $"client {who} does not have RoboticInbox (server has v{Mine})"
                        : $"client {who} has RoboticInbox v{version}, server has v{Mine}";
                    if (!RoboticInboxPlugin.RequireMod.Value)
                    {
                        Debug.LogWarning($"RoboticInbox: {problem}; allowed because Server.RequireMod = false.");
                        return true;
                    }
                    Debug.LogWarning($"RoboticInbox: {problem}; disconnecting (Server.RequireMod = true).");
                    rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
                    PendingKicks[rpc] = Time.time + KickDelay;
                    __state = false;
                    return false;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("RoboticInbox: version check failed, letting client in: " + e);
                    return true;
                }
            }

            static void Postfix(ZNet __instance, ZRpc rpc, bool __state)
            {
                if (!__state || rpc == null) return;
                if (!__instance.IsServer())
                {
                    if (_serverVersion == null)
                        Debug.Log("RoboticInbox: this server does not run RoboticInbox. Sorting still works for you with your local settings; players without the mod won't see inboxes.");
                    return;
                }
                var pkg = new ZPackage();
                pkg.Write(RoboticInboxPlugin.ScanRadius.Value);
                pkg.Write(RoboticInboxPlugin.InboxWidth.Value);
                pkg.Write(RoboticInboxPlugin.InboxHeight.Value);
                pkg.Write(RoboticInboxPlugin.PickupEnabled.Value);
                pkg.Write(RoboticInboxPlugin.PickupRadius.Value);
                pkg.Write(RoboticInboxPlugin.PickupAllowList.Value ?? "");
                pkg.Write(RoboticInboxPlugin.PickupDenyList.Value ?? "");
                rpc.Invoke(RpcName, pkg);
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), new[] { typeof(ZNetPeer) })]
        static class Disconnect_Patch
        {
            static void Prefix(ZNetPeer peer)
            {
                if (peer?.m_rpc == null) return;
                PeerVersions.Remove(peer.m_rpc);
                PendingKicks.Remove(peer.m_rpc);
            }
        }

        // Main menu "connection failed" panel: explain RoboticInbox rejections instead of only
        // "Incompatible version".
        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        static class ShowConnectError_Patch
        {
            static void Postfix(FejdStartup __instance)
            {
                if (_rejectMessage == null || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.ErrorVersion) return;
                if (__instance.m_connectionFailedError != null)
                    __instance.m_connectionFailedError.text = _rejectMessage;
                _rejectMessage = null;
            }
        }

        // Called every frame from the plugin (also on dedicated servers).
        internal static void Tick()
        {
            if (PendingKicks.Count > 0)
            {
                var znet = ZNet.instance;
                foreach (var rpc in new List<ZRpc>(PendingKicks.Keys))
                {
                    if (znet != null && Time.time < PendingKicks[rpc]) continue;
                    PendingKicks.Remove(rpc);
                    var peer = znet != null ? GetPeer(znet, rpc) : null;
                    if (peer == null) continue; // client already left
                    Debug.Log($"RoboticInbox: disconnecting rejected client {Endpoint(rpc)}.");
                    znet.Disconnect(peer);
                }
            }

            if (_pendingHudWarning != null && Player.m_localPlayer != null && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, _pendingHudWarning);
                _pendingHudWarning = null;
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        static class OnDestroy_Patch
        {
            static void Postfix()
            {
                RoboticInboxPlugin.SyncedRadius = null;
                RoboticInboxPlugin.SyncedWidth = RoboticInboxPlugin.SyncedHeight = null;
                RoboticInboxPlugin.SyncedPickupEnabled = null;
                RoboticInboxPlugin.SyncedPickupRadius = null;
                RoboticInboxPlugin.SyncedPickupAllow = RoboticInboxPlugin.SyncedPickupDeny = null;
                RoboticInboxPlugin.ApplyInboxSize();
                PeerVersions.Clear();
                PendingKicks.Clear();
                _serverVersion = null;
                _pendingHudWarning = null;
            }
        }

        static void RPC_Config(ZRpc rpc, ZPackage pkg)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;
            RoboticInboxPlugin.SyncedRadius = Mathf.Clamp(pkg.ReadSingle(), 1f, 64f);
            RoboticInboxPlugin.SyncedWidth = Mathf.Clamp(pkg.ReadInt(), 1, 8);
            RoboticInboxPlugin.SyncedHeight = Mathf.Clamp(pkg.ReadInt(), 1, 8);
            RoboticInboxPlugin.ApplyInboxSize();
            // Ground-pickup settings were added in 1.3.0; an older server's package ends here.
            try
            {
                if (pkg.GetPos() < pkg.Size())
                {
                    RoboticInboxPlugin.SyncedPickupEnabled = pkg.ReadBool();
                    RoboticInboxPlugin.SyncedPickupRadius = Mathf.Clamp(pkg.ReadSingle(), 1f, 64f);
                    RoboticInboxPlugin.SyncedPickupAllow = pkg.ReadString();
                    RoboticInboxPlugin.SyncedPickupDeny = pkg.ReadString();
                }
            }
            catch (Exception e) { Debug.LogWarning("RoboticInbox: could not read ground-pickup settings from server: " + e.Message); }
            Debug.Log($"RoboticInbox: using server config radius={RoboticInboxPlugin.SyncedRadius} size={RoboticInboxPlugin.SyncedWidth}x{RoboticInboxPlugin.SyncedHeight}");
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class ZNetScene_Awake_Patch
    {
        static void Postfix(ZNetScene __instance)
        {
            try { RoboticInboxPlugin.RegisterPrefab(__instance); }
            catch (Exception e) { Debug.LogWarning("RoboticInbox: register failed " + e); }
        }
    }
}

namespace DarthJML.RoboticInbox
{
    // Safety net: make sure every hammer table the player owns contains the inbox,
    // and that the player "knows" it, so it shows in the build menu.
    [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
    public static class Player_UpdateKnownRecipesList_Patch
    {
        static string _lastState;
        static readonly AccessTools.FieldRef<Player, HashSet<string>> KnownRecipes = AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownRecipes");
        static readonly System.Reflection.MethodInfo UpdateAvailablePiecesList = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

        static void Postfix(Player __instance)
        {
            try
            {
                var prefab = RoboticInboxPlugin.RoboticInboxPrefab;
                if (prefab == null || __instance != Player.m_localPlayer) return;
                var piece = prefab.GetComponent<Piece>();
                var known = KnownRecipes(__instance);
                var tables = new List<PieceTable>();
                __instance.GetInventory().GetAllPieceTables(tables);

                int hammerTables = 0, added = 0;
                foreach (var t in tables)
                {
                    if (t == null || t.m_pieces == null) continue;
                    if (!t.m_pieces.Exists(g => g != null && g.name == "piece_chest_wood")) continue;
                    hammerTables++;
                    if (!t.m_pieces.Contains(prefab)) { t.m_pieces.Add(prefab); added++; }
                }

                bool wasKnown = known.Contains(piece.m_name);
                if (hammerTables > 0 && !wasKnown) known.Add(piece.m_name);
                if (added > 0 || (hammerTables > 0 && !wasKnown))
                    UpdateAvailablePiecesList.Invoke(__instance, null);

                string state = $"tables={tables.Count} hammerTables={hammerTables} added={added} wasKnown={wasKnown}";
                if (state != _lastState) { _lastState = state; Debug.Log("RoboticInbox: " + state); }
            }
            catch (Exception e) { Debug.LogWarning("RoboticInbox: known-list patch failed: " + e); }
        }
    }
}
