package dev.valcraft.world;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

/**
 * Makes Minecraft ray casts (arrows and other projectiles, the crosshair pick) hit Valheim's exact
 * collision triangles. Vanilla still clips against real Minecraft blocks; whichever is nearer wins.
 */
public final class ValClip {
	private ValClip() {
	}

	public enum Use {
		/** A projectile: the hit cell is the one the surface is in (it sticks there). */
		PROJECTILE,
		/** The player's crosshair: the hit cell is where a block placed against the surface goes. */
		PICK
	}

	private static final ThreadLocal<List<ValTri>> SCRATCH = ThreadLocal.withInitial(ArrayList::new);

	public static BlockHitResult refine(Vec3 from, Vec3 to, BlockHitResult vanilla, Use use) {
		ValRay.Hit hit = cast(from, to);
		if (hit == null) {
			return vanilla;
		}
		Vec3 location = new Vec3(hit.x(), hit.y(), hit.z());
		if (vanilla.getType() != HitResult.Type.MISS && from.distanceToSqr(vanilla.getLocation()) <= from.distanceToSqr(location)) {
			return vanilla;
		}
		Direction face = Direction.values()[ValRay.dominantFace(hit.nx(), hit.ny(), hit.nz())];
		int[] cell = use == Use.PICK ? ValRay.placementCell(hit) : ValRay.surfaceCell(hit);
		return new ValheimHitResult(location, face, new BlockPos(cell[0], cell[1], cell[2]), hit.nx(), hit.ny(), hit.nz());
	}

	/** Nearest Valheim triangle hit on the segment, or null. */
	public static ValRay.Hit cast(Vec3 from, Vec3 to) {
		List<ValTri> tris = SCRATCH.get();
		tris.clear();
		ValCollision.trianglesNear(new AABB(from, to).inflate(0.01), tris);
		if (tris.isEmpty()) {
			return null;
		}
		ValRay.Hit hit = ValRay.cast(tris, from.x, from.y, from.z, to.x, to.y, to.z);
		tris.clear();
		return hit;
	}

	/** A hit on Valheim geometry (not a Minecraft block). Keeps the exact surface normal. */
	public static final class ValheimHitResult extends BlockHitResult {
		public final double nx, ny, nz;

		public ValheimHitResult(Vec3 location, Direction direction, BlockPos pos, double nx, double ny, double nz) {
			super(location, direction, pos, false);
			this.nx = nx;
			this.ny = ny;
			this.nz = nz;
		}
	}
}
