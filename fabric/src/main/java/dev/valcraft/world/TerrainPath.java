package dev.valcraft.world;

import it.unimi.dsi.fastutil.longs.Long2DoubleOpenHashMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.PriorityQueue;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.pathfinder.Node;
import net.minecraft.world.level.pathfinder.Path;
import net.minecraft.world.level.pathfinder.PathType;
import org.jspecify.annotations.Nullable;

/**
 * Walking mobs' pathfinding on Valheim terrain. Minecraft's pathfinder plans in whole blocks; on
 * Valheim's smooth hills and loose objects it hopped up slopes and sent mobs onto rocks and logs.
 * Valheim's ground is a height map, so this searches it as one: a column is a place to stand at its
 * exact terrain height (or on top of Minecraft blocks built there), a step can rise about a block
 * or drop three, and Valheim's objects (rocks, logs, trees, bushes) are in the way, never stood on.
 * With no full route, the mob goes to the reachable ground spot closest to its target.
 *
 * Only where Valheim's terrain height is known and not in the block-terrain save (real blocks there).
 */
public final class TerrainPath {
	/**
	 * Effort, from Valheim's config ([Mobs] Pathfinding, Proto.VAL_MOB_PATHING_SHIFT): how many ground
	 * spots one plan may look at, how far out, and how long (ticks) a mob keeps its plan when it asks
	 * for the same target again (they re-plan several times a second; reusing it is most of the saving).
	 */
	private static volatile int maxExpansions = 700, maxRadius = 24, reuseTicks = 16;
	private static volatile int quality = -1;
	private static final java.util.Map<Mob, Object[]> RECENT = new java.util.WeakHashMap<>();
	private static final double MAX_RISE = 1.2, MAX_DROP = 3.0;
	// Paths planned here: their points are columns of Valheim ground, not blocks (see isOurs).
	private static final java.util.Set<Path> OURS = java.util.Collections.newSetFromMap(new java.util.WeakHashMap<>());

	/**
	 * A path over Valheim's ground: its points are columns, at the terrain's height. A mob on a rock,
	 * a log or a steep slope can be more than a block above or below that, which Minecraft's
	 * waypoint check never counts as arrived, so the mob circled the point until its stuck check
	 * (5 s) gave up. Followed by horizontal distance instead (PathNavigationValheimMixin).
	 */
	public static boolean isOurs(@Nullable Path path) {
		return path != null && OURS.contains(path);
	}

	private record Cell(int x, int y, int z, double floor) {
	}

	private record Open(long key, double f) {
	}

	private TerrainPath() {
	}

	/** 0 balanced, 1 low, 2 high. */
	public static void setQuality(int q) {
		if (q == quality) {
			return;
		}
		quality = q;
		switch (q) {
			case 1 -> { maxExpansions = 300; maxRadius = 16; reuseTicks = 30; }
			case 2 -> { maxExpansions = 1500; maxRadius = 32; reuseTicks = 8; }
			default -> { maxExpansions = 700; maxRadius = 24; reuseTicks = 16; }
		}
		dev.valcraft.ValCraft.LOG.info("ValCraft: mob pathfinding on Valheim terrain: {}", q == 1 ? "low" : q == 2 ? "high" : "balanced");
	}

	public static boolean applies(Mob mob) {
		return !ValGround.blockTerrainSave && !Float.isNaN(ValCollision.terrainHeight(mob.getBlockX(), mob.getBlockZ()));
	}

	private static long key(int x, int z) {
		return ((long) x << 32) ^ (z & 0xFFFFFFFFL);
	}

	/** Where a mob stands in this column: on the terrain or on Minecraft blocks, whichever is nearest {@code near}. */
	private static @Nullable Cell cellAt(BlockGetter level, int x, int z, double near) {
		float h = ValCollision.terrainHeight(x, z);
		if (Float.isNaN(h)) {
			return null;
		}
		int g = ValGround.groundCell(h);
		Cell best = null;
		if (ValGround.bodyFree(level, x, g, z)) {
			best = new Cell(x, g, z, h);
		}
		// Minecraft blocks built above the terrain: only where the column's highest block says so
		// (nearly nowhere in Valheim-terrain mode), not eight block lookups for every column.
		int highest = level instanceof net.minecraft.world.level.Level l
			? l.getHeight(net.minecraft.world.level.levelgen.Heightmap.Types.MOTION_BLOCKING, x, z) : g + 9;
		for (int y = g + 1; y <= Math.min(g + 8, highest); y++) {
			if (ValGround.blockSolidAt(level, x, y - 1, z) && ValGround.bodyFree(level, x, y, z)) {
				double floor = y - 1 + ValGround.blockTop(level, x, y - 1, z);
				if (best == null || Math.abs(floor - near) < Math.abs(best.floor - near)) {
					best = new Cell(x, y, z, floor);
				}
			}
		}
		return best;
	}

