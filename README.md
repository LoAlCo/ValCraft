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

**Changing keys:** Minecraft's own keys (movement, inventory, hotbar, ...) are changed in Minecraft's
options: press O, then Controls. ValCraft's keys above (F7, F8, G, O, M, and Alt for rotating) are in
`[Controls]` of `BepInEx/config/loalco.valcraft.cfg` (or your mod manager's config editor), as
Unity key names such as `F7`, `G` or `LeftAlt`. Changes apply right away, without restarting.

**Optional:** [ConfigurationManager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/)
isn't needed, but it's handy for changing ValCraft's settings (keys included, with a key picker)
mid-game with F1 instead of through your mod manager. ValCraft works with it: its window keeps the
keyboard and mouse away from Minecraft while it's open, and F1 doesn't also hide Minecraft's HUD.
Valheim keeps those keys, so pick ones Minecraft doesn't use; if you change Valheim's map key in
Valheim's settings, set `Map` to the same key.

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
- **Explosions:** TNT blows craters in Valheim's ground and smashes its trees, rocks and buildings; both are multipliers in `[Explosions]`. Creepers do too with `[Explosions] CreeperGriefing` on (off by default: they only hurt). The stone and wood they knock loose gathers into stacks, so a big blast doesn't leave hundreds of loose drops.
- **Boats:** Minecraft boats and rafts float on Valheim's sea, lakes and rivers, ride and tilt with its waves, and row at water speed. Place them on the water or on the shore and push them in.
- **Underwater:** diving under Valheim's sea (or into Minecraft water) gets fog, a tint, the surface seen from below, and muffled world sounds.
- **Minecraft things:** entities (burning ones on fire), particles, chests and furnaces, TNT, arrows, fishing lines, leads, dropped items, and your own Minecraft body in third person.
- **Combat:** Minecraft weapons and arrows hit Valheim creatures, and their hits come back as Minecraft damage, with armor and shields.
- **Valheim creatures and Minecraft mobs:** Valheim's monsters notice Minecraft's monsters and fight them, its animals run from them, and tamed wolves and lox defend you; hits go both ways and each side turns on whoever hit it.
- **Minecraft mobs:** they walk Valheim's terrain (around its rocks, logs and trees), and Minecraft's monsters and iron golems hunt Valheim's hostile creatures. How hard they plan their way is `[Mobs] Pathfinding` in the config (High, Balanced, Low) for slower PCs. With `[Mobs] NaturalSpawning` on they spawn by themselves: monsters at night and animals any time, picked by the Valheim biome (husks on the Plains, strays in the Mountains, slimes and witches in the Swamp, wither skeletons in the Ashlands, ...).
- **Tools:** axes chop trees and pickaxes mine rocks, with Valheim's tool tiers. Shovels dig soil (dirt, grass, sand, snow); rock (steep slopes and paved ground) takes a pickaxe.
- **Hoe:** a Minecraft hoe works like Valheim's: level ground, raise ground, paths and paved roads, from Valheim's own hoe menu. Stone costs come out of your Minecraft cobblestone.
- **Building (experimental):** the **Build Hammer** (crafted from planks and sticks: three planks on top, planks either side of a stick, a stick below) builds with Valheim's own pieces, menu and workbench rules. The costs are Minecraft items from your inventory, shown with Minecraft's icons (Wood = oak logs, Stone = cobblestone, Surtling Core = fire charge, ...). Every piece is unlocked for now. Creative builds for free.
- **Pausing:** pausing Valheim when you play alone pauses Minecraft too.
- **Bosses:** the use key (G) at an altar, boss item stand or locked door offers its item for you, from your Minecraft inventory or the hidden Valheim inventory, and says where it came from. Every boss's summoning item and drops are ValCraft Minecraft items, redrawn from Valheim's icons (Deer Trophy, Ancient Seed, Withered Bone, Dragon Egg, Fuling Totem, Sealbreaker, Bell, Hard Antler, Swamp Key, Wishbone, Dragon Tear, Torn Spirit, Majestic Carapace, Kindled Ribs and the boss trophies). Boss hits are scaled like any other creature's.
- **Valheim items:** 165 Valheim items with no Minecraft counterpart are ValCraft Minecraft items, redrawn from Valheim's icons and models: ores and metals (Flametal, Bloodgold, Black Metal Scrap, ...), every seed, every mead and mead base, and every weapon and tool. Meads drink like potions: their Minecraft effects, plus the Valheim mead's own effect on your Viking. Weapons and tools work like Minecraft's of their kind (axes chop, pickaxes mine), with their own damage, speed and durability, and their hits on Valheim's creatures do the Valheim weapon's own damage, frost, fire, poison and all.
- **Valheim armor:** every Valheim armor set is Minecraft armor (helmet, chestplate, leggings and boots; Valheim's legs piece comes with its boots), from Leather to the Deep North sets, plus the Fishing Hat, the Crown of Valheim and the Dverger Circlet. Each looks like its Valheim set when worn, painted at twice Minecraft's resolution, with its 3D parts on top: the Drake helmet's horns, antlers, the bear's head, the Protector's wings, crests and shoulder plates. Its protection, toughness and durability come from Valheim's armor. Valheim armor you pick up or craft goes to the Minecraft inventory.
- **3D weapons (optional):** the built-in **ValCraft 3D Weapons** resource pack (Options, Resource Packs) shows every Valheim weapon and tool as a blocky 3D model in your hand and on the ground, built from the Valheim weapon's shape and held at its real size; gems and runes glow. The inventory keeps the flat icons.
- **Loot:** Valheim loot goes into the Minecraft inventory (wood becomes oak logs, stone becomes cobblestone, and so on). Edit the mapping in `BepInEx/config/ValCraft.loot.txt`.
- **Block terrain (F8):** Valheim's ground becomes real Minecraft blocks you can mine and build into, by biome, with ores below. It uses its own Minecraft save per world; your inventory, stats and builds follow you across both (blocks you dig out of the terrain stay in block mode). The blocks go down to bedrock where Valheim's own digging stops (8 m under its ground), and they follow changes made to Valheim's ground in the meantime (TNT craters, digging, the hoe) without touching your builds.
- **Time:** Minecraft's time of day follows Valheim's, and Minecraft's `/time set` and `/time add` move Valheim's clock forward too (the host's).
- **Boss bars:** Minecraft's bosses (the Wither, the Ender Dragon, the Warden, the Elder Guardian) and raids get Valheim's boss health bar instead of Minecraft's.
- **Fire:** Minecraft's fire, lava, fire charges, Fire Aspect and Flame set Valheim's creatures burning with Valheim's flames (Surtlings and other fire creatures don't mind), a torch hit sets any mob alight, and fire spreads between Minecraft's wooden builds and Valheim's wooden buildings and trees both ways.
- **Light:** a torch in your hand lights the area around you, flickering like Valheim's torch (soul torches blue), and placed torches light Valheim's world like its own. Lava, magma, glowstone and other bright blocks glow.
- **Weather:** Minecraft's weather follows Valheim's: rain or snow makes it rain (so monsters don't burn in the day), a thunderstorm makes it thunder.
- **Items across F8:** items lying on the ground stay where they are when you switch block terrain on or off.
- **Boats:** in a Minecraft boat, other players see your Viking sitting in it.
- **Death:** dying in Minecraft kills your Viking too, with Valheim's death screen, tombstone and respawn.
- **Multiplayer (experimental, not fully tested yet):** everyone with ValCraft in the same Valheim world plays in one Minecraft world, so you see each other's Minecraft avatars, blocks, mobs and items. It happens on its own: the first ValCraft player in opens their Minecraft world to the others through [e4mc](https://e4mc.link) (included), and everyone after joins it; nobody types an address. Each player keeps their own Valheim underneath, so each one's loot, building costs, meads, skills and hits on Valheim's creatures go to their own Viking, and the host's world knows the ground and creatures around every player, not just the host. Another ValCraft player's Viking is hidden (their Minecraft avatar is where they are). Players without ValCraft see Vikings as usual, and fights and loot sync with them. `/join <address>` and `/leave` still pick a world by hand.

### Settings

In game: **ValCraft settings** in Valheim's pause menu (Esc) changes them on the spot (the Minecraft and Debug ones under **Advanced settings**), and its **Minecraft options** button opens Minecraft's own options. They're also in `BepInEx/config/loalco.valcraft.cfg` (or your mod manager's config editor, or the optional ConfigurationManager, see Controls):

| Setting | Does |
|---|---|
| `[Minecraft] StartWithValheim`, `Launcher`, `Arguments` | Whether Valheim starts the bundled Minecraft, or your own launcher |
| `[Terrain] BlockTerrain` | Block terrain on at start (F8 toggles it in game) |
| `[Terrain] Radius` | How far around you (metres) the ground becomes blocks |
| `[Mobs] Pathfinding` | `High`, `Balanced` or `Low`: how hard Minecraft's mobs work out their way (Low for slower PCs) |
| `[Mobs] NaturalSpawning` | Minecraft mobs spawn on their own in Valheim's world, by biome (default off) |
| `[Combat] DamageScale` | Minecraft damage times this is the damage Valheim creatures take |
| `[Combat] Range` | How far (metres) Valheim creatures can be fought from Minecraft |
| `[Explosions] Damage` | Multiplier for the damage Minecraft explosions (TNT, creepers) do to Valheim's creatures, trees, rocks and buildings (default 2) |
| `[Explosions] Craters` | Multiplier for the size of the craters explosions blow in Valheim's ground (default 1; 0 = no craters) |
| `[Explosions] CreeperGriefing` | Creeper explosions break the environment, in Valheim and in block terrain (Minecraft's mobGriefing rule; default off) |
| `[Multiplayer] SharedWorld` | Everyone with ValCraft in the Valheim world plays in one Minecraft world (default on); off: your own |
| `[Camera] FOV` | Field of view, the same setting as Minecraft's FOV option: changing either changes both |
| `[Camera] ViewmodelFOV`, `LockViewmodelFOV` | The field of view your hands and held item are drawn at; locked (default) it follows FOV |
| `[Controls] ValheimControls`, `BlockTerrain`, `Use`, `MinecraftOptions`, `Map`, `BuildRotate` | ValCraft's own keys (F7, F8, G, O, M, Alt); see Controls |
| `[Debug] Diagnostics` | Extra logging, for bug reports |
| `[Debug] ExportIcons`, `ExportData`, `ExportRenders`, `ExportModels` | For making ValCraft's Minecraft versions of Valheim items: save Valheim's icons, item/recipe/creature tables, renders of item models, and samples of their shapes |

`BepInEx/config/ValCraft.loot.txt` lists which Minecraft item each Valheim item becomes. Building uses the same list in reverse for its costs.

## Known issues

- You can sometimes slip through the floor of a steep crater in Valheim terrain; Valheim's rescue puts you back on top.
- Bows, arrows, shields, staffs, bombs and capes don't have ValCraft versions yet (they stay in the Valheim inventory); nor do the cosmetic tunics, dresses and hats.
- Valheim armor's upgrade levels and set bonuses don't carry over to its Minecraft version yet.
- Building with the Build Hammer is experimental and not everything has been tried.
- Not every Valheim material has a Minecraft counterpart yet (some trophies and late-game items). Pieces that need one take it from the Valheim inventory, so they may not be buildable for now. More will be added; you can add your own in `ValCraft.loot.txt`.
- Valheim's creatures don't bump into Minecraft boats.
- Multiplayer: the shared Minecraft world is the host's, so it closes when the host leaves; everyone else goes back to their own and the next ValCraft player in hosts. Builds made in it are saved in the host's world.
- Multiplayer: everyone should use the same terrain mode (F8); the shared world is in the host's.
- The first time a world is opened to friends, Windows may ask whether Java may use the network. e4mc works either way.
- Minecraft's portals (Nether and End) don't work. They may or may not be added later.

## Layout

| Path | What |
|---|---|
| `valheim/ValCraft` | BepInEx plugin (C#): link, puppet, input, collision export, overlay, rendering, combat, loot, launcher |
| `fabric/` | Fabric mod for Minecraft 26.3 (Java 25), forked from SkyCraft |
| `protocol/valcraft_protocol.h` | Shared-memory layout, mirrored by `Link/Proto.cs` and `link/Proto.java` |
| `tools/make_hammer_texture.py`, `tools/make_item_textures.py` | Draw ValCraft's hand-made 16x16 item textures (Build Hammer, Deer Trophy, Fuling Totem, Wishbone) |
| `tools/pixelize_icons.py` | Makes the other boss items' 16x16 textures from Valheim's icons (exported with `[Debug] ExportIcons`; the icons themselves stay out of the repo) |
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
