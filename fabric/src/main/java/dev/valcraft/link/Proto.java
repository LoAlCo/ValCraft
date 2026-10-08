package dev.valcraft.link;

/**
 * Mirror of protocol/valcraft_protocol.h. Keep the two in sync.
 */
public final class Proto {
	private Proto() {
	}

	public static final int MAGIC = 0x434C4156; // "VALC"
	public static final int VERSION = 10;
	// A second client on the same PC (multiplayer testing) talks to its own stand-in Valheim:
	// -Dvalcraft.link=Local\ValCraft_guest (see tools/fake_guest.py).
	public static final String MAPPING_NAME = System.getProperty("valcraft.link", "Local\\ValCraft_v1");
	public static final double UNITS_PER_BLOCK = 1.0; // Valheim: 1 unit = 1 m = 1 block

	public static final long OFF_HEADER = 0x0;
	public static final long OFF_VAL_STATE = 0x100;
	public static final long OFF_MC_STATE = 0x200;
	public static final long OFF_WATER_GRID = 0x400;
	public static final int WATER_GRID_SIZE = 16;
	public static final long WG_SEQ = 0x0, WG_ORIGIN_X = 0x4, WG_ORIGIN_Z = 0x8, WG_WORLD_ID = 0xC, WG_SURFACE = 0x10;
	public static final long OFF_OVERLAY_CTL = 0x300;
	public static final long OFF_OVERLAY_SLOT_HDR = 0x340;
	public static final long OFF_INPUT_RING = 0x1000;
	public static final long OFF_COLLISION_RING = 0x20000;
	public static final long COLLISION_RING_BYTES = 32L << 20;
	public static final long OFF_OVERLAY_PIXELS = OFF_COLLISION_RING + COLLISION_RING_BYTES;
	public static final int MAX_OVERLAY_W = 3840;
	public static final int MAX_OVERLAY_H = 2160;
	public static final long OVERLAY_SLOT_BYTES = (long) MAX_OVERLAY_W * MAX_OVERLAY_H * 4;
	public static final int OVERLAY_SLOTS = 3;
	public static final long OFF_ACTOR_TABLE = 0x12000;
	public static final long OFF_EVENT_RING = 0x17000;
	public static final long OFF_WORLD_ENTITIES = 0x1C000;
	public static final long OFF_RENDER_RING = OFF_OVERLAY_PIXELS + OVERLAY_SLOT_BYTES * OVERLAY_SLOTS;
	public static final long RENDER_RING_BYTES = 64L << 20;
	public static final long MAPPING_BYTES = OFF_RENDER_RING + RENDER_RING_BYTES;

	// Input types added in v5
	public static final int IN_HURT = 7;
	public static final int IN_OPEN_MENU = 8;
	// ValCraft loot bridge: a = count (negative: take that many, for Valheim building costs), b = item id length (UTF-8);
	// IN_GIVE_DATA events follow with 12 id bytes each in a, b, c
	public static final int IN_GIVE = 9;
	public static final int IN_GIVE_DATA = 10;
	/** ValCraft: a Valheim fire at a/b/c (MC coords x 8): Minecraft's flammable blocks and mobs there catch fire (FireBridge). */
	public static final int IN_VALHEIM_FIRE = 11;
	/** ValCraft: a Valheim creature hit a Minecraft mob: a = entity id, b = Valheim damage x 100, c = the creature's form id. */
	public static final int IN_MOB_HIT = 12;
	public static final int HURT_MELEE = 0;
	public static final int HURT_PROJECTILE = 1;
	public static final int HURT_MAGIC = 2;
	public static final int HURT_OTHER = 3;
	public static final int HURT_BLOCKED_IN_VALHEIM = 1;
	public static final int HURT_POWER_ATTACK = 2;

	// Actor table (relative to OFF_ACTOR_TABLE)
	public static final int MAX_ACTORS = 256;
	public static final long AT_SEQ = 0x00;
	public static final long AT_COUNT = 0x04;
	public static final long AT_RECORDS = 0x40;
	public static final long ACTOR_RECORD_BYTES = 64;
	public static final int ACTOR_HOSTILE = 1;
	public static final int ACTOR_DEAD = 1 << 1;
	public static final int ACTOR_ESSENTIAL = 1 << 2;
	public static final int ACTOR_IN_COMBAT = 1 << 3;

