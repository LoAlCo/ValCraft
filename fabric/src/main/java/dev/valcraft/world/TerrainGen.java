package dev.valcraft.world;

import dev.valcraft.ValCraft;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.util.Arrays;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.storage.LevelResource;
import org.jspecify.annotations.Nullable;

/**
 * Block terrain: Valheim's ground as real Minecraft blocks. Valheim sends each chunk's column
 * heights and biomes (COL_TERRAIN); the server thread builds the columns once (a biome surface,
 * dirt, then stone with ores, down to a bedrock floor where Valheim's own digging stops: 8 m under
 * its ground as the world made it, sent with each column; sand and gravel under the sea; some plants)
 * and
 * remembers which chunks it built, in the world folder. Only used in the "-blocks" save
 * (see MirrorWorld), so normal-mode builds are never touched.
 *
 * Valheim's ground changes (TNT craters, digging, the hoe) while block terrain is off; the next time
 * a chunk comes in with other heights, its changed columns follow the new ground. Generation is
 * deterministic, so a block that's still exactly what the old ground generated is terrain and is
 * reshaped; anything else is the player's and stays. Air inside the old terrain was mined and stays
 * air; air outside it (under a crater, above raised ground) gets the new ground. The heights each
 * chunk was built from are kept next to the list of built chunks (chunks built before that: read
 * back from the blocks themselves).
 */
public final class TerrainGen {
	public static final int BIOME_MEADOWS = 1, BIOME_BLACK_FOREST = 2, BIOME_SWAMP = 3, BIOME_MOUNTAIN = 4, BIOME_PLAINS = 5, BIOME_MISTLANDS = 6,
		BIOME_ASHLANDS = 7, BIOME_DEEP_NORTH = 8, BIOME_OCEAN = 9;
	/** Before 0.5.8 the ground was 12 deep, with nothing under it. */
	private static final int OLD_DEPTH = 12;
	/** How far above or below a column's top its old surface is looked for (chunks from before heights were kept). */
	private static final int SCAN = 24;
	/** Valheim's sea level is 30 m: columns below it are sea floor. */
	private static final int SEA_TOP = 29;
	// 40 chunks a second: far more than exploring needs, and each one is re-lit, re-meshed and sent to Valheim.
	private static final int CHUNKS_PER_TICK = 2;

	/** floor: how many blocks under top the bedrock is (fixed in the world: Valheim's dig limit). */
	private record Chunk(int cx, int cz, short[] top, byte[] biome, byte[] floor) {
		int bedrock(int i) {
			return this.top[i] - (this.floor[i] & 0xFF);
		}
	}

	private static final ConcurrentLinkedQueue<Chunk> QUEUE = new ConcurrentLinkedQueue<>();
	private static final Set<Long> BUILT = ConcurrentHashMap.newKeySet();
	/** The column heights each built chunk was built from (absent for chunks built before they were kept). */
	private static final ConcurrentHashMap<Long, short[]> HEIGHTS = new ConcurrentHashMap<>();
	/** Chunks checked this session for the full depth and bedrock floor (older chunks were shallower). */
	private static final Set<Long> FLOORED = ConcurrentHashMap.newKeySet();
	/** Chunks waiting in QUEUE, so one isn't queued again while it waits. */
	private static final Set<Long> QUEUED = ConcurrentHashMap.newKeySet();
	private static volatile Path builtFile, heightsFile;
	private static volatile ServerLevel builtLevel;

	private TerrainGen() {
	}

	private static long key(int cx, int cz) {
		return ((long) cx << 32) ^ (cz & 0xFFFFFFFFL);
	}

	/** True once the chunk's terrain is built (any thread). */
	public static boolean isBuilt(int cx, int cz) {
		return BUILT.contains(key(cx, cz));
	}

	/** Collision consumer thread: one chunk's columns, copied out of shared memory. */
	public static void receive(int cx, int cz, short[] top, byte[] biome, byte[] floor) {
		long k = key(cx, cz);
		if (BUILT.contains(k)) {
			short[] was = HEIGHTS.get(k);
			if (was != null && Arrays.equals(was, top) && FLOORED.contains(k)) {
				return; // the same ground it was built from
			}
		}
		if (QUEUED.add(k)) {
			QUEUE.add(new Chunk(cx, cz, top, biome, floor));
		}
	}

