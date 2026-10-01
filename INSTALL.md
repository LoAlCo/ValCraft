# Installing ValCraft (from nothing)

ValCraft lets you play Valheim as a Minecraft player. It takes about 10 minutes the first time.

## What you need

- **Valheim** on Steam (Windows).
- **Minecraft: Java Edition**, owned on a Microsoft account. You don't need it installed:
  ValCraft brings its own copy of Minecraft and downloads the game files for you.
- About **3 GB of free RAM** on top of Valheim, and about 2 GB of disk space.

## Step 1: Install Gale (the mod manager)

1. Go to **https://github.com/Kesomannen/gale/releases/latest**.
2. Download the file ending in **`_x64-setup.exe`** and run it.
3. Open Gale and pick **Valheim** from the game list.

## Step 2: Make a profile for ValCraft

1. In Gale, click the profile name at the top left, then **New profile**.
2. Name it, for example `ValCraft`.

## Step 3: Add ValCraft

1. Download **`LoAlCo-ValCraft-<version>.zip`** from
   **https://github.com/LoAlCo/ValCraft2/releases/latest**. Don't unzip it.
2. In Gale, open the **File** menu (top left), then **Import**, then **Local mod**, and pick the zip.
3. Gale installs **BepInEx** with it automatically. If it asks to install dependencies, say yes.

## Step 4: Play

1. Click **Launch game** in Gale.
2. **The first time only**, a **Prism Launcher** window opens behind Valheim:
   - Alt-Tab to it, and click through its quick setup (the defaults are fine).
   - Click **Accounts** (top right), then **Add Microsoft**, and sign in with the account that owns Minecraft.
   - Prism downloads Minecraft, Fabric and Java. This takes a few minutes.
3. Load a Valheim world. When the top left says **"ValCraft: Minecraft is ready"**, you're a Minecraft player.

After the first time, just click **Launch game**. Minecraft starts and closes by itself.

## Controls

| Key | Does |
|---|---|
| Mouse, WASD, Space, Shift, Ctrl, 1-9, E, Q, T, F5 | Minecraft, as usual |
| G | Use Valheim things: doors, chests, portals, beds, traders |
| Esc / M | Valheim's menu / map |
| O | Minecraft's options menu |
| F7 | Switch back to normal Valheim controls (press again to return) |
| F8 | Block terrain on/off: turns Valheim's ground into real Minecraft blocks (its own save, so your normal builds are kept apart) |

## If something goes wrong

- **Only Valheim, no Minecraft:** look for a Prism Launcher window behind the game (Alt-Tab).
  It's waiting for the sign-in from Step 4.
- **"Minecraft still hasn't connected":** close Valheim, open Task Manager, end any `javaw.exe`,
  and launch again.
- **Still stuck:** in Gale, open the profile folder (Profile menu, then Open folder) and send
  `BepInEx/LogOutput.log` with your bug report at https://github.com/LoAlCo/ValCraft2/issues.

## Uninstalling

Delete the profile in Gale, then delete the folder `%LOCALAPPDATA%\ValCraft`. That folder holds
the bundled Minecraft, your Prism sign-in and your ValCraft Minecraft worlds.
