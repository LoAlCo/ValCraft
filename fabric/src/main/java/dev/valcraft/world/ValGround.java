package dev.valcraft.world;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.pathfinder.Node;
import net.minecraft.world.level.pathfinder.Path;
import net.minecraft.world.level.pathfinder.PathType;
import org.jspecify.annotations.Nullable;

/**
 * Valheim's world as ground for Minecraft mobs (pathfinding, wandering, fleeing, chasing).
 *
 * Valheim sends its terrain surface per column (ValCollision.terrainHeight): everything below it is
 * solid ground, and the cell above it is where a mob stands. Everything else Valheim has (trees,
 * rocks, buildings) is an object: solid where it fills a cell, an obstacle to walk around, never a
 * place wandering or fleeing mobs pick (they went for trees and rocks, whose geometry reaches every
 * height). Narrow objects (trunks, bushes, posts) have no standable top either.
 *
 * In the block-terrain save the ground is real Minecraft blocks (dug and built into), so the
 * terrain surface isn't used there. Where it isn't known yet, Valheim geometry alone decides.
 */
public final class ValGround {
	/** The open save is the block-terrain one (TerrainGen sets this every tick). */
	public static volatile boolean blockTerrainSave;

	private ValGround() {
	}

	/** The terrain surface's exact height here, NaN in the block-terrain save or where it isn't known yet. */
	private static float terrain(int x, int z) {
		return blockTerrainSave ? Float.NaN : ValCollision.terrainHeight(x, z);
	}

	/** Valheim geometry fills most of this cell (used where the terrain surface isn't known). */
	private static boolean geometry(int x, int y, int z) {
		BlockPos pos = new BlockPos(x, y, z);
		return ValCollision.hasGeometry(pos) && ValCollision.groundTop(pos) > 0.5F;
	}

	/**
	 * The cell a mob stands in on the terrain here. Its collision sits up to an eighth of a block
	 * above the surface (Valheim's voxels), so a surface just under a cell's top already counts as
	 * that cell's floor.
	 */
	static int groundCell(float h) {
		return (int) Math.floor(h + 0.15F);
	}

	/** The height mobs stand at in this cell, from the terrain, or NaN where it's not the terrain's ground cell. */
	public static double floorAt(int x, int y, int z) {
		float h = terrain(x, z);
		return !Float.isNaN(h) && groundCell(h) == y ? Math.max(y, h) : Double.NaN;
	}

	/**
	 * Something other than the terrain fills this cell (a log, rock, trunk, wall). The cell the
	 * surface passes through holds a sliver of terrain too: only geometry reaching well above the
	 * surface counts there (counting it made every mob's own spot look blocked, and they planned
	 * their paths in the air).
	 */
	private static boolean object(int x, int y, int z, float h) {
		int ground = groundCell(h);
		if (y < ground) {
			return false;
		}
		BlockPos pos = new BlockPos(x, y, z);
		if (!ValCollision.hasGeometry(pos)) {
			return false;
		}
		float needed = y == ground ? Math.max(0.0F, h - ground) + 0.35F : 0.5F;
		return ValCollision.groundTop(pos) > needed;
	}

	public static boolean solid(int x, int y, int z) {
		float h = terrain(x, z);
		if (!Float.isNaN(h)) {
			return y < groundCell(h) || object(x, y, z, h);
		}
		if (geometry(x, y, z)) {
			return true;
		}
		// No terrain surface known: ground low in the cell above stands on this one.
		BlockPos above = new BlockPos(x, y + 1, z);
		return ValCollision.hasGeometry(above) && ValCollision.groundTop(above) <= 0.5F;
	}

	/** An object (not terrain) fills this cell, and it doesn't spread out: a trunk, bush or post. */
	public static boolean narrowObject(int x, int y, int z) {
		float h = terrain(x, z);
		boolean isObject = Float.isNaN(h) ? solid(x, y, z) : object(x, y, z, h);
		if (!isObject) {
			return false;
		}
		int around = 0;
		for (int dx = -1; dx <= 1; dx++) {
			for (int dz = -1; dz <= 1; dz++) {
				if (dx == 0 && dz == 0) {
					continue;
				}
				float hn = terrain(x + dx, z + dz);
				for (int dy = -1; dy <= 1; dy++) {
					boolean there = Float.isNaN(hn) ? geometry(x + dx, y + dy, z + dz) : object(x + dx, y + dy, z + dz, hn);
					if (there) {
						if (++around >= 4) {
							return false;
						}
						break;
					}
				}
			}
		}
		return true;
	}