	// Event ring (relative to OFF_EVENT_RING)
	public static final int EVENT_RING_ENTRIES = 512;
	public static final long ER_HEAD = 0x00;
	public static final long ER_TAIL = 0x40;
	public static final long ER_DATA = 0x80;
	public static final long EVENT_BYTES = 32;
	public static final int EV_HIT_ACTOR = 1;
	public static final int EV_PLAYER_DIED = 2;
	public static final int EV_EXPLOSION = 3;
	/** EV_EXPLOSION flags: it broke no blocks (a creeper with mobGriefing off), so it hurts creatures only. */
	public static final int EXPLOSION_KEEPS_BLOCKS = 1;
	public static final int EV_ARROW_STUCK = 4;
	public static final int EV_SKILL_USE = 5;
	// ValCraft: a swing at Valheim's world. formId = tool (TOOL_* | tier << 4), a/b/c = where it hit (MC), d = attack strength (0..1)
	public static final int EV_VALHEIM_HIT = 6;
	/** BuildSync: formId = 1 when builds from the other terrain mode start arriving, 0 once they're in. */
	public static final int EV_BUILD_SYNC = 7;
	/** TimeSync: a /time command; a = hour of day (0-24, Valheim's), b = whole days to skip besides. */
	public static final int EV_SET_TIME = 9;
	/** ValheimItems: drank a Valheim mead; formId = its Valheim prefab name's String.hashCode(). */
	public static final int EV_CONSUME = 10;
	/** Just before an EV_HIT_ACTOR made with a ValCraft Valheim weapon: formId = its prefab's hash, a = its Minecraft damage. */
	public static final int EV_HIT_WEAPON = 11;
	/** ValCraft: a Valheim actor's stand-in caught fire: formId, a = seconds it burns. Valheim sets the creature burning. */
	public static final int EV_IGNITE = 12;
	/** EV_HIT_ACTOR flag: the hit is fire alone (lava, fire, fireballs, magma): Valheim deals it as fire damage only. */
	public static final int HIT_PURE_FIRE = 1 << 5;
	public static final int TOOL_NONE = 0, TOOL_SWORD = 1, TOOL_AXE = 2, TOOL_PICKAXE = 3, TOOL_SHOVEL = 4, TOOL_HOE = 5;
	// Valheim skills (ActorValue) Minecraft reports use of; weapon skills come from EV_HIT_ACTOR.
	public static final int SKILL_BLOCK = 9;
	public static final int SKILL_SMITHING = 10;
	public static final int SKILL_HEAVY_ARMOR = 11;
	public static final int SKILL_LIGHT_ARMOR = 12;
	public static final int HIT_CRITICAL = 1;
	public static final int HIT_PROJECTILE = 1 << 1;
	public static final int HIT_SWEEP = 1 << 2;
	public static final int HIT_FIRE = 1 << 3;
	/** Hit by a Minecraft mob, not the player: no attacker in Valheim, no skill. */
	public static final int HIT_MOB = 1 << 4;
	public static final int WEAPON_UNARMED = 0;
	public static final int WEAPON_BLADE = 1;
	public static final int WEAPON_AXE = 2;
	public static final int WEAPON_BLUNT = 3;
	public static final int WEAPON_PIERCE = 4;
	public static final int WEAPON_ARROW = 5;

	// World entities (relative to OFF_WORLD_ENTITIES)
	public static final int MAX_WORLD_ENTITIES = 160;
	// ValCraft: boss bars (see BossTable): seq, count, then MAX_BOSSES records of BOSS_RECORD_BYTES
	public static final long OFF_BOSS_TABLE = 0x1FD00;
	public static final int MAX_BOSSES = 8;
	public static final long BOSS_RECORDS = 0x10;
	public static final int BOSS_RECORD_BYTES = 64;
	public static final int BOSS_NAME_BYTES = 48;
	public static final int BOSS_OTHER = 0, BOSS_WITHER = 1, BOSS_DRAGON = 2, BOSS_WARDEN = 3, BOSS_ELDER_GUARDIAN = 4, BOSS_RAID = 5;
	public static final long WE_SEQ = 0x00;
	public static final long WE_COUNT = 0x04;
	public static final long WE_HAS_SELECTION = 0x08;
	public static final long WE_SEL_MIN = 0x0C;
	public static final long WE_SEL_MAX = 0x18;
	public static final long WE_RECORDS = 0x40;
	public static final long WORLD_ENTITY_BYTES = 96;
	public static final int WE_ARROW = 1;
	public static final int WE_ITEM = 2;
	public static final int WE_TRIDENT = 3;
	public static final int WE_BLOCK = 4;
	public static final int WE_CRACK = 5;
	public static final int WE_SHADOW = 6;

