package dev.valcraft.mixin;

import dev.valcraft.world.ValCollision;
import dev.valcraft.world.ValGround;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.pathfinder.PathType;
import net.minecraft.world.level.pathfinder.WalkNodeEvaluator;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * In Valheim-terrain mode the Minecraft world is a void and Valheim's ground is collision only, so
 * mobs found no path and stood still. Their pathfinder sees it now (ValGround.blockedForPath).
 * Hooked where each block's path type is worked out, whose result the level caches per block: the
 * lookups ran on every query before, and slowed Minecraft's server down.
 */
@Mixin(WalkNodeEvaluator.class)
public abstract class PathfindingValheimMixin {
	@Inject(method = "getPathTypeFromState(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/pathfinder/PathType;",
		at = @At("RETURN"), cancellable = true)
	private static void valcraft$valheimGround(BlockGetter level, BlockPos pos, CallbackInfoReturnable<PathType> cir) {
		if (cir.getReturnValue() == PathType.OPEN && ValCollision.active() && ValGround.blockedForPath(pos.getX(), pos.getY(), pos.getZ())) {
			cir.setReturnValue(PathType.BLOCKED);
		}
	}
}
