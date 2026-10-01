package dev.valcraft.client;

import dev.valcraft.world.ValCollision;
import dev.valcraft.world.ValTri;
import dev.valcraft.world.TriCollider;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/** Feeds the local player's movement through {@link TriCollider} against nearby Valheim triangles. */
public final class ValCollider {
	private ValCollider() {
	}

	public static Vec3 collide(LocalPlayer player, Vec3 move) {
		AABB box = player.getBoundingBox();
		double step = player.maxUpStep();
		List<ValTri> tris = new ArrayList<>();
		ValCollision.trianglesNear(box.expandTowards(move).inflate(1.0, 1.0 + step, 1.0), tris);
		if (tris.isEmpty()) {
			return move;
		}
		double[] r = TriCollider.resolve(
			tris, (box.minX + box.maxX) * 0.5, box.minY, (box.minZ + box.maxZ) * 0.5, box.getXsize() * 0.5, box.getYsize(), step, player.onGround(),
			move.x, move.y, move.z
		);
		if (r[0] == move.x && r[1] == move.y && r[2] == move.z) {
			return move;
		}
		// The triangle pass (snapping down a slope, pushing out of a wall) can move the player into a
		// Minecraft block placed on the terrain; collide that result with Minecraft blocks again.
		return Entity.collideBoundingBox(player, new Vec3(r[0], r[1], r[2]), box, player.level(), List.of());
	}

	/** Highest Valheim surface at or below {@code maxAbove} over the feet at (x, y, z), or NaN. */
	public static double groundAt(double x, double y, double z, double maxAbove) {
		List<ValTri> tris = new ArrayList<>();
		ValCollision.trianglesNear(new AABB(x - 1, y - 4, z - 1, x + 1, y + maxAbove + 1, z + 1), tris);
		return TriCollider.groundAt(tris, x, y, z, maxAbove);
	}
}