	/** Server thread, every tick. */
	public static void tick(MinecraftServer server) {
		ServerLevel level = server.overworld();
		if (level != builtLevel) {
			load(server, level);
		}
		ValGround.blockTerrainSave = server.getWorldData().getLevelName().endsWith("-blocks");
		var sky = new dev.valcraft.link.ValLink.ValState();
		boolean read = dev.valcraft.link.ValLink.readValState(sky);
		if (read) {
			TerrainPath.setQuality((sky.flags >> dev.valcraft.link.Proto.VAL_MOB_PATHING_SHIFT) & 3);
		}
		if (!read || (sky.flags & dev.valcraft.link.Proto.VAL_BLOCK_TERRAIN) == 0) {
			QUEUE.clear();
			QUEUED.clear();
			return;
		}
		// Valheim starts sending the moment block terrain goes on, while Minecraft is still switching
		// to the -blocks save: keep those chunks for when it's open (dropping them left holes).
		if (!server.getWorldData().getLevelName().endsWith("-blocks")) {
			return;
		}
		for (int i = 0; i < CHUNKS_PER_TICK; i++) {
			Chunk c = QUEUE.poll();
			if (c == null) {
				return;
			}
			long k = key(c.cx, c.cz);
			QUEUED.remove(k);
			if (BUILT.add(k)) {
				build(level, c);
				remember(c.cx, c.cz);
				rememberHeights(c);
				FLOORED.add(k);
			} else {
				short[] was = HEIGHTS.get(k);
				if (was == null) {
					was = readBack(level, c); // built before heights were kept
				}
				if (FLOORED.add(k)) {
					deepen(level, c, was); // first, so the old ground below is whole before it's reshaped
				}
				int changed = follow(level, c, was);
				if (!Arrays.equals(was, c.top) || !HEIGHTS.containsKey(k)) {
					rememberHeights(c);
				}
				if (changed > 0) {
					ValCraft.LOG.info("ValCraft: block terrain: {} columns of chunk {} {} follow Valheim's changed ground", changed, c.cx, c.cz);
				}
			}
		}
	}

	private static void load(MinecraftServer server, ServerLevel level) {
		builtLevel = level;
		BUILT.clear();
		HEIGHTS.clear();
		QUEUED.clear();
		FLOORED.clear();
		builtFile = server.getWorldPath(LevelResource.ROOT).resolve("valcraft_terrain.txt");
		heightsFile = server.getWorldPath(LevelResource.ROOT).resolve("valcraft_terrain_heights.dat");
		try {
			if (Files.exists(builtFile)) {
				for (String line : Files.readAllLines(builtFile)) {
					String[] p = line.trim().split(" ");
					if (p.length == 2) {
						BUILT.add(key(Integer.parseInt(p[0]), Integer.parseInt(p[1])));
					}
				}
			}
			ValCraft.LOG.info("ValCraft: block terrain: {} chunks already built in this world", BUILT.size());
		} catch (IOException | NumberFormatException e) {
			ValCraft.LOG.warn("ValCraft: couldn't read {}", builtFile, e);
		}
		loadHeights();
	}

	private static void remember(int cx, int cz) {
		try {
			Files.writeString(builtFile, cx + " " + cz + "\n", StandardOpenOption.CREATE, StandardOpenOption.APPEND);
		} catch (IOException e) {
			ValCraft.LOG.warn("ValCraft: couldn't write {}", builtFile, e);
		}
	}

	// valcraft_terrain_heights.dat: records of int cx, int cz, 256 shorts (the column tops), appended
	// as chunks are built or follow new ground; a chunk's last record wins.
	private static void loadHeights() {
		try {
			if (!Files.exists(heightsFile)) {
				return;
			}
			ByteBuffer in = ByteBuffer.wrap(Files.readAllBytes(heightsFile));
			while (in.remaining() >= 8 + 512) {
				int cx = in.getInt(), cz = in.getInt();
				short[] top = new short[256];
				for (int i = 0; i < 256; i++) {
					top[i] = in.getShort();
				}
				HEIGHTS.put(key(cx, cz), top);
			}
		} catch (IOException e) {
			ValCraft.LOG.warn("ValCraft: couldn't read {}", heightsFile, e);
		}
	}

	private static void rememberHeights(Chunk c) {
		HEIGHTS.put(key(c.cx, c.cz), c.top.clone());
		ByteBuffer out = ByteBuffer.allocate(8 + 512).putInt(c.cx).putInt(c.cz);
		for (short t : c.top) {
			out.putShort(t);
		}
		try {
			Files.write(heightsFile, out.array(), StandardOpenOption.CREATE, StandardOpenOption.APPEND);
		} catch (IOException e) {
			ValCraft.LOG.warn("ValCraft: couldn't write {}", heightsFile, e);
		}
	}

