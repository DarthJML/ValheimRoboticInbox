# Sharing Robotic Inbox with your Discord group

The file to share is **`DarthJML-RoboticInbox-1.3.0.zip`** (in this folder). It's a normal Thunderstore package:
r2modman can import it directly, and Thunderstore accepts it as-is.

**Golden rule:** the server and every player need the mod, and everyone must have the **same version**.
If someone's missing it, the server kicks them with "Incompatible version".

---

## A. Publish on Thunderstore (best option: everyone installs with one click)

1. Go to <https://thunderstore.io> and click **Login** (top right). Sign in with Discord or GitHub.
2. Create your team (this is your "author name"):
   - Click your profile picture → **Teams** → **Create team**.
   - Name it exactly **`DarthJML`**. The name is permanent, so check the spelling.
3. Upload:
   - Click **Upload** (top bar), or go to <https://thunderstore.io/package/create/>.
   - **Team:** `DarthJML`. **Community:** `Valheim`.
   - **Categories:** pick something like *Mods*, *Building*, *Server-side*, *Client-side*.
   - Leave "Contains NSFW content" unchecked.
   - Drag in `DarthJML-RoboticInbox-1.3.0.zip` and click **Submit**.
4. Thunderstore checks the file automatically. After a few minutes it shows up at
   `https://thunderstore.io/c/valheim/p/DarthJML/RoboticInbox/` and in r2modman's **Online** tab.
5. **Updates later:** every upload needs a new version number (1.2.1, 1.3.0, …). You can't upload 1.3.0 again.

## B. Without Thunderstore: send the zip

1. Post `DarthJML-RoboticInbox-1.3.0.zip` in Discord (it's about 6 MB).
2. Each player:
   1. Opens **r2modman** → **Valheim** → selects their profile.
   2. Makes sure **BepInExPack_Valheim** is installed (**Online** tab → search "BepInExPack Valheim" → Download).
   3. **Settings** → **Import local mod** → picks the zip → confirms.
   4. Clicks **Start modded**.
3. The server needs it too (see D for setting up a server with BepInEx).

## C. Share an r2modman profile code (everyone gets the same mods)

1. In r2modman, select the profile you play with.
2. **Settings** → **Export profile as code**. r2modman copies a code to your clipboard. Paste it in Discord.
3. Friends: on r2modman's profile screen, **Import / Update** → **Import new profile** → **From code**,
   paste the code, name the profile, select it, then **Start modded**.

**Important:** a profile code only re-downloads mods that are **on Thunderstore**. Until Robotic Inbox is
published (option A), friends must also do **Import local mod** with the zip (option B) after importing the code.
Codes expire after a while, so make a new one if someone says it doesn't work.

## D. Test multiplayer on your own PC

You can run a real dedicated server on your PC and join it from your own game.

### 1. Install the server
1. In Steam, open **Library**, change the filter from "Games" to **Tools** (or include tools).
2. Install **Valheim Dedicated Server** (free, app 896660).
3. Right-click it → **Manage** → **Browse local files**. Keep this folder open (the "server folder").

### 2. Put BepInEx + the mod on the server
The quickest way is to copy your r2modman profile, so the server has the same mods as you:
1. In r2modman: **Settings** → **Browse profile folder**.
2. Copy **everything** in that folder (`BepInEx`, `winhttp.dll`, `doorstop_config.ini`, `doorstop_libs`, …)
   into the server folder. Say yes if Windows asks to merge folders.
3. Check that `BepInEx\plugins\` in the server folder has the Robotic Inbox DLL (`RoboticInbox.dll`).
   If not, open the zip and copy `RoboticInbox.dll` into `BepInEx\plugins\` yourself.

### 3. Start the server
1. In the server folder, right-click `start_headless_server.bat` → **Edit**. Make the `valheim_server` line look like this:
   ```
   valheim_server -nographics -batchmode -name "RI Test" -port 2456 -world "RITest" -password "testpass1" -public 0
   ```
   (The password needs at least 5 characters and can't contain the server name.)
2. Double-click `start_headless_server.bat`. A black console window opens.
3. Wait until you see **`RoboticInbox v1.3.0 loaded.`** near the top and, later, a line containing
   **`Game server connected`**. If Windows Firewall asks, click **Allow**.

### 4. Join it with the mod (should work)
1. r2modman → **Start modded**.
2. **Start Game** → pick a character → **Join Game** tab → **Join IP** (or **Add server**) → type `127.0.0.1:2456` → connect → password `testpass1`.
3. You should spawn normally. Build a Robotic Inbox and a chest with some Wood in it, drop Wood into the inbox, close it: the Wood should move.
4. The server console should show nothing about Robotic Inbox rejecting you.

### 5. Join it WITHOUT the mod (should be kicked)
1. Close Valheim. In r2modman click **Start vanilla** (starts the game with no mods).
2. Join `127.0.0.1:2456` again.
3. Expected: you're sent back to the main menu with **"Incompatible version"**.
   The server console shows: `RoboticInbox: client … does not have RoboticInbox (server has v1.3.0); disconnecting`.
4. Optional: set `RequireMod = false` in the server's `BepInEx\config\com.darthjml.roboticinbox.cfg`, restart the server,
   and try again. Now the vanilla game is allowed in, and the server only logs a warning.

### 6. Modded game on a server WITHOUT the mod (should be harmless)
Join any vanilla server (or temporarily move `RoboticInbox.dll` out of the server's `BepInEx\plugins\` and restart it).
You should play normally. Your log says `this server does not run RoboticInbox`.

### Where the logs are
- Server: the console window, and `BepInEx\LogOutput.log` in the server folder.
- Your game: r2modman → **Settings** → **Browse profile folder** → `BepInEx\LogOutput.log`.

### Stopping the server
Click the console window and press **Ctrl+C** once, then wait for it to save and close. Don't just close the window
or you may lose the test world's progress.