	/** A path to {@code target}: the one the mob is on if it asked for the same target just now, else straight there, else searched. */
	public static @Nullable Path find(Mob mob, BlockPos target, int reach) {
		long now = mob.level().getGameTime();
		Object[] recent = RECENT.get(mob);
		if (recent != null && now - (long) recent[0] < reuseTicks && ((BlockPos) recent[1]).distSqr(target) <= 2.25 && !((Path) recent[2]).isDone()) {
			return (Path) recent[2];
		}
		Path path = ValGround.straightPath(mob, target);
		if (path == null) {
			path = search(mob, target, reach);
		}
		if (path != null) {
			RECENT.put(mob, new Object[] { now, target, path });
			OURS.add(path);
		}
		return path;
	}

	private static @Nullable Path search(Mob mob, BlockPos target, int reach) {
		BlockGetter level = mob.level();
		Long2ObjectOpenHashMap<Cell> columns = new Long2ObjectOpenHashMap<>();  // cellAt, once per column per search
		int sx = mob.getBlockX(), sz = mob.getBlockZ();
		Cell start = cellAt(level, sx, sz, mob.getY());
		if (start == null) {
			return null;
		}
		double tx = target.getX() + 0.5, tz = target.getZ() + 0.5;
		double goalDist = Math.max(1.0, reach + 0.75);
		Long2ObjectOpenHashMap<Cell> cells = new Long2ObjectOpenHashMap<>();
		Long2ObjectOpenHashMap<Long> parent = new Long2ObjectOpenHashMap<>();
		Long2DoubleOpenHashMap g = new Long2DoubleOpenHashMap();
		g.defaultReturnValue(Double.MAX_VALUE);
		PriorityQueue<Open> open = new PriorityQueue<>((a, b) -> Double.compare(a.f, b.f));
		long startKey = key(sx, sz);
		cells.put(startKey, start);
		g.put(startKey, 0.0);
		open.add(new Open(startKey, dist(start, tx, tz)));
		long bestKey = startKey;
		double bestH = dist(start, tx, tz);
		boolean reached = false;
		int expansions = 0;
		while (!open.isEmpty() && expansions++ < maxExpansions) {
			Open o = open.poll();
			Cell c = cells.get(o.key);
			double gc = g.get(o.key);
			if (o.f > gc + dist(c, tx, tz) + 1e-6) {
				continue;  // a stale entry
			}
			double hc = dist(c, tx, tz);
			if (hc < bestH) {
				bestH = hc;
				bestKey = o.key;
			}
			if (hc <= goalDist && Math.abs(c.floor - target.getY()) <= 2.5) {
				bestKey = o.key;
				reached = true;
				break;
			}
			for (int dx = -1; dx <= 1; dx++) {
				for (int dz = -1; dz <= 1; dz++) {
					if (dx == 0 && dz == 0) {
						continue;
					}
					int nx = c.x + dx, nz = c.z + dz;
					if (Math.abs(nx - sx) > maxRadius || Math.abs(nz - sz) > maxRadius) {
						continue;
					}
					Cell n = column(level, columns, nx, nz, c.floor);
					if (n == null || !stepOk(c, n)) {
						continue;
					}
					if (dx != 0 && dz != 0) {
						// Diagonal: both corners passable too, or the body catches on them.
						Cell a = column(level, columns, c.x + dx, c.z, c.floor), b = column(level, columns, c.x, c.z + dz, c.floor);
						if (a == null || b == null || !stepOk(c, a) || !stepOk(c, b)) {
							continue;
						}
					}
					double rise = n.floor - c.floor;
					double cost = (dx != 0 && dz != 0 ? 1.414 : 1.0) + (rise > 0.6 ? 0.8 : 0.0);
					long nk = key(nx, nz);
					double ng = gc + cost;
					if (ng < g.get(nk)) {
						g.put(nk, ng);
						cells.put(nk, n);
						parent.put(nk, Long.valueOf(o.key));
						open.add(new Open(nk, ng + dist(n, tx, tz)));
					}
				}
			}
		}
		if (bestKey == startKey && !reached) {
			return null;
		}
		List<Node> nodes = new ArrayList<>();
		for (Long k = bestKey; k != null; k = parent.get(k.longValue())) {
			Cell c = cells.get(k.longValue());
			Node node = new Node(c.x, c.y, c.z);
			node.type = PathType.WALKABLE;
			nodes.add(node);
		}
		Collections.reverse(nodes);
		return new Path(nodes, target, reached);
	}

	/** cellAt, remembered for the rest of this search (a missing spot too). */
	private static @Nullable Cell column(BlockGetter level, Long2ObjectOpenHashMap<Cell> columns, int x, int z, double near) {
		long k = key(x, z);
		if (columns.containsKey(k)) {
			return columns.get(k);
		}
		Cell c = cellAt(level, x, z, near);
		columns.put(k, c);
		return c;
	}

	private static boolean stepOk(Cell from, Cell to) {
		double rise = to.floor - from.floor;
		return rise <= MAX_RISE && rise >= -MAX_DROP;
	}

	private static double dist(Cell c, double tx, double tz) {
		return Math.hypot(c.x + 0.5 - tx, c.z + 0.5 - tz);
	}
}
