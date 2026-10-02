package dev.valcraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.valcraft.world.ValWater;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.BoatItem;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * A boat goes on Valheim's water where the player looks, when that's nearer than anything solid; on
 * Valheim's uneven ground (to push into the water) it's lifted until it clears the slope.
 */
@Mixin(BoatItem.class)
public abstract class BoatItemValheimMixin {
	@WrapOperation(
		method = "use",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/world/item/BoatItem;getPlayerPOVHitResult(Lnet/minecraft/world/level/Level;Lnet/minecraft/world/entity/player/Player;Lnet/minecraft/world/level/ClipContext$Fluid;)Lnet/minecraft/world/phys/BlockHitResult;"
		)
	)
	private BlockHitResult valcraft$onValheimWater(Level level, Player player, ClipContext.Fluid fluid, Operation<BlockHitResult> original) {
		BlockHitResult hit = original.call(level, player, fluid);
		if (!ValWater.active()) {
			return hit;
		}
		Vec3 eye = player.getEyePosition();
		Vec3 dir = player.getViewVector(1.0F);
		double reach = player.blockInteractionRange();
		double solid = hit.getType() == HitResult.Type.MISS ? reach : eye.distanceTo(hit.getLocation());
		// March along the look until it dips under Valheim's surface.
		for (double d = 0.0; d <= solid; d += 0.05) {
			Vec3 p = eye.add(dir.scale(d));
			double surface = ValWater.surfaceAt((int) Math.floor(p.x), (int) Math.floor(p.z));
			if (!Double.isNaN(surface) && p.y <= surface) {
				Vec3 at = new Vec3(p.x, surface, p.z);
				return new BlockHitResult(at, Direction.UP, BlockPos.containing(at), false);
			}
		}
		return hit;
	}

	@WrapOperation(
		method = "use",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;noCollision(Lnet/minecraft/world/entity/Entity;Lnet/minecraft/world/phys/AABB;)Z")
	)
	private boolean valcraft$clearOfSlope(Level level, net.minecraft.world.entity.Entity boat, net.minecraft.world.phys.AABB box, Operation<Boolean> original) {
		if (original.call(level, boat, box)) {
			return true;
		}
		for (double up = 0.1; up <= 1.2; up += 0.1) {
			if (original.call(level, boat, box.move(0.0, up, 0.0))) {
				boat.setPos(boat.getX(), boat.getY() + up, boat.getZ());
				return true;
			}
		}
		return false;
	}
}
