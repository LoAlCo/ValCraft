package dev.valcraft.world;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.RandomSource;
import net.minecraft.world.Difficulty;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.phys.AABB;

/**
 * Minecraft's own spawning is off (the mirror world is a void, see ValCraft.configureServer); with
 * [Mobs] NaturalSpawning on in Valheim's config, this spawns Minecraft mobs on Valheim's ground
 * instead. One pass every couple of seconds per player: monsters at night, animals any time, picked
 * by the Valheim biome under them, 24-56 blocks away on dry land, up to a cap around each player.
 * Monsters despawn far from players like Minecraft's own. Also carries [Explosions] CreeperGriefing
 * over as Minecraft's mobGriefing rule. Server thread.
 */
public final class MobSpawner {
	private static final int EVERY_TICKS = 40;
	private static final int ANIMALS_EVERY = 10; // passes: animals are tried every 20 s
	private static final int MONSTER_CAP = 16, ANIMAL_CAP = 10;
	private static final double COUNT_RANGE = 96;
	private static final int MIN_DIST = 24, MAX_DIST = 56;

	private static final ValLink.ValState STATE = new ValLink.ValState();
	private static int pass;
	private static Boolean griefing;

	private MobSpawner() {
	}

	private record Pick(EntityType<? extends Mob> type, int weight) {
	}

	/** Server thread, every tick. */
	public static void tick(MinecraftServer server) {
		if (server.getTickCount() % EVERY_TICKS != 0 || !ValLink.active() || !ValLink.readValState(STATE) || !STATE.inGame() || STATE.paused()) {
			return;
		}
		boolean grief = (STATE.flags & Proto.VAL_MOB_GRIEFING) != 0;
		if (griefing == null || griefing != grief) {
			griefing = grief;
			server.getGameRules().set(GameRules.MOB_GRIEFING, grief, server);
			ValCraft.LOG.info("ValCraft: mob griefing {}", grief ? "on" : "off");
		}
		if ((STATE.flags & Proto.VAL_MOB_SPAWNING) == 0) {
			return;
		}
		ServerLevel level = server.overworld();
		boolean animals = ++pass % ANIMALS_EVERY == 0;
		boolean monsters = level.isDarkOutside() && level.getDifficulty() != Difficulty.PEACEFUL;
		if (!animals && !monsters) {
			return;
		}
		for (ServerPlayer player : level.players()) {
			if (player.isSpectator()) {
				continue;
			}
			int monsterCount = 0, animalCount = 0;
			for (Mob mob : level.getEntitiesOfClass(Mob.class, new AABB(player.blockPosition()).inflate(COUNT_RANGE))) {
				MobCategory category = mob.getType().getCategory();
				if (category == MobCategory.MONSTER) {
					monsterCount++;
				} else if (category == MobCategory.CREATURE) {
					animalCount++;
				}
			}
			if (monsters && monsterCount < MONSTER_CAP) {
				for (int attempt = 0; attempt < 2; attempt++) {
					trySpawn(level, player, true);
				}
			}
			if (animals && animalCount < ANIMAL_CAP) {
				trySpawn(level, player, false);
			}
		}
	}

	private static void trySpawn(ServerLevel level, ServerPlayer player, boolean monster) {
		RandomSource random = level.getRandom();
		double angle = random.nextDouble() * Math.PI * 2;
		double dist = MIN_DIST + random.nextDouble() * (MAX_DIST - MIN_DIST);
		int x = (int) Math.floor(player.getX() + Math.cos(angle) * dist);
		int z = (int) Math.floor(player.getZ() + Math.sin(angle) * dist);
		int top = ValCollision.terrainTop(x, z);
		// Not known yet, or the player is far above the ground (a dungeon: they're placed up in the sky).
		if (top == Integer.MIN_VALUE || player.getY() - top > 40) {
			return;
		}
		double water = ValWater.surfaceAt(x, z);
		if (!Double.isNaN(water) && water > top + 1) {
			return; // land mobs only
		}
		Pick[] table = table(ValCollision.terrainBiome(x, z), monster);
		if (table.length == 0) {
			return;
		}
		EntityType<? extends Mob> type = pick(table, random);
		int group = monster ? 1 : 2 + random.nextInt(2);
		for (int n = 0; n < group; n++) {
			int gx = x + (n == 0 ? 0 : random.nextInt(5) - 2), gz = z + (n == 0 ? 0 : random.nextInt(5) - 2);
			int gtop = ValCollision.terrainTop(gx, gz);
			if (gtop == Integer.MIN_VALUE || Math.abs(gtop - top) > 2) {
				continue;
			}
			Mob mob = type.create(level, null, new BlockPos(gx, gtop + 1, gz), EntitySpawnReason.NATURAL, false, false);
			if (mob == null) {
				continue;
			}
			if (!level.noCollision(mob)) {
				mob.discard(); // inside a rock, tree or building
				continue;
			}
			level.addFreshEntityWithPassengers(mob);
		}
	}

