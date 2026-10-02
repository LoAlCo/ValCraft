package dev.valcraft.world;

import dev.valcraft.link.ValLink;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.material.Fluids;
import org.jspecify.annotations.Nullable;

/**
 * Valheim's lakes, rivers and sea as Minecraft water: Valheim sends the water surface over the block
 * columns around the player (see WaterGrid in the protocol), and wherever Minecraft has air below
 * that surface, entities treat it as water, so the player swims, floats, sinks slowly and drowns
 * there as in Minecraft water. Only entity physics sees it; no blocks change.
 *
 * Beyond the grid (a boat left behind, a mob swimming off), Valheim's sea level stands in wherever
 * its terrain is below it: Valheim has one water level, and its lakes and rivers are ground below it.
 */
public final class ValWater {
	private record Grid(int originX, int originZ, int size, float[] surface) {
	}

	private static volatile @Nullable Grid grid;
	/** The water level last seen in the grid (waves averaged out), for columns outside it; NaN until seen. */
	private static volatile double seaLevel = Double.NaN;
	private static int seaWorld;

	private ValWater() {
	}

	/** Once a frame on the client: pick up Valheim's latest grid. */
	public static void refresh() {
		ValLink.WaterGrid read = ValLink.readWaterGrid();
		if (read != null) {
			grid = new Grid(read.originX, read.originZ, read.size, read.surface);
			if (read.worldId != seaWorld) {
				seaWorld = read.worldId;
				seaLevel = Double.NaN; // another world, or a dungeon: no sea known there yet
			}
			double sum = 0.0;
			int n = 0;
			for (float s : read.surface) {
				if (s > -1.0e20F) {
					sum += s;
					n++;
				}
			}
			if (n >= 8) {
				seaLevel = sum / n;
			}
		}
	}

	public static void clear() {
		grid = null;
	}

	public static boolean active() {
		return grid != null;
	}

	/** Minecraft y of Valheim's water surface over this column, or NaN where there is none. */
	public static double surfaceAt(int x, int z) {
		Grid g = grid;
		if (g == null) {
			return Double.NaN;
		}
		int dx = x - g.originX(), dz = z - g.originZ();
		if (dx < 0 || dz < 0 || dx >= g.size() || dz >= g.size()) {
			double sea = seaLevel;
			if (Double.isNaN(sea)) {
				return Double.NaN;
			}
			float ground = ValCollision.terrainHeight(x, z);
			return !Float.isNaN(ground) && ground < sea - 0.25 ? sea : Double.NaN;
		}
		float s = g.surface()[dz * g.size() + dx];
		return s < -1.0e20F ? Double.NaN : s;
	}

	/**
	 * The water surface at any point, blended between the four nearest columns (each column's value
	 * is its centre's), for a smooth wave slope under a boat. NaN if any of them has no water.
	 */
	public static double surfaceSmooth(double x, double z) {
		double fx = x - 0.5, fz = z - 0.5;
		int x0 = (int) Math.floor(fx), z0 = (int) Math.floor(fz);
		double tx = fx - x0, tz = fz - z0;
		double a = surfaceAt(x0, z0), b = surfaceAt(x0 + 1, z0), c = surfaceAt(x0, z0 + 1), d = surfaceAt(x0 + 1, z0 + 1);
		double top = a + (b - a) * tx, bottom = c + (d - c) * tx;
		return top + (bottom - top) * tz;
	}

	/** How much of this block (0..1) is under Valheim's water; 0 above the surface. */
	public static float depthIn(BlockPos pos) {
		double s = surfaceAt(pos.getX(), pos.getZ());
		if (Double.isNaN(s)) {
			return 0.0F;
		}
		double h = s - pos.getY();
		return h < 0.02 ? 0.0F : (float) Math.min(1.0, h);
	}

	/** True if Valheim water reaches up into the box of block cells (inclusive). */
	public static boolean anyIn(int x0, int y0, int z0, int x1, int y1, int z1) {
		if (grid == null) {
			return false;
		}
		for (int x = x0; x <= x1; x++) {
			for (int z = z0; z <= z1; z++) {
				double s = surfaceAt(x, z);
				if (!Double.isNaN(s) && s > y0) {
					return true;
				}
			}
		}
		return false;
	}

	/** Valheim water in an otherwise empty (air) Minecraft cell, as a Minecraft fluid; null if none. */
	public static @Nullable FluidState fluidAt(BlockGetter level, BlockPos pos) {
		if (depthIn(pos) <= 0.0F || !level.getBlockState(pos).isAir()) {
			return null;
		}
		return Fluids.WATER.getSource(false);
	}

	/**
	 * How far Valheim's surface is above the bottom of a cell only Valheim fills, not capped at one
	 * block (-1 otherwise). A boat takes its water level from the cells around its bottom; with a cap
	 * a wave pushing it one cell down would leave it "afloat" there, under the real surface.
	 */
	public static float surfaceAbove(BlockGetter level, BlockPos pos) {
		if (depthIn(pos) <= 0.0F || !level.getFluidState(pos).isEmpty() || !level.getBlockState(pos).isAir()) {
			return -1.0F;
		}
		return (float) (surfaceAt(pos.getX(), pos.getZ()) - pos.getY());
	}

	/** The exact water height in a cell only Valheim fills (so floating matches its surface); -1 otherwise. */
	public static float substitutedHeight(BlockGetter level, BlockPos pos) {
		float depth = depthIn(pos);
		if (depth <= 0.0F || !level.getFluidState(pos).isEmpty() || !level.getBlockState(pos).isAir()) {
			return -1.0F;
		}
		return depth;
	}
}
