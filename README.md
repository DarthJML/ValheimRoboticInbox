# Robotic Inbox (Valheim mod)

A glowing Viking Cybercrate that sorts your loot and collects drops from the ground.

- **Download:** [Thunderstore](https://thunderstore.io/c/valheim/p/DarthJML/RoboticInbox/) or the zip on the [Releases](https://github.com/DarthJML/ValheimRoboticInbox/releases) page
- **Needs:** BepInExPack for Valheim. Install with r2modman.
- **Multiplayer:** the server and every player need the same version.

**Stop sorting your loot by hand.** Build a Robotic Inbox next to your chests, dump everything into it,
close it, and each item flies into the nearby chest that already holds that kind of item.

It's a Valheim version of the **Robotic Inbox** mod for *7 Days to Die*, created by
**Jonathan Robertson (Kanaverum)**. All credit for the idea goes to them. Valheim port by DarthJML.

## How to build it

1. Equip your **Hammer**.
2. Open the **Misc** tab of the build menu.
3. Pick **Robotic Inbox**. It costs **10 Wood + 10 Stone** and doesn't need a workbench.
4. Place it near your storage chests.

It looks like a **Viking Cybercrate**: an old timber-and-stone crate with iron fittings and glowing cyan runes.

## How sorting works

- Put items in the inbox, then **close it**. Nothing moves while someone has it open.
- Every second the inbox checks the chests around it (20 meters by default, about 10 floor tiles).
- An item goes to the **closest chest that already has at least one of that item**, as long as there's room.
  Got a chest with Wood in it? All your Wood goes there.
- Items no chest has yet **stay in the inbox**. To make a new "home" for an item, put one of it in a chest yourself.
- It never moves items into other inboxes, graves (tombstones), chests that are open, or private chests you can't open.
- A full chest is skipped and the next closest matching chest is tried.

## Ground pickup (new in 1.3.0)

The inbox also **collects loose items lying on the ground near it**, then sorts them like anything else.
Build one beside a wolf pen or a mob farm and the meat, pelts and trophies end up in your chests.

- Reach is **20 meters** by default (10 floor tiles). It sweeps every 2 seconds.
- It only takes **dropped items**. It never harvests crops, berries or plants, and it never takes items
  on item stands, armor stands or anything you placed as decoration with the hammer.
- It also leaves live fish, quest items and items stuck in tar alone.
- When the inbox is full, items stay on the ground until there is room.
- Want only some items? Set `GroundPickup.OnlyTheseItems` (for example `RawMeat, WolfPelt, WolfFang`).
  Want everything except a few? Set `GroundPickup.NeverTheseItems`.
- Don't want it? Set `GroundPickup.Enabled = false`.

Items a player **throws on the ground are left alone**, so you can still drop things for friends next to an
inbox. (Only players running the mod mark their drops this way.)

## Settings (optional)

You can change these in r2modman under **Config editor**, file `com.darthjml.roboticinbox.cfg`
(or `BepInEx/config/com.darthjml.roboticinbox.cfg`).

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | true | Turns sorting on or off. |
| `General.ScanRadius` | 20 | How far (in meters) the inbox looks for chests. 1 floor tile = 2 m. Max 64. |
| `Inbox.Columns` / `Inbox.Rows` | 8 / 4 | Size of the inbox (8 x 4 = 32 slots). Takes effect the next time you load the world. |
| `Visual.GlowIntensity` | 3 | Brightness of the glowing runes (0 = off, max 10). Only changes how it looks for you. |
| `GroundPickup.Enabled` | true | The inbox collects loose items on the ground near it. |
| `GroundPickup.Radius` | 20 | How far (in meters) it reaches for ground items. Max 64. |
| `GroundPickup.SweepSeconds` | 2 | Seconds between ground sweeps. |
| `GroundPickup.OnlyTheseItems` | (empty) | Comma-separated item names. If set, only these are collected. |
| `GroundPickup.NeverTheseItems` | (empty) | Comma-separated item names that are never collected. |
| `Server.RequireMod` | true | Server only: kick players who don't have Robotic Inbox or have a different version. |
| `General.Debug` | false | Writes extra lines to the log for troubleshooting. |

On a multiplayer server, the **server's** `ScanRadius`, `Columns`, `Rows` and ground pickup settings
(except `SweepSeconds`) are used for everyone.

## Multiplayer

**Everyone needs it: the server and every player, all on the same version.**

- If you join a server with Robotic Inbox but you **don't have the mod**, you'll be disconnected with
  "Incompatible version". Install the mod (easiest: import the server's r2modman profile code) and join again.
- If you have a **different version** than the server, you'll get a message that tells you both versions.
- The server owner can turn this check off with `Server.RequireMod = false` (not recommended:
  players without the mod won't be able to see or use inboxes).
- Joining a server that doesn't have the mod is safe. Nothing breaks; you just play normally.

## How to install with r2modman

**From Thunderstore (easiest):**
1. Open r2modman, pick **Valheim**, and select your profile.
2. Go to **Online**, search for **RoboticInbox**, and click **Download**. BepInEx is installed for you.
3. Click **Start modded**.

**From a zip file someone sent you:**
1. Open r2modman, pick **Valheim**, and select your profile.
2. Go to **Settings** → **Import local mod**, choose the `DarthJML-RoboticInbox-1.3.0.zip` file, and confirm.
3. If you don't have **BepInExPack_Valheim** yet, install it from **Online** too.
4. Click **Start modded**.

**From a profile code (best for a group):**
1. The server owner exports their profile: **Settings** → **Export profile as code**, and shares the code.
2. Everyone else: on the profile screen click **Import / Update** → **Import new profile** → **From code**, paste it, and give it a name.
3. Select that profile and click **Start modded**. You now have exactly the same mods and versions as the server.
4. If Robotic Inbox is missing from the imported profile (the code only carries mods published on Thunderstore),
   add it with **Import local mod** as described above.

## Credits

- Original idea and *7 Days to Die* mod: **Jonathan Robertson (Kanaverum)**, MIT license.
- Valheim port and Viking Cybercrate model: **DarthJML**.

## Build from source

Requires the .NET SDK and a Valheim install with BepInEx.

```
dotnet build -c Release -p:ValheimDir="C:/Program Files (x86)/Steam/steamapps/common/Valheim"
```

The DLL lands in `bin/Release/net472/RoboticInbox.dll`. `package/` holds the Thunderstore manifest, icon and readme.
`assets/darthjml_roboticinbox_cybercrate` is the Cybercrate model as a Unity AssetBundle, embedded into the DLL.

## License

MIT. See [LICENSE](LICENSE).
