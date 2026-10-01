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
 */
public final class ValWater {
	private record Grid(int originX, int originZ, int size, float[] surface) {
	}

	private static volatile @Nullable Grid grid;

	private ValWater() {
	}

	/** Once a frame on the client: pick up Valheim's latest grid. */
	public static void refresh() {
		ValLink.WaterGrid read = ValLink.readWaterGrid();
		if (read != null) {
			grid = new Grid(read.originX, read.originZ, read.size, read.surface);
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
			return Double.NaN;
		}
		float s = g.surface()[dz * g.size() + dx];
		return s < -1.0e20F ? Double.NaN : s;
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

	/** The exact water height in a cell only Valheim fills (so floating matches its surface); -1 otherwise. */
	public static float substitutedHeight(BlockGetter level, BlockPos pos) {
		float depth = depthIn(pos);
		if (depth <= 0.0F || !level.getFluidState(pos).isEmpty() || !level.getBlockState(pos).isAir()) {
			return -1.0F;
		}
		return depth;
	}
}