	// Render ring (relative to OFF_RENDER_RING)
	public static final long RR_HEAD = 0x00;
	public static final long RR_TAIL = 0x40;
	public static final long RR_DATA = 0x80;
	public static final long RR_DATA_BYTES = RENDER_RING_BYTES - RR_DATA;
	public static final int REN_PAD = 0;
	public static final int REN_ATLAS = 1;
	public static final int REN_SECTION = 2;
	public static final int REN_CLEAR_ALL = 3;
	public static final int REN_TEXTURE = 4;
	public static final int REN_AVATAR = 5;
	public static final int REN_SCENE = 6;
	public static final int REN_ATLAS_REGION = 7;
	public static final int REN_LIGHTS = 8;
	public static final int REN_RAGDOLL = 9;
	public static final int REN_SOLIDS = 10;
	// ValCraft: the first-person hands and held items, in Minecraft view space (camera at the origin
	// looking down -Z), drawn by Valheim in its scene. RenAvatar layout; 0 batches = none this frame.
	public static final int REN_VIEWMODEL = 11;
	/** ValCraft: the player's inventory (int entries, then per entry: int count, int id length, UTF-8 id), for Valheim building costs. */
	public static final int REN_INVENTORY = 12;
	/** ValCraft: every item's icon in the atlas (int entries, then per entry: int id length, UTF-8 id, u0 v0 u1 v1), for Valheim's build menu. */
	public static final int REN_ITEM_ICONS = 13;
	/** ValCraft: Minecraft's mobs near the player, for Valheim's creatures to fight (MobExporter / MobProxies.cs). */
	public static final int REN_MOBS = 14;
	public static final int PART_HEAD = 1, PART_BODY = 2, PART_RIGHT_ARM = 3, PART_LEFT_ARM = 4, PART_RIGHT_LEG = 5, PART_LEFT_LEG = 6;
	public static final int LIGHT_STEADY = 0, LIGHT_FLAME = 1, LIGHT_LAVA = 2;
	public static final int REN_VERTEX_BYTES = 32;

	// Header
	public static final long H_MAGIC = 0x00;
	public static final long H_VERSION = 0x04;
	public static final long H_VALHEIM_PID = 0x08;
	public static final long H_MC_PID = 0x0C;
	public static final long H_VALHEIM_HEARTBEAT = 0x10;
	public static final long H_MC_HEARTBEAT = 0x18;

	// ValState (relative to OFF_VAL_STATE)
	public static final long SS_SEQ = 0x00;
	public static final long SS_FLAGS = 0x04;
	public static final long SS_WORLD_ID = 0x08;
	public static final long SS_COLLISION_EPOCH = 0x0C;
	public static final long SS_POS_X = 0x10;
	public static final long SS_POS_Y = 0x18;
	public static final long SS_POS_Z = 0x20;
	public static final long SS_YAW = 0x28;
	public static final long SS_PITCH = 0x2C;
	public static final long SS_TELEPORT_SEQ = 0x30;
	public static final long SS_VIEWPORT_W = 0x34;
	public static final long SS_VIEWPORT_H = 0x38;
	public static final long SS_GAME_HOUR = 0x3C;
	public static final long SS_FOV_SETTING = 0x40;
	public static final long SS_FOV_SEQ = 0x44;
	public static final long SS_MP_MODE = 0x48;
	public static final long SS_MP_SEQ = 0x4C;
	public static final long SS_MP_LINK = 0x50;
	public static final int MP_LINK_BYTES = 64;
	/** ValState.mpMode: our own world; our own, opened to friends; a friend's (at mpLink). */
	public static final int MP_OWN = 0, MP_HOST = 1, MP_JOIN = 2;

	public static final int VAL_IN_GAME = 1;
	public static final int VAL_MENU_OPEN = 1 << 1;
	public static final int VAL_LOADING = 1 << 2;
	public static final int VAL_BLOCK_TERRAIN = 1 << 3; // ValCraft: block terrain on (the "-blocks" save, see TerrainGen)
	/** ValCraft: mobs' pathfinding effort on Valheim terrain (bits 4-5): 0 balanced, 1 low, 2 high (TerrainPath). */
	public static final int VAL_MOB_PATHING_SHIFT = 4;
	/** Valheim's game is paused (single player with its menu open): Minecraft pauses too. */
	public static final int VAL_PAUSED = 1 << 6;
	/** ValCraft: [Mobs] NaturalSpawning, Minecraft mobs spawn on Valheim's ground (MobSpawner). */
	public static final int VAL_MOB_SPAWNING = 1 << 7;
	/** ValCraft: [Explosions] CreeperGriefing, Minecraft's mobGriefing rule. */
	public static final int VAL_MOB_GRIEFING = 1 << 8;
	/** ValCraft: Valheim's weather where the player is: rain or snow (Minecraft rains), a thunderstorm (it thunders). */
	public static final int VAL_WET = 1 << 9, VAL_THUNDER = 1 << 10;

