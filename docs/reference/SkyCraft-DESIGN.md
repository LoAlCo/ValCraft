# SkyCraft — Design Doc

> Play Skyrim as the main game while *being* a Minecraft player: real Minecraft movement physics, inventory, items, block placing, and combat, inside the real Skyrim world, able to fight and talk to Skyrim NPCs.

Status: draft v0.1 · 2026-09-29

---

## 1. Core principle

**Neither game is rewritten.** Minecraft runs its own, unmodified game logic: movement, collision resolution, combat math, inventory, crafting, block logic, rendering of items and hands. Skyrim runs its own world: terrain, buildings, NPCs, AI, quests, dialogue, saves.

The two mods only **translate** between them:

- Skyrim tells Minecraft *what the world is shaped like* and *where the NPCs are*.
- Minecraft tells Skyrim *where the player is*, *what the player hit*, and *what to draw on top*.

If we ever find ourselves re-implementing a Minecraft mechanic in C++ or a Skyrim mechanic in Java, the design has gone wrong.

## 2. Target environment

| Thing | Value | Notes |
|---|---|---|
| Skyrim | **Anniversary Edition runtime** (developed on 1.7.104) | SKSE64 + Address Library |
| Mod manager | MO2 or Vortex | The plugin installs like any SKSE plugin |
| Minecraft | **26.3 + Fabric** (fabric-api 0.161.0+26.3) | 26.x ships unobfuscated, so Mixins target Mojang names directly |
| Java | 25 | Minecraft 26.x needs it |
| C++ toolchain | Visual Studio 2026, CMake, Git | |

Development happens against a **clean Fabric 26.3 dev environment** (Loom `runClient`), not a modpack. Sodium and friends replace the renderer, so compat with them comes later (§12).

## 3. Components

```
┌──────────────────── Skyrim.exe ────────────────────┐        ┌──────────────── javaw.exe (Minecraft 26.3) ────────────────┐
│  skycraft.dll  (SKSE plugin, CommonLibSSE-NG)       │        │  skycraft (Fabric mod)                                     │
│                                                     │        │                                                            │
│  WorldExporter   ─ Skyrim collision near player ────┼──────▶ │  CollisionField  → injected into MC collision queries      │
│  ActorMirror     ─ nearby NPCs (pos, box, state) ───┼──────▶ │  ActorProxy entities (invisible, hittable)                │
│  InputBridge     ─ raw keyboard/mouse ──────────────┼──────▶ │  input handlers (as if MC window had focus)                │
│  HitBridge       ─ "NPC hit player for X" ──────────┼──────▶ │  player.hurt(skycraft:skyrim_* damage source)              │
│                                                     │        │                                                            │
│  PlayerPuppet    ◀─ player pos / look / pose ───────┼─────── │  real MC LocalPlayer physics                               │
│  CameraDriver    ◀─ view + projection matrix ───────┼─────── │  GameRenderer camera                                       │
│  DamageApplier   ◀─ "you hit NPC 0x1A2B3 for X" ────┼─────── │  ActorProxy.hurt() hook                                    │
│  Compositor      ◀─ color+depth textures (GPU) ─────┼─────── │  offscreen render: world layer / hand layer / GUI layer    │
└─────────────────────────────────────────────────────┘        └────────────────────────────────────────────────────────────┘
                         shared memory (Local\SkyCraft_v1) + named events + shared GPU textures
```

Plus one small shared piece: **`protocol/`**, the message schema used by both sides (§10).

## 4. Coordinate mapping

Skyrim is Z-up and Minecraft is Y-up. Scale is **1 block = 70 Skyrim units** (≈1 m). This works out neatly because the MC player is 1.8 blocks tall and the Skyrim player is about 128 units.

```
mc.x =  sky.x / 70
mc.y =  sky.z / 70
mc.z = -sky.y / 70          (Skyrim +Y is north; MC -Z is north)
mc.yaw   = f(sky.rotZ)      (exact sign/offset pinned down in Phase 0 with a test)
mc.pitch = f(sky.rotX)
```

