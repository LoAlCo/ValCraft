# ValCraft

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

The earlier, non-passthrough ValCraft (Minecraft mechanics rebuilt inside Valheim) is now
[MinecraftMode](https://github.com/LoAlCo/MinecraftMode).

## Play

**New here? Follow [INSTALL.md](INSTALL.md).** In short:

1. Download **`LoAlCo-ValCraft-<version>.zip`** from [Releases](https://github.com/LoAlCo/ValCraft/releases/latest)
   and import it into a Gale profile (File, then Import, then Local mod). It brings BepInEx with it.
   Prefer a guided setup? Run the optional **`ValCraft-Installer-<version>.exe`** from the same page
   instead: it walks you through everything, with Gale, r2modman or the Thunderstore Mod Manager.
2. Launch the game from your mod manager.
3. **First time only:** Valheim unpacks ValCraft's own Minecraft (a portable Prism Launcher with a
   ready Minecraft 26.3 + Fabric instance) to `%LOCALAPPDATA%\ValCraft` and starts Prism Launcher.
   Alt-Tab to it, click through its quick setup, and add your Microsoft account. Prism downloads
   Minecraft, Fabric and Java (a few minutes).
4. From then on it's automatic: Minecraft starts hidden with Valheim, opens a world of its own for
   each Valheim world, and quits when Valheim closes. Valheim says "ValCraft: Minecraft is ready"
   once they're linked.

**You need:** Valheim on Steam, a Microsoft account that owns Minecraft: Java Edition, and about
3 GB of free RAM for Minecraft on top of Valheim.

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

Holding a Minecraft hoe or the **Build Hammer** (Valheim's build mode):

| Key | Does |
|---|---|
| Right click | Valheim's build menu |
| Left click | Place the piece / use the hoe tool |
| Middle click | Remove a piece (hammer) |
| Alt + mouse wheel | Rotate the piece (the wheel alone still scrolls the hotbar) |
| Shift | Place freely, without snapping |

### What works

- **Movement:** Minecraft physics on Valheim's terrain, rocks, trees, buildings and dungeons, and swimming in Valheim's sea.
- **Building:** placing and breaking Minecraft blocks in Valheim's scene, lit by its sun, shadows and fog. Torches and lava light the world, and Valheim's creatures bump into your builds.
- **First person:** Minecraft's hands and held items are drawn in Valheim's scene, lit and shadowed by its sun, weather and lightning.
- **Underwater:** diving under Valheim's sea (or into Minecraft water) gets fog, a tint, the surface seen from below, and muffled world sounds.
- **Minecraft things:** entities (burning ones on fire), particles, chests and furnaces, TNT, arrows, fishing lines, leads, dropped items, and your own Minecraft body in third person.
- **Combat:** Minecraft weapons and arrows hit Valheim creatures, and their hits come back as Minecraft damage, with armor and shields.
- **Minecraft mobs:** they walk Valheim's terrain (around its rocks, logs and trees), and Minecraft's monsters and iron golems hunt Valheim's hostile creatures. How hard they plan their way is `[Mobs] Pathfinding` in the config (High, Balanced, Low) for slower PCs.
- **Tools:** axes chop trees and pickaxes mine rocks, with Valheim's tool tiers. Shovels dig soil (dirt, grass, sand, snow); rock (steep slopes and paved ground) takes a pickaxe.
- **Hoe:** a Minecraft hoe works like Valheim's: level ground, raise ground, paths and paved roads, from Valheim's own hoe menu. Stone costs come out of your Minecraft cobblestone.
- **Building (experimental):** the **Build Hammer** (crafted from planks and sticks: three planks on top, planks either side of a stick, a stick below) builds with Valheim's own pieces, menu and workbench rules. The costs are Minecraft items from your inventory, shown with Minecraft's icons (Wood = oak logs, Stone = cobblestone, Surtling Core = fire charge, ...). Every piece is unlocked for now. Creative builds for free.
- **Pausing:** pausing Valheim when you play alone pauses Minecraft too.
- **Loot:** Valheim loot goes into the Minecraft inventory (wood becomes oak logs, stone becomes cobblestone, and so on). Edit the mapping in `BepInEx/config/ValCraft.loot.txt`.
- **Block terrain (F8):** Valheim's ground becomes real Minecraft blocks you can mine and build into, by biome, with ores below. It uses its own Minecraft save per world; your inventory, stats and builds follow you across both (blocks you dig out of the terrain stay in block mode).
- **Time:** Minecraft's time of day follows Valheim's.
- **Death:** dying in Minecraft kills your Viking too, with Valheim's death screen, tombstone and respawn.
- **Multiplayer:** you can play with unmodded Valheim players. They see your Viking walk, run, swim, crouch and jump, and fights and loot sync. They don't see your Minecraft blocks or mobs.

### Settings

In `BepInEx/config/loalco.valcraft.cfg` (or your mod manager's config editor):

| Setting | Does |
|---|---|
| `[Minecraft] StartWithValheim`, `Launcher`, `Arguments` | Whether Valheim starts the bundled Minecraft, or your own launcher |
| `[Terrain] BlockTerrain` | Block terrain on at start (F8 toggles it in game) |
| `[Terrain] Radius` | How far around you (metres) the ground becomes blocks |
| `[Mobs] Pathfinding` | `High`, `Balanced` or `Low`: how hard Minecraft's mobs work out their way (Low for slower PCs) |
| `[Combat] DamageScale` | Minecraft damage times this is the damage Valheim creatures take |
| `[Combat] Range` | How far (metres) Valheim creatures can be fought from Minecraft |
| `[Debug] Diagnostics` | Extra logging, for bug reports |

`BepInEx/config/ValCraft.loot.txt` lists which Minecraft item each Valheim item becomes. Building uses the same list in reverse for its costs.

## Known issues

- Minecraft mobs sometimes spin in place for a moment when their path breaks.
- Valheim creatures don't fight back at Minecraft mobs yet.
- Minecraft items lying on the ground don't follow you across F8 (block terrain on/off).
- Building with the Build Hammer is experimental and not everything has been tried.
- Not every Valheim material has a Minecraft counterpart yet (some trophies and late-game items). Pieces that need one take it from the Valheim inventory, so they may not be buildable for now. More will be added; you can add your own in `ValCraft.loot.txt`.
- Minecraft's portals (Nether and End) don't work. They may or may not be added later.

## Layout

| Path | What |
|---|---|
| `valheim/ValCraft` | BepInEx plugin (C#): link, puppet, input, collision export, overlay, rendering, combat, loot, launcher |
| `fabric/` | Fabric mod for Minecraft 26.3 (Java 25), forked from SkyCraft |
| `protocol/valcraft_protocol.h` | Shared-memory layout, mirrored by `Link/Proto.cs` and `link/Proto.java` |
| `tools/make_hammer_texture.py` | Draws the Build Hammer's 16x16 texture |
| `installer/` | The optional step-by-step installer (WinForms, .NET Framework 4.8 built into Windows); `make_icon.py` draws its icon |
| `tools/package.py` | Builds both halves, the release zips and the installer into `dist/` |
| `tools/minecraft-bundle/` | The Prism instance and settings packed into `ValCraft-Minecraft.zip` |
| `tools/Play ValCraft.bat` | Starts Valheim with the ValCraft Gale profile |
| `tools/dev_valheim.sh`, `tools/stop_minecraft.ps1` | Dev loop: Valheim windowed into the first world; close Minecraft cleanly (it saves) |
| `docs/reference/` | SkyCraft's design doc and license |

## Build

For working on ValCraft itself. Requirements: .NET 8 SDK, JDK 25, Python 3, and Valheim with a Gale profile that has BepInExPack_Valheim
(`MC-V2` by default; `-p:ProfileDir=...` for another).

```
python tools/package.py              # build both halves -> dist/ (downloads pinned Prism + Fabric API once)
python tools/package.py --deploy     # ...and put ValCraft-Minecraft.zip into the MC-V2 profile too
dotnet build valheim/ValCraft -c Release    # plugin only, deployed into the MC-V2 profile
tools/Play ValCraft.bat              # start Valheim with the MC-V2 Gale profile (or name another)
cd fabric && ./gradlew runClient     # Minecraft dev client with the mod (JAVA_HOME = JDK 25)
bash tools/dev_valheim.sh            # Valheim windowed (1600x900), straight into the first world
```

When a Minecraft with the mod is already running (`runClient`), Valheim doesn't start the bundled
one. Either one links up by itself.

## Credits

Minecraft is © Mojang Studios / Microsoft; Valheim is © Iron Gate AB. Not affiliated with either.
Nothing from either game is included or redistributed. Prism Launcher (GPL-3.0) and Fabric API
(Apache-2.0) are downloaded unmodified at package time. Built with Claude Code (AI-assisted).