	// McState (relative to OFF_MC_STATE)
	public static final long MS_SEQ = 0x00;
	public static final long MS_FLAGS = 0x04;
	public static final long MS_X = 0x08;
	public static final long MS_Y = 0x10;
	public static final long MS_Z = 0x18;
	public static final long MS_YAW = 0x20;
	public static final long MS_PITCH = 0x24;
	public static final long MS_EYE_HEIGHT = 0x28;
	public static final long MS_SENSITIVITY = 0x2C;
	public static final long MS_TELEPORT_ACK = 0x30;
	public static final long MS_GUI_SCALE = 0x34;
	public static final long MS_FRAME_COUNTER = 0x38;
	public static final long MS_FOV = 0x40;
	public static final long MS_BOB_PHASE = 0x44;
	public static final long MS_BOB_AMOUNT = 0x48;
	public static final long MS_EYE_X = 0x50;
	public static final long MS_EYE_Y = 0x58;
	public static final long MS_EYE_Z = 0x60;
	public static final long MS_TICK_QPC = 0x68;
	public static final long MS_PREV_X = 0x70;
	public static final long MS_CUR_X = 0x88;
	public static final long MS_EYE_HEIGHT_O = 0xA0;
	public static final long MS_EYE_HEIGHT_T = 0xA4;
	public static final long MS_WALK_O = 0xA8;
	public static final long MS_WALK = 0xAC;
	public static final long MS_BOB_O = 0xB0;
	public static final long MS_BOB = 0xB4;
	public static final long MS_TICK_MS = 0xB8;
	public static final long MS_CAMERA_MODE = 0xC0;
	public static final long MS_CAMERA_DISTANCE = 0xC4;
	public static final long MS_OPTIONS_FOV = 0xC8;
	public static final long MS_FOV_ACK = 0xCC;
	public static final long MS_MP_STATE = 0xD0;
	public static final long MS_MP_LINK = 0xD4;
	public static final int MS_MP_LINK_BYTES = 44;
	/** McState.mpState: our world is open to friends at mpLink; we're in a friend's world. */
	public static final int MP_PUBLISHED = 1, MP_IN_FRIEND_WORLD = 2;

	public static final int MC_IN_WORLD = 1;
	public static final int MC_SCREEN_OPEN = 1 << 1;
	public static final int MC_ON_GROUND = 1 << 2;
	public static final int MC_SNEAKING = 1 << 3;
	public static final int MC_SPRINTING = 1 << 4;
	public static final int MC_DEAD = 1 << 5;
	public static final int MC_SWIMMING = 1 << 6;
	public static final int MC_FLYING = 1 << 7;
	/** The eye is in a Minecraft water / lava block (Valheim's own sea is Valheim's to check). */
	public static final int MC_EYE_IN_WATER = 1 << 8;
	public static final int MC_EYE_IN_LAVA = 1 << 9;
	/** ValCraft: a hoe / the Build Hammer in the main hand (Valheim's build modes); creative mode (they cost nothing). */
	public static final int MC_HOLDING_HOE = 1 << 10;
	public static final int MC_CREATIVE = 1 << 11;
	public static final int MC_HOLDING_HAMMER = 1 << 12;
	public static final int MC_HOLDING_LIGHT = 1 << 13;
	public static final int MC_HOLDING_SOUL_LIGHT = 1 << 14;
	public static final int MC_IN_BOAT = 1 << 15;

	// Overlay
	public static final long OC_STATE = 0x00;
	public static final long OC_FRAMES_PUBLISHED = 0x08;
	public static final int OVERLAY_DIRTY = 1 << 2;
	public static final long SLOT_HDR_SIZE = 0x40;
	public static final long SH_WIDTH = 0x00;
	public static final long SH_HEIGHT = 0x04;
	public static final long SH_FLAGS = 0x08;
	public static final long SH_FRAME_ID = 0x10;

	// Input ring (relative to OFF_INPUT_RING)
	public static final int INPUT_RING_ENTRIES = 4096;
	public static final long IR_HEAD = 0x00;
	public static final long IR_TAIL = 0x40;
	public static final long IR_DATA = 0x80;
	public static final int IN_KEY = 1;
	public static final int IN_MOUSE_BUTTON = 2;
	public static final int IN_SCROLL = 3;
	public static final int IN_CURSOR = 4;
	public static final int IN_TEXT = 5;
	public static final int IN_RELEASE_ALL = 6;

	// Collision ring (relative to OFF_COLLISION_RING)
	public static final long CR_HEAD = 0x00;
	public static final long CR_TAIL = 0x40;
	public static final long CR_DATA = 0x80;
	public static final long CR_DATA_BYTES = COLLISION_RING_BYTES - CR_DATA;
	public static final int COL_PAD = 0;
	public static final int COL_CLEAR = 1;
	public static final int COL_REGION = 2;
	public static final int COL_TRIS = 3;
	public static final int COL_TERRAIN = 4; // ValCraft: a chunk's column heights and biomes for block terrain
	public static final int COL_TRI_BYTES = 40;
	public static final int TRI_STAIR_HELPER = 1;
	public static final int COL_REGION_HEADER_BYTES = 32;
	public static final int COL_BLOCK_BYTES = 80;
}
