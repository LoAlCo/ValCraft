# ValCraft QA checklist

What's waiting for a test in the game, and the known issues still open. Tick a line when it works ([~]: partly);
note what you saw when it doesn't (the Valheim log is `BepInEx/LogOutput.log` in the Gale profile,
the Minecraft log is in the ValCraft instance's `logs` folder).

Start Valheim from Gale after an update: the first start installs the new Minecraft.

## Waiting for a test

### Boss bars (tested OK)
- [x] `/summon wither`: Valheim's boss bar, no purple Minecraft bar; drains with hits; goes on death or 100+ blocks away
- [x] Warden and Elder Guardian get bars within ~40 blocks

### Fire
- [ ] Set a Greyling or Boar alight (flint and steel, a fire block, lava): Valheim's flames on it, burn damage for several seconds
- [ ] In lava it keeps burning without the damage piling up
- [ ] Fire charge, Fire Aspect, Flame bow: each sets it burning
- [x] A torch hit: it burns about 1.5 s only
- [ ] Minecraft fire beside a Valheim wooden building or tree: Valheim's fire takes hold and spreads (log: `fire:`)
- [ ] A Valheim fire beside a Minecraft wooden build: the blocks catch
- [ ] Surtlings don't burn
- [ ] Flint and steel on Valheim ground: the fire burns a few seconds and goes out

### Light
- [x] A torch in hand at night: light follows you, flickering like Valheim's torch; soul torch is blue
- [ ] Placed torches light the area like Valheim's torches, the nearest two with shadows (log: `light:`)

### Fixes for known issues
- [~] **Mobs spinning:** improved (they get unstuck quickly most of the time), not fixed yet; more later
- [ ] **Crash fix:** no more crash when a mob reaches the end of its path (it crashed during the mob fights; fixed)
- [x] **Sledges in first person:** sledges (and the big spiky maces) are smaller in first person; swords, axes and picks unchanged
- [ ] **Boats:** in a Minecraft boat, a friend sees your Viking sitting, not standing in the water (log: `boat:`)
- [ ] **Items across F8:** drop some items, press F8: they lie in the same place in the other mode; and back again
- [ ] **Combat balance:** a wooden axe no longer one-shots a Greyling; an iron sword feels like Valheim's iron sword; netherite does real damage to late-game creatures (`[Combat] DamageScale` still scales it all)
- [ ] **Crafting with Minecraft's materials:** at a workbench, recipes whose materials are in the Minecraft inventory show as craftable (not red); crafting takes them from Minecraft
- [ ] **Sealbreaker and Fader's bells:** craft them from their fragments at their stations; the key / bell goes to the Minecraft inventory and works at its door / altar
- [ ] **Loot table catch-up:** the Valheim log says `loot bridge: added the lines older ValCraft sections were missing` once (Sealbreaker fragments, Moder's tear); after that, those drops go to Minecraft
- [ ] **Dev script:** `tools/dev_valheim.sh` gets into the world on Valheim 1.0 (my tool, not yours)

### Valheim armor
- [x] Creative menu, Combat tab: the Valheim armor (79 pieces with the boots); each has its own icon
- [x] Wear each set (F5 to see yourself): it looks like the Valheim set (some clipping, as Minecraft's own armor has)
- [ ] **Z-fighting fixed:** no flickering where the 3D parts meet the armor (helm points, Drake ridge, crown points, Protector crest, fishing hat); tell me any piece that still flickers
- [ ] The 3D parts sit right: Drake horns, Flametal and Caller antlers, bear ears and snout, Protector wings, crests, points, the fishing hat's brim, shoulder plates and fur mantles (and face forward)
- [ ] Armor stands, zombies and skeletons wearing it show the same
- [ ] Valheim sees it too: your avatar in Valheim (F5) and friends' avatars wear it
- [ ] Protection feels right: Valheim's hits hurt less in heavier sets (leather < bronze < iron < padded < flametal)
- [ ] Craft a Valheim armor piece at the workbench: it goes into the Minecraft inventory; pick one up: same
- [ ] Repairing in an anvil works (leather sets with leather, metal sets with iron or netherite ingots)

### Valheim creatures vs Minecraft mobs
- [x] Greydwarves, skeletons, trolls etc. notice a Minecraft zombie / skeleton / creeper near them and attack it
- [ ] Their hits hurt the Minecraft mob (red flash, it dies eventually), and the mob turns on that creature
- [ ] A Minecraft mob attacking a Valheim creature makes it fight back at that mob
- [ ] Deer and other Valheim animals run from Minecraft monsters
- [ ] Tamed wolves and lox defend you against Minecraft monsters
- [ ] Valheim monsters ignore nothing they shouldn't: they still go for you when no Minecraft mob is closer
- [ ] No errors in the Valheim log mentioning "mob stand-in"

### This round's fixes
- [ ] Held torch lights a wider area (1.5x)
- [ ] A torch hit sets any mob alight (cows, zombies...) for about 1.5 s
- [ ] Every armor set has boots (`/give @s valcraft:iron_boots`); leggings from Valheim come with their boots
- [ ] The repainted sets match Valheim's (Leather, Troll, Padded, Eitr-weave, Caller, Protector, Vanguard, Embla, Carapace, Fenris, Bear)

### Multiplayer (experimental, needs friends)
- [ ] Friends with 0.7.0+ joining your Valheim world end up in your Minecraft world within ~10 s
- [ ] You see each other's Minecraft avatars, not Vikings; blocks, loot, hits and meads work for guests

## Still open

- **Slipping through a steep crater floor** (Valheim's rescue puts you back). Needs a repro: where it happened (which crater, how you walked in) and the Valheim log.
- **Valheim creatures don't bump into Minecraft boats.** Needs colliders for Minecraft's boats on the Valheim side.
- **176 Valheim materials have no Minecraft counterpart** (fish, meats, hides, trophies, molds, spices, ...). They stay in the Valheim inventory, where crafting finds them, so nothing breaks; mapping them is a design choice (one Minecraft item per Valheim item, see the inventory-sync notes).
- **Bows, arrows, shields, staffs, bombs, capes** and the cosmetic tunics, dresses and hats have no ValCraft versions yet. Capes: a decision is needed (the chestplate slot, or a cape slot of their own).
- **Valheim armor's upgrade levels and set bonuses** don't carry over yet.
- **Mob pathing** over Valheim's ground: better, still to improve.
- **Weak weapon designs:** fists, Berserkir axes, Early Axes, Skoll and Hati.
- **Minecraft's portals** (Nether, End) don't work.
- **Multiplayer by design:** the shared world is the host's (it closes when they leave); everyone should be in the host's terrain mode (F8).