	/**
	 * For the pathfinder: no way through this cell. Solid, or standing on something that isn't
	 * ground: with the terrain known, every Valheim object (a log, rock, trunk) has no walkable top
	 * (mobs that couldn't reach their target went to the nearest spot, often on top of a log, and
	 * got stuck there); without it, narrow objects only.
	 */
	public static boolean blockedForPath(int x, int y, int z) {
		if (solid(x, y, z)) {
			return true;
		}
		float h = terrain(x, z);
		if (!Float.isNaN(h)) {
			return y > groundCell(h) && solid(x, y - 1, z);
		}
		return narrowObject(x, y - 1, z);
	}

	/** Ground to stand on here: on the terrain surface when it's known, else on broad Valheim geometry. */
	public static boolean standable(int x, int y, int z) {
		if (solid(x, y, z) || solid(x, y + 1, z)) {
			return false;
		}
		float h = terrain(x, z);
		if (!Float.isNaN(h)) {
			return y == groundCell(h);
		}
		return solid(x, y - 1, z) && !narrowObject(x, y - 1, z);
	}

	private static boolean blockSolid(BlockGetter level, int x, int y, int z) {
		BlockPos pos = new BlockPos(x, y, z);
		return !level.getBlockState(pos).getCollisionShape(level, pos).isEmpty();
	}

	static boolean blockSolidAt(BlockGetter level, int x, int y, int z) {
		return blockSolid(level, x, y, z);
	}

	/** How high (0..1) the Minecraft block's collision reaches in its cell. */
	static double blockTop(BlockGetter level, int x, int y, int z) {
		BlockPos pos = new BlockPos(x, y, z);
		var shape = level.getBlockState(pos).getCollisionShape(level, pos);
		return shape.isEmpty() ? 0.0 : Math.min(1.0, shape.max(net.minecraft.core.Direction.Axis.Y));
	}

	/** Room for a body two high here: no Valheim object, terrain or Minecraft block in the way. */
	static boolean bodyFree(BlockGetter level, int x, int y, int z) {
		return free(level, x, y, z) && free(level, x, y + 1, z);
	}

	/** Free for a body (Valheim and Minecraft blocks). */
	private static boolean free(BlockGetter level, int x, int y, int z) {
		return !solid(x, y, z) && !blockSolid(level, x, y, z);
	}

	/** Somewhere to stand, on Valheim ground or a Minecraft block, with room for a body two high. */
	private static boolean standHere(BlockGetter level, int x, int y, int z) {
		if (!free(level, x, y, z) || !free(level, x, y + 1, z)) {
			return false;
		}
		if (standable(x, y, z) || blockSolid(level, x, y - 1, z)) {
			return true;
		}
		// Valheim objects are ground only where the terrain isn't known.
		return Float.isNaN(terrain(x, z)) && solid(x, y - 1, z) && !narrowObject(x, y - 1, z);
	}

	/**
	 * A straight walk to {@code target} (up to 24 blocks) when the ground allows one: steps of at most
	 * one block up or three down, nothing in the way. The block pathfinder took long detours over
	 * Valheim's uneven ground even when the way was clear. Null when there's no straight way.
	 */
	public static @Nullable Path straightPath(Mob mob, BlockPos target) {
		double sx = mob.getX(), sz = mob.getZ();
		double tx = target.getX() + 0.5, tz = target.getZ() + 0.5;
		double dist = Math.hypot(tx - sx, tz - sz);
		if (dist < 1.5 || dist > 24.0 || Math.abs(target.getY() - mob.getBlockY()) > 8) {
			return null;
		}
		BlockGetter level = mob.level();
		int y = mob.getBlockY();
		int lastX = mob.getBlockX(), lastZ = mob.getBlockZ();
		List<Node> nodes = new ArrayList<>();
		nodes.add(node(lastX, y, lastZ));
		int steps = (int) Math.ceil(dist / 0.4);
		for (int i = 1; i <= steps; i++) {
			double t = (double) i / steps;
			int bx = (int) Math.floor(sx + (tx - sx) * t), bz = (int) Math.floor(sz + (tz - sz) * t);
			if (bx == lastX && bz == lastZ) {
				continue;
			}
			int found = Integer.MIN_VALUE;
			for (int cy : new int[] { y, y + 1, y - 1, y - 2, y - 3 }) {
				if (standHere(level, bx, cy, bz)) {
					found = cy;
					break;
				}
			}
			// Diagonal steps: neither corner blocked (or the body would catch on it).
			if (found == Integer.MIN_VALUE || bx != lastX && bz != lastZ && (!free(level, bx, found, lastZ) || !free(level, lastX, found, bz))) {
				return null;
			}
			y = found;
			lastX = bx;
			lastZ = bz;
			nodes.add(node(bx, y, bz));
		}
		boolean reached = lastX == target.getX() && lastZ == target.getZ();
		return new Path(nodes, target, reached);
	}

	private static Node node(int x, int y, int z) {
		Node n = new Node(x, y, z);
		n.type = PathType.WALKABLE;
		return n;
	}
}
