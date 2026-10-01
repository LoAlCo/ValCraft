# ValCraft 2

A passthrough mod: Minecraft and Valheim run at the same time and talk through shared memory.
You play Valheim's world as a Minecraft player, with Minecraft's movement, inventory, HUD, hand,
blocks, combat and chat inside Valheim's terrain, lighting, weather, creatures and dungeons.

Neither game is rewritten. Minecraft (with the ValCraft Fabric mod) runs hidden and simulates the
player. A BepInEx plugin in Valheim:
- sends Minecraft Valheim's collision around the player;
- puppets the Valheim character to where Minecraft says;
- draws Minecraft's blocks, entities, particles, items and HUD in Valheim's frame;
- carries combat and loot between the two.

Based on [SkyCraft](https://github.com/chasmlol/SkyCraft) by chasmlol (Minecraft in Skyrim). The
Fabric mod in `fabric/` is a fork of SkyCraft's (MIT, see [NOTICE.md](NOTICE.md)). The Valheim side
is new.

## Play

**New here? Follow [INSTALL.md](INSTALL.md)**: download the release zip, import it into Gale, and launch.
The rest of this section covers the details.

**You need:** Valheim on Steam, a Microsoft account that owns Minecraft: Java Edition, and Gale (or
r2modman) with a profile containing BepInExPack_Valheim. Budget about 3 GB of extra RAM for Minecraft.

1. Build the package (`python tools/package.py`, see below) and import
   `dist/LoAlCo-ValCraft-<version>.zip` into a Gale profile (Import local mod). It contains the
   plugin and `ValCraft-Minecraft.zip`: a portable Prism Launcher with a ready Minecraft 26.3 +
   Fabric instance.
2. Launch the game from Gale, or double-click `tools/Play ValCraft.bat`. The script uses the
   `MC-V2` profile; give it another profile's name as an argument to use that one.
3. **First time only:** Valheim unpacks Minecraft to `%LOCALAPPDATA%\ValCraft` and starts Prism
   Launcher. Alt-Tab to the Prism window, click through its quick setup, and add your Microsoft
   account. Prism downloads Minecraft, Fabric and Java (a few minutes).
4. From then on it's automatic: Minecraft starts hidden with Valheim, opens a world of its own for
   each Valheim world, and quits when Valheim closes. Valheim says "ValCraft: Minecraft is ready"
   once they're linked.

To use your own launcher instead, set `[Minecraft] Launcher` / `Arguments` in
`BepInEx/config/loalco.valcraft.cfg`, or `StartWithValheim = false` and start Minecraft yourself.

### Controls (while Minecraft drives)

| Key | Does |
|---|---|
| Mouse, WASD, Space, Shift, Ctrl, 1-9, E, Q, T, F5 | Minecraft, as usual |
| G | Valheim "use": doors, chests, portals, beds, traders |
| Esc / M | Valheim's menu / map |
| O | Minecraft's options menu |
| F7 | Hand the controls back to Valheim (and again to return) |
| F8 | Block terrain on/off: Valheim's ground as real Minecraft blocks |

### What works

- **Movement:** Minecraft physics on Valheim's terrain, rocks, trees, buildings and dungeons, and swimming in Valheim's sea.
- **Building:** placing and breaking Minecraft blocks in Valheim's scene, lit by its sun, shadows and fog. Torches and lava light the world, and Valheim's creatures bump into your builds.
- **Minecraft things:** entities, particles, chests and furnaces, TNT, arrows, dropped items, and your own Minecraft body in third person.
- **Combat:** Minecraft weapons and arrows hit Valheim creatures, and their hits come back as Minecraft damage, with armor and shields.
- **Tools:** axes chop trees, pickaxes mine rocks and dig terrain, with Valheim's tool tiers.
- **Loot:** Valheim loot goes into the Minecraft inventory (wood becomes oak logs, stone becomes cobblestone, and so on). Edit the mapping in `BepInEx/config/ValCraft.loot.txt`.
- **Block terrain (F8):** Valheim's ground becomes real Minecraft blocks you can mine and build into, by biome, with ores below. It uses its own Minecraft save per world, so normal-mode builds stay apart.
- **Time:** Minecraft's time of day follows Valheim's.
- **Multiplayer:** you can play with unmodded Valheim players. They see your Viking walk, run, swim, crouch and jump, and fights and loot sync. They don't see your Minecraft blocks.

## Layout

| Path | What |
|---|---|
| `valheim/ValCraft` | BepInEx plugin (C#): link, puppet, input, collision export, overlay, rendering, combat, loot, launcher |
| `fabric/` | Fabric mod for Minecraft 26.3 (Java 25), forked from SkyCraft |
| `protocol/valcraft_protocol.h` | Shared-memory layout, mirrored by `Link/Proto.cs` and `link/Proto.java` |
| `tools/package.py` | Builds both halves and the release zips into `dist/` |
| `tools/minecraft-bundle/` | The Prism instance and settings packed into `ValCraft-Minecraft.zip` |
| `tools/Play ValCraft.bat` | Starts Valheim with the ValCraft Gale profile |
| `tools/dev_valheim.sh`, `tools/stop_minecraft.ps1` | Dev loop: Valheim windowed into the first world; close Minecraft cleanly (it saves) |
| `docs/reference/` | SkyCraft's design doc and license |

## Build

Requirements: .NET 8 SDK, JDK 25, Python 3, and Valheim with a Gale profile that has BepInExPack_Valheim
(`MC-V2` by default; `-p:ProfileDir=...` for another).

```
python tools/package.py              # build both halves -> dist/ (downloads pinned Prism + Fabric API once)
python tools/package.py --deploy     # ...and put ValCraft-Minecraft.zip into the MC-V2 profile too
dotnet build valheim/ValCraft -c Release    # plugin only, deployed into the MC-V2 profile
cd fabric && ./gradlew runClient     # Minecraft dev client with the mod (JAVA_HOME = JDK 25)
bash tools/dev_valheim.sh            # Valheim windowed (1600x900), straight into the first world
```

When a Minecraft with the mod is already running (`runClient`), Valheim doesn't start the bundled
one. Either one links up by itself.

## Credits

Minecraft is © Mojang Studios / Microsoft; Valheim is © Iron Gate AB. Not affiliated with either.
Nothing from either game is included or redistributed. Prism Launcher (GPL-3.0) and Fabric API
(Apache-2.0) are downloaded unmodified at package time. Built with Claude Code (AI-assisted).