	private static EntityType<? extends Mob> pick(Pick[] table, RandomSource random) {
		int total = 0;
		for (Pick p : table) {
			total += p.weight;
		}
		int r = random.nextInt(total);
		for (Pick p : table) {
			if ((r -= p.weight) < 0) {
				return p.type;
			}
		}
		return table[0].type;
	}

	private static final Pick[] NONE = {};

	/** What spawns in each Valheim biome (TerrainGen.BIOME_*). */
	private static Pick[] table(int biome, boolean monster) {
		if (monster) {
			return switch (biome) {
				case TerrainGen.BIOME_SWAMP -> MONSTERS_SWAMP;
				case TerrainGen.BIOME_MOUNTAIN, TerrainGen.BIOME_DEEP_NORTH -> MONSTERS_COLD;
				case TerrainGen.BIOME_PLAINS -> MONSTERS_PLAINS;
				case TerrainGen.BIOME_MISTLANDS -> MONSTERS_MIST;
				case TerrainGen.BIOME_ASHLANDS -> MONSTERS_ASH;
				case 0 -> NONE;
				default -> MONSTERS;
			};
		}
		return switch (biome) {
			case TerrainGen.BIOME_MEADOWS -> ANIMALS_MEADOWS;
			case TerrainGen.BIOME_BLACK_FOREST -> ANIMALS_FOREST;
			case TerrainGen.BIOME_SWAMP -> ANIMALS_SWAMP;
			case TerrainGen.BIOME_MOUNTAIN -> ANIMALS_MOUNTAIN;
			case TerrainGen.BIOME_PLAINS -> ANIMALS_PLAINS;
			case TerrainGen.BIOME_DEEP_NORTH -> ANIMALS_NORTH;
			default -> NONE;
		};
	}

	private static final Pick[] MONSTERS = {
		new Pick(EntityTypes.ZOMBIE, 30), new Pick(EntityTypes.SKELETON, 25), new Pick(EntityTypes.SPIDER, 20),
		new Pick(EntityTypes.CREEPER, 20), new Pick(EntityTypes.ENDERMAN, 3),
	};
	private static final Pick[] MONSTERS_SWAMP = {
		new Pick(EntityTypes.ZOMBIE, 25), new Pick(EntityTypes.SKELETON, 15), new Pick(EntityTypes.SPIDER, 15),
		new Pick(EntityTypes.CREEPER, 15), new Pick(EntityTypes.SLIME, 20), new Pick(EntityTypes.WITCH, 5),
	};
	private static final Pick[] MONSTERS_COLD = {
		new Pick(EntityTypes.ZOMBIE, 25), new Pick(EntityTypes.STRAY, 30), new Pick(EntityTypes.SPIDER, 15),
		new Pick(EntityTypes.CREEPER, 20), new Pick(EntityTypes.ENDERMAN, 3),
	};
	private static final Pick[] MONSTERS_PLAINS = {
		new Pick(EntityTypes.HUSK, 30), new Pick(EntityTypes.SKELETON, 25), new Pick(EntityTypes.SPIDER, 20),
		new Pick(EntityTypes.CREEPER, 20), new Pick(EntityTypes.ENDERMAN, 3),
	};
	private static final Pick[] MONSTERS_MIST = {
		new Pick(EntityTypes.SPIDER, 30), new Pick(EntityTypes.CAVE_SPIDER, 20), new Pick(EntityTypes.ZOMBIE, 15),
		new Pick(EntityTypes.SKELETON, 15), new Pick(EntityTypes.CREEPER, 15), new Pick(EntityTypes.ENDERMAN, 10),
	};
	private static final Pick[] MONSTERS_ASH = {
		new Pick(EntityTypes.WITHER_SKELETON, 25), new Pick(EntityTypes.MAGMA_CUBE, 20), new Pick(EntityTypes.SKELETON, 20),
		new Pick(EntityTypes.CREEPER, 15), new Pick(EntityTypes.ENDERMAN, 5),
	};
	private static final Pick[] ANIMALS_MEADOWS = {
		new Pick(EntityTypes.COW, 10), new Pick(EntityTypes.SHEEP, 12), new Pick(EntityTypes.PIG, 10), new Pick(EntityTypes.CHICKEN, 10),
	};
	private static final Pick[] ANIMALS_FOREST = {
		new Pick(EntityTypes.WOLF, 8), new Pick(EntityTypes.FOX, 8), new Pick(EntityTypes.RABBIT, 6), new Pick(EntityTypes.PIG, 4),
	};
	private static final Pick[] ANIMALS_SWAMP = { new Pick(EntityTypes.FROG, 10) };
	private static final Pick[] ANIMALS_MOUNTAIN = { new Pick(EntityTypes.GOAT, 10), new Pick(EntityTypes.RABBIT, 3) };
	private static final Pick[] ANIMALS_PLAINS = {
		new Pick(EntityTypes.HORSE, 6), new Pick(EntityTypes.SHEEP, 8), new Pick(EntityTypes.RABBIT, 6), new Pick(EntityTypes.COW, 6),
	};
	private static final Pick[] ANIMALS_NORTH = { new Pick(EntityTypes.POLAR_BEAR, 5), new Pick(EntityTypes.RABBIT, 5) };
}
