package dev.valcraft.world;

import dev.valcraft.ValCraft;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
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

/**
 * Block terrain: Valheim's ground as real Minecraft blocks. Valheim sends each chunk's column
 * heights and biomes (COL_TERRAIN); the server thread builds the columns once (a biome surface,
 * dirt, then stone with ores, about 12 deep; sand and gravel under the sea; some plants) and
 * remembers which chunks it built, in the world folder. Only used in the "-blocks" save
 * (see MirrorWorld), so normal-mode builds are never touched.
 */
public final class TerrainGen {
	public static final int BIOME_MEADOWS = 1, BIOME_BLACK_FOREST = 2, BIOME_SWAMP = 3, BIOME_MOUNTAIN = 4, BIOME_PLAINS = 5, BIOME_MISTLANDS = 6,
		BIOME_ASHLANDS = 7, BIOME_DEEP_NORTH = 8, BIOME_OCEAN = 9;
	private static final int DEPTH = 12;
	/** Valheim's sea level is 30 m: columns below it are sea floor. */
	private static final int SEA_TOP = 29;
	// 40 chunks a second: far more than exploring needs, and each one is re-lit, re-meshed and sent to Valheim.
	private static final int CHUNKS_PER_TICK = 2;

	private record Chunk(int cx, int cz, short[] top, byte[] biome) {
	}

	private static final ConcurrentLinkedQueue<Chunk> QUEUE = new ConcurrentLinkedQueue<>();
	private static final Set<Long> BUILT = ConcurrentHashMap.newKeySet();
	private static volatile Path builtFile;
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
	public static void receive(int cx, int cz, short[] top, byte[] biome) {
		if (!BUILT.contains(key(cx, cz))) {
			QUEUE.add(new Chunk(cx, cz, top, biome));
		}
	}

	/** Server thread, every tick. */
	public static void tick(MinecraftServer server) {
		ServerLevel level = server.overworld();
		if (level != builtLevel) {
			load(server, level);
		}
		var sky = new dev.valcraft.link.ValLink.ValState();
		if (!dev.valcraft.link.ValLink.readValState(sky) || (sky.flags & dev.valcraft.link.Proto.VAL_BLOCK_TERRAIN) == 0) {
			QUEUE.clear();
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
			if (BUILT.add(key(c.cx, c.cz))) {
				build(level, c);
				remember(c.cx, c.cz);
			}
		}
	}

	private static void load(MinecraftServer server, ServerLevel level) {
		builtLevel = level;
		BUILT.clear();
		builtFile = server.getWorldPath(LevelResource.ROOT).resolve("valcraft_terrain.txt");
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
	}

	private static void remember(int cx, int cz) {
		try {
			Files.writeString(builtFile, cx + " " + cz + "\n", StandardOpenOption.CREATE, StandardOpenOption.APPEND);
		} catch (IOException e) {
			ValCraft.LOG.warn("ValCraft: couldn't write {}", builtFile, e);
		}
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
				int top = c.top[i], biome = c.biome[i];
				int wx = c.cx * 16 + x, wz = c.cz * 16 + z;
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
				for (int y = top; y > top - DEPTH; y--) {
					int depth = top - y;
					String id = depth == 0 ? surface : depth <= 3 ? fill : deep;
					if (depth > 3 && (id.equals("stone") || id.equals("deepslate"))) {
						String ore = ore(wx, y, wz, depth);
						if (ore != null) {
							id = id.equals("deepslate") ? "deepslate_" + ore : ore;
						}
					}
					level.setBlock(pos.set(wx, y, wz), block(id), flags);
				}
				// No water blocks: Valheim's own sea (waves and all) stays the water, and Minecraft already
				// swims in it; water blocks at the same height would fight with its surface.
				if (!underwater && (surface.equals("grass_block") || surface.equals("podzol"))) {
					String plant = plant(biome, hash(wx, top, wz, 8));
					if (plant != null) {
						level.setBlock(pos.set(wx, top + 1, wz), block(plant), flags);
					}
				}
			}
		}
	}

	private static String plant(int biome, float r) {
		if (biome == BIOME_BLACK_FOREST) {
			return r < 0.08F ? "fern" : r < 0.12F ? "short_grass" : r < 0.125F ? "brown_mushroom" : null;
		}
		if (biome == BIOME_PLAINS) {
			return r < 0.18F ? "short_grass" : r < 0.19F ? "dandelion" : null;
		}
		return r < 0.14F ? "short_grass" : r < 0.155F ? "dandelion" : r < 0.17F ? "poppy" : r < 0.175F ? "oxeye_daisy" : r < 0.18F ? "cornflower" : null;
	}

	private static String ore(int x, int y, int z, int depth) {
		float r = hash(x, y, z, 11);
		if (r < 0.012F) return "coal_ore";
		if (r < 0.020F) return "iron_ore";
		if (r < 0.028F) return "copper_ore";
		if (depth > 6 && r < 0.031F) return "gold_ore";
		if (depth > 6 && r < 0.035F) return "redstone_ore";
		if (depth > 6 && r < 0.037F) return "lapis_ore";
		if (depth > 8 && r < 0.0385F) return "diamond_ore";
		return null;
	}
}