- **Vertical range:** Skyrim terrain spans more than 384 blocks (the Throat of the World is well above MC's default build height). The mirror world therefore uses a **custom `dimension_type`** with an expanded range (up to min_y −2032 / height 4064).
- **Worldspaces:** each Skyrim worldspace (Tamriel, Solstheim, etc.) maps to its own MC dimension.
- **Interiors:** interior cells live in one `skycraft:interiors` dimension. Each interior cell gets its own 1024×1024 region slot, allocated by FormID.
- Changing cell or worldspace in Skyrim (load doors) moves the MC player to the matching dimension or slot.

## 5. The mirror world (Minecraft side)

The MC mod runs a normal singleplayer world ("mirror world") with these properties:

- **Void generator:** there are no MC blocks at all except the ones the player places.
- `doMobSpawning false`, `doWeatherCycle false`, `doDaylightCycle false`. Time and weather are driven from Skyrim.
- The integrated server runs normally. Physics runs on the client as usual in MC. Because collision injection is common code, the server's movement validation sees the same world and won't rubber-band the player.

### 5.1 CollisionField: how Skyrim's shape reaches MC physics

MC's movement code (`Entity.collide` and friends) asks the level for collision shapes (`VoxelShape`s, which are unions of AABBs) along the movement path. A Mixin appends **extra shapes from the CollisionField** to that query. MC's own collision resolution, step-up, gravity, sprint-jumping, sneaking-at-edges and so on then run completely unchanged against those shapes.

- The field is a sparse store of AABBs, bucketed per 16³ section, at **1/8-block resolution** (8.75 units).
- These shapes are **not blocks**. They don't occupy the block grid, so player blocks can be placed next to or on top of Skyrim geometry freely.
- **Slopes** become 1/8-block micro-steps. Each one is well under MC's 0.6 step height, so walking up a hill is effectively smooth. The Skyrim camera may apply *visual-only* Y smoothing; physics is untouched.
- **Steep surfaces:** to stop the player strolling up cliffs, the exporter raises any cell whose surface normal is steeper than a threshold (~50°, matching Skyrim's walkable slope) into a full wall column. MC's step-up rule then refuses it naturally. This is a data decision, not a physics change.

**Where the data comes from (Skyrim side, WorldExporter):**

| Stage | Method | Covers | Cost |
|---|---|---|---|
| A: MVP | Havok ray casts on a grid around the player (≈48-block radius, refined near the player, amortized over frames), multi-hit per column for overhangs | Terrain, most statics | Cheap; misses thin geometry |
| C: final | Walk the loaded `bhkWorld`, read the actual shapes (heightfields, compressed meshes, boxes/capsules), voxelize on a worker thread | Everything, including mod-added content and opened doors | Complex (Havok shape decoding) but exact |

Data streams as deltas per section. MC keeps a ring around the player and evicts far sections.

### 5.2 Water

Later: a Mixin on fluid-state queries reports `water` inside Skyrim water volumes. MC's swimming physics then applies unchanged.

## 6. The player

**Minecraft is authoritative for player position and physics.**

1. Each MC render frame, the mod sends `PlayerState`:
   - the interpolated position and look (MC's partial-tick render position, not the raw 20 TPS tick position)
   - pose (standing, sneaking, swimming, flying) and on-ground
   - the full **view matrix, including view bob**, plus the projection/FOV
2. The Skyrim plugin (**PlayerPuppet**):
   - disables the player's own movement (character controller input), and
   - moves the `PlayerCharacter` reference and its Havok capsule to that position every frame.

   The Skyrim player ref stays in the world as a **puppet**. That keeps NPC AI targeting, detection and stealth, trigger volumes, quest location checks, and projectiles hitting the player working.
3. **CameraDriver** forces Skyrim first person and overwrites the camera with MC's view and projection. MC's FOV is vertical and Skyrim's is effectively horizontal, so it converts between them. The Skyrim player body and arms are hidden.

## 7. Input

- The Skyrim window has OS focus. **InputBridge** reads raw input through Skyrim's input device manager, **swallows it from Skyrim's controls**, and forwards it to MC's `KeyboardHandler` / `MouseHandler` through Mixin entry points.
- The MC window is hidden but told it is focused, so it doesn't pause or release the mouse.
- **Routing modes:**
  - **Gameplay:** input goes to MC. Skyrim receives only a small allow-list: Esc for the Skyrim journal/system menu, and the Skyrim "Activate" route described below.
  - **MC screen open** (inventory, crafting, chest): input goes to MC and the MC cursor is shown in the overlay.
  - **Skyrim menu open** (dialogue, barter, lockpicking, map, loading screen): input goes to Skyrim and the MC client is frozen.
- **"Use" arbitration:** when you press MC's use key, both sides pick a target:
  - MC's ray pick (blocks and actor proxies)
  - Skyrim's crosshair pick (doors, containers, NPCs to talk to, items)

  The **nearest target wins**. A Skyrim target becomes a Skyrim `Activate`. That is how you open doors, loot, and start dialogue.

## 8. Combat

### 8.1 Skyrim NPCs inside Minecraft: ActorProxy

For every Skyrim actor within ~64 blocks, the MC server spawns a `skycraft:actor_proxy` entity:

- **Invisible**, because Skyrim draws the real NPC.
- Its **hitbox** comes from the actor's bound or race dimensions and updates each tick; position and rotation are interpolated.
- It carries the actor's FormID, a mirrored health fraction, and hostility, essential and dead flags.
- Proxies are real `LivingEntity`s. Your sword swing, attack cooldown, crits, sweeping edge, Sharpness, Fire Aspect, knockback, arrows, tridents and splash potions all work on them **with vanilla MC code**.

### 8.2 You hit an NPC

1. Vanilla MC computes the final damage on the proxy (`hurt` / actuallyHurt path).
2. The mod intercepts the result, keeps the proxy alive (it isn't the real NPC), and sends `HitActor {formId, damage, knockback, isCrit, sourceItem, fireTicks}`.
3. **DamageApplier** in Skyrim applies it through the game's own hit pipeline, so the NPC reacts properly: hit reaction and stagger, blood, sounds, aggro, and **crime/assault** if they're a citizen. The exact function will be found with RE (Ghidra is available).
   - Fallback: `DamageActorValue(Health)`, then an assault alarm, then a stagger animation event.
   - Knockback becomes a Havok impulse.
4. **Damage scaling is an open decision (§13).** A diamond sword does 7, while a Skyrim bandit has 50–300 HP.

### 8.3 An NPC hits you

1. Skyrim's hit on the player puppet (melee, arrow, spell) is caught in a hook and **cancelled on the Skyrim side**.
2. The plugin sends `PlayerHurt {amount, type, sourceFormId, direction}`.
3. MC applies `player.hurt()` with custom damage types (`skycraft:skyrim_melee`, `skyrim_arrow`, `skyrim_magic`). Armor, Protection, shields and blocking, totems, i-frames and knockback are all vanilla MC.
4. **MC health is authoritative.** Skyrim's player health is mirrored as a fraction so NPC behaviour (fleeing, finishers) still reads sensibly.
   - MC death means the Skyrim player is killed, and Skyrim's normal death/reload flow runs.
   - Fall damage is MC's own.

## 9. Rendering

Skyrim renders the world. MC renders **only its own stuff** offscreen at Skyrim's resolution, using the camera Skyrim is about to use, in three layers:

| Layer | Contents | Composited |
|---|---|---|
| **World** | Placed blocks, block entities (chests), dropped items, arrows, particles. Sky, clouds and fog are off; transparent clear | Mid-frame, **depth-tested against Skyrim's depth buffer**, so Skyrim walls correctly hide your blocks and vice versa |
| **Hand** | First-person arm and held item, including MC's swing, equip and eat animations | After Skyrim's scene, before the HUD |
| **GUI** | Hotbar, hearts, hunger, XP, crosshair, and every open MC screen | On top of everything |

- **Transport:** the color and depth textures are shared on the GPU. How depends on which renderer MC 26.3 actually uses, which needs checking:
  - **OpenGL:** `WGL_NV_DX_interop2` onto D3D11 shared textures.
  - **Vulkan:** `VK_KHR_external_memory_win32` importing D3D11 shared NT handles, plus a shared fence.
  - **Fallback:** PBO readback and upload, which costs about one frame of latency.
- **Frame lockstep:** MC's own frame cap and vsync are disabled.
  1. Skyrim signals "begin frame N" with the camera.
  2. Both games render in parallel.
  3. Skyrim waits, with a timeout, on MC's "frame N ready" fence before compositing.
  4. If MC misses the deadline, Skyrim reuses frame N−1.
- **Lighting:** MC `dayTime` is driven from Skyrim's `GameHour`, and MC weather follows Skyrim's (rain/snow → rain). That keeps block shading roughly in line with the scene.
- **Skyrim depth format and hook points** (after the scene, before post-processing and Scaleform) get pinned down with RenderDoc in Phase 2. ENB compatibility is a stretch goal.

## 10. Protocol / IPC

- **Shared memory** `Local\SkyCraft_v1` holds:
  - a **header**: magic, protocol version, both PIDs, heartbeats
  - **latest-value slots** under a seqlock, for per-frame data: `PlayerState`, `CameraState`, `FrameSync`
  - **two SPSC ring buffers** (Skyrim→MC and MC→Skyrim) for events
- **Named events** handle wakeups; heartbeats detect crashes. If either side dies, the other drops to a safe state: Skyrim restores normal control, MC pauses.
- **Schema:** it lives once in `protocol/messages.*`. It generates or is mirrored into a C++ header and a Java class, and a layout test runs in CI on both sides. Everything is little-endian with fixed-size structs, so there's no serialization library in the hot path.

Initial message catalog:

| Dir | Message | Rate |
|---|---|---|
| S→M | `Hello / Heartbeat` | 1 Hz |
| S→M | `WorldContext {worldspace/cell, GameHour, weather}` | On change |
| S→M | `CollisionSection {sectionPos, aabbs[]}` / `CollisionEvict` | Streamed |
| S→M | `ActorUpsert {formId, pos, rot, box, hpFrac, flags}` / `ActorRemove` | 20 Hz |
| S→M | `Input {keys, mouse dx/dy, wheel, buttons}` | Per frame |
| S→M | `PlayerHurt {...}` | Event |
| S→M | `BeginFrame {frameId, viewport}` | Per frame |
| S→M | `SaveRequest / LoadRequest {saveId}` | Event |
| M→S | `PlayerState {pos, look, pose, onGround, viewMtx, projMtx}` | Per frame |
| M→S | `HitActor {...}` / `UseTarget {...}` | Event |
| M→S | `BlockChange {pos, stateId}` (for Skyrim-side NPC collision) | Event |
| M→S | `FrameReady {frameId}` | Per frame |
| M→S | `MenuState {mcScreenOpen}` | On change |

## 11. Other systems

- **NPCs vs placed blocks:** MC blocks are real in MC, but Skyrim NPCs need to collide with your builds too. The plugin keeps Skyrim-side collision in sync using `BlockChange`:
  - MVP: invisible collision-only box statics.
  - Final: one merged Havok shape per chunk.
- **Save/load:** SKSE serialization stores a `saveId` in each Skyrim save. On save, MC flushes the mirror world and snapshots its (tiny, sparse) region and player data under that id. On load, it restores that snapshot, so loading an old Skyrim save also rewinds your builds and inventory consistently.
- **Launching:** the MC client must go through a real launcher for account auth.
  - v1: start the SkyCraft MC profile first. It waits in standby and Skyrim connects on game load.
  - Later: Skyrim triggers the launcher automatically.
  - During development, Loom `runClient` is enough.

## 12. Phased plan

Each phase ends in something you can actually play.

| # | Phase | "Done" when |
|---|---|---|
| 0 | **Link** | Both mods handshake over shared memory. The coordinate mapping is unit-tested. Walking in MC (on a temporary flat floor at Skyrim ground height) moves the Skyrim player |
| 1 | **Walk Skyrim in MC physics** | CollisionField stage A, CameraDriver, InputBridge. You can sprint-jump around Whiterun with real MC movement, and slopes and cliffs behave |
| 2 | **Overlay** | Hand and GUI layers composited (CPU path first, then GPU interop). The real MC hotbar and inventory screen work in Skyrim |
| 3 | **Combat** | ActorProxy, HitActor, PlayerHurt, health mirroring, death. You can fight a bandit camp with an MC sword and shield |
| 4 | **Blocks** | Place and break blocks on Skyrim surfaces, world layer depth-composited, NPCs collide with builds |
| 5 | **Full world** | CollisionField stage C (buildings, interiors, multi-level), water, activation arbitration, load doors across dimensions |
| 6 | **Persistence & polish** | Save snapshots, time/weather sync, Skyrim loot → MC items bridge, third person, auto-launch, Sodium compat |

## 13. Decisions (2026-09-29)

1. **Damage scaling:** Claude's call. MC→Skyrim damage is multiplied by `5 + 0.25 × NPC level` (a diamond sword crit of ~10 hits a level-10 bandit for ~75). Skyrim→MC damage is divided by 5, so a 20-damage Skyrim hit becomes 4 MC damage (2 hearts). Both are config values.
2. **Skyrim HUD:** keep the **compass** and the **Esc (journal/system) menu**. Hide everything else.
3. **Shouts, magic, Skyrim inventory, skill leveling:** off or ignored for now.
4. **Loot bridge:** out of scope for now.
5. **Mining Skyrim ore veins** for MC ores: parked.

## 14. Risks

| Risk | Mitigation |
|---|---|
| Havok shape extraction (stage C) is hard | Stage A ray-cast field is good enough to ship Phases 1–4 |
| Hit-pipeline function in Skyrim needs RE | Ghidra plus CommonLib; the `DamageActorValue` fallback always works |
| GPU interop across GL/Vulkan and D3D11 | CPU fallback path built first |
| Frame lockstep adds latency or stutter | Timeout plus reuse of the previous frame; measure early in Phase 2 |
| Mixin targets shift between MC versions | Pin to 26.3; keep all Mixins in one package with a target list |
| Two games' RAM and GPU cost | MC renders almost nothing (void world, no terrain); cap JVM heap at ~3 GB |

## 15. Repo layout (proposed)

```
skycraft/
  docs/DESIGN.md
  protocol/            message schema + generator + layout tests
  skse/                SKSE plugin (CMake, vcpkg, CommonLibSSE-NG, C++23)
  fabric/              Fabric mod (Gradle, Loom, MC 26.3)
  tools/               dev scripts (deploy to MO2, launch both)
```