	/**
	 * A chunk built before heights were kept: each column's old top, read from the blocks (the
	 * surface block the generator would have put there, with nothing solid on it). Where it can't be
	 * found (built over, dug out), the new top: that column is left as it is.
	 */
	private static short[] readBack(ServerLevel level, Chunk c) {
		short[] was = c.top.clone();
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		for (int i = 0; i < 256; i++) {
			int wx = c.cx * 16 + (i & 15), wz = c.cz * 16 + (i >> 4), top = c.top[i], bedrock = c.bedrock(i);
			for (int t = top + SCAN; t > bedrock && t >= top - SCAN; t--) {
				BlockState surface = generated(t, bedrock, c.biome[i], wx, wz, t);
				if (surface != null && !surface.isAir() && level.getBlockState(pos.set(wx, t, wz)) == surface
					&& level.getBlockState(pos.set(wx, t + 1, wz)).getCollisionShape(level, pos).isEmpty()) {
					was[i] = (short) t;
					break;
				}
			}
		}
		return was;
	}

	/**
	 * Chunks built before the bedrock floor (12 deep with nothing under it, or a 0.5.8 test build's
	 * floor at another depth): the ground goes down to the floor, filling only the empty space under
	 * where the old ground ended (nobody can have dug there), and a stray old floor becomes ground.
	 * Safe on any chunk; done once a session per chunk.
	 */
	private static void deepen(ServerLevel level, Chunk c, short[] tops) {
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		int flags = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE;
		BlockState bedrock = Blocks.BEDROCK.defaultBlockState();
		for (int i = 0; i < 256; i++) {
			int top = tops[i], floor = c.bedrock(i), wx = c.cx * 16 + (i & 15), wz = c.cz * 16 + (i >> 4);
			for (int y = top - OLD_DEPTH; y >= floor; y--) {
				BlockState now = level.getBlockState(pos.set(wx, y, wz));
				if (now.isAir() || (now == bedrock && y > floor)) {
					BlockState want = generated(top, floor, c.biome[i], wx, wz, y);
					if (want != null && want != now) {
						level.setBlock(pos, want, flags);
					}
				}
			}
			if (level.getBlockState(pos.set(wx, floor, wz)) != bedrock && floor < top) {
				level.setBlock(pos, bedrock, flags);
			}
		}
	}

	/** Moves the columns whose top changed onto the new ground (see the class comment). Returns how many. */
	private static int follow(ServerLevel level, Chunk c, short[] was) {
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		int flags = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE;
		BlockState air = Blocks.AIR.defaultBlockState();
		int changed = 0;
		for (int i = 0; i < 256; i++) {
			int oldTop = was[i], newTop = c.top[i];
			if (oldTop == newTop) {
				continue;
			}
			changed++;
			int biome = c.biome[i], floor = c.bedrock(i);
			int wx = c.cx * 16 + (i & 15), wz = c.cz * 16 + (i >> 4);
			int from = floor, to = Math.max(oldTop, newTop) + 1;
			for (int y = from; y <= to; y++) {
				BlockState now = level.getBlockState(pos.set(wx, y, wz));
				BlockState before = generated(oldTop, floor, biome, wx, wz, y);
				BlockState after = generated(newTop, floor, biome, wx, wz, y);
				BlockState want = after != null ? after : air;
				if (now == want) {
					continue;
				}
				boolean oldTerrain = y > floor && y <= oldTop;
				if (before != null && now == before) {
					// still the old ground (or its plant / the air above it): reshaped
					level.setBlock(pos, want, flags);
				} else if (now.isAir() && !oldTerrain) {
					// open space the old ground never filled: the new ground goes in
					level.setBlock(pos, want, flags);
				}
				// anything else is the player's (built, or mined out of the old ground) and stays
			}
		}
		return changed;
	}

	private static BlockState block(String id) {
		Block b = BuiltInRegistries.BLOCK.getValue(Identifier.withDefaultNamespace(id));
		return b == null ? Blocks.STONE.defaultBlockState() : b.defaultBlockState();
	}

	/** Deterministic noise in [0, 1) per position and salt. */
	private static float hash(int x, int y, int z, int salt) {
		long h = x * 73856093L ^ y * 19349663L ^ z * 83492791L ^ salt * 2654435761L;
		h ^= h >>> 13;
		h *= 0x5bd1e995L;
		h ^= h >>> 15;
		return (h & 0xFFFFFF) / (float) 0x1000000;
	}

	private static void build(ServerLevel level, Chunk c) {
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		int flags = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE;
		for (int z = 0; z < 16; z++) {
			for (int x = 0; x < 16; x++) {
				int i = x + 16 * z;
				int top = c.top[i], floor = c.bedrock(i);
				int wx = c.cx * 16 + x, wz = c.cz * 16 + z;
				for (int y = top + 1; y >= floor; y--) {
					BlockState state = generated(top, floor, c.biome[i], wx, wz, y);
					if (state != null && !state.isAir()) {
						level.setBlock(pos.set(wx, y, wz), state, flags);
					}
				}
			}
		}
	}

	/**
	 * What the generator puts at y in a column whose ground is at top and bedrock at floor: its
	 * blocks, the bedrock, the plant (or air) just above, and null outside that. Deterministic.
	 */
	private static @Nullable BlockState generated(int top, int floor, int biome, int wx, int wz, int y) {
		if (y > top + 1 || y < floor) {
			return null;
		}
		if (y == floor && floor < top) {
			return Blocks.BEDROCK.defaultBlockState(); // Valheim's dig limit: no digging past it, or into the void
		}
		boolean underwater = top < SEA_TOP;
		String surface, fill, deep = "stone";
		switch (biome) {
			case BIOME_BLACK_FOREST -> { surface = hash(wx, 0, wz, 1) < 0.35F ? "podzol" : hash(wx, 0, wz, 2) < 0.15F ? "coarse_dirt" : "grass_block"; fill = "dirt"; }
			case BIOME_SWAMP -> { surface = "mud"; fill = "mud"; }
			case BIOME_MOUNTAIN -> { surface = top > 110 ? "snow_block" : hash(wx, 0, wz, 3) < 0.5F ? "stone" : "snow_block"; fill = "stone"; }
			case BIOME_PLAINS -> { surface = hash(wx, 0, wz, 4) < 0.2F ? "coarse_dirt" : "grass_block"; fill = "dirt"; }
			case BIOME_MISTLANDS -> { surface = "moss_block"; fill = "tuff"; deep = "deepslate"; }
			case BIOME_ASHLANDS -> { surface = hash(wx, 0, wz, 5) < 0.04F ? "magma_block" : hash(wx, 0, wz, 6) < 0.3F ? "blackstone" : "netherrack"; fill = "netherrack"; deep = "basalt"; }
			case BIOME_DEEP_NORTH -> { surface = "snow_block"; fill = "packed_ice"; }
			case BIOME_OCEAN -> { surface = "sand"; fill = "sandstone"; }
			default -> { surface = "grass_block"; fill = "dirt"; }
		}
		if (underwater || (top < SEA_TOP + 2 && biome != BIOME_SWAMP)) {
			surface = top < SEA_TOP - 3 && hash(wx, 0, wz, 7) < 0.3F ? "gravel" : "sand";
			fill = "sand";
		}
		if (y == top + 1) {
			// No water blocks: Valheim's own sea (waves and all) stays the water, and Minecraft already
			// swims in it; water blocks at the same height would fight with its surface.
			String plant = !underwater && (surface.equals("grass_block") || surface.equals("podzol")) ? plant(biome, hash(wx, top, wz, 8)) : null;
			return plant != null ? block(plant) : Blocks.AIR.defaultBlockState();
		}
		int depth = top - y;
		String id = depth == 0 ? surface : depth <= 3 ? fill : deep;
		if (depth > 3 && (id.equals("stone") || id.equals("deepslate"))) {
			String ore = ore(wx, y, wz, depth);
			if (ore != null) {
				id = id.equals("deepslate") ? "deepslate_" + ore : ore;
			}
		}
		return block(id);
	}

	private static @Nullable String plant(int biome, float r) {
		if (biome == BIOME_BLACK_FOREST) {
			return r < 0.08F ? "fern" : r < 0.12F ? "short_grass" : r < 0.125F ? "brown_mushroom" : null;
		}
		if (biome == BIOME_PLAINS) {
			return r < 0.18F ? "short_grass" : r < 0.19F ? "dandelion" : null;
		}
		return r < 0.14F ? "short_grass" : r < 0.155F ? "dandelion" : r < 0.17F ? "poppy" : r < 0.175F ? "oxeye_daisy" : r < 0.18F ? "cornflower" : null;
	}

	private static @Nullable String ore(int x, int y, int z, int depth) {
		float r = hash(x, y, z, 11);
		if (r < 0.012F) return "coal_ore";
		if (r < 0.020F) return "iron_ore";
		if (r < 0.028F) return "copper_ore";
		if (depth > 4 && r < 0.031F) return "gold_ore";
		if (depth > 4 && r < 0.035F) return "redstone_ore";
		if (depth > 4 && r < 0.037F) return "lapis_ore";
		if (depth > 6 && r < 0.0385F) return "diamond_ore";
		return null;
	}
}
