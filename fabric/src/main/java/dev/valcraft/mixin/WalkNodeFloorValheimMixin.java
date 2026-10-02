package dev.valcraft.mixin;

import dev.valcraft.world.ValCollision;
import dev.valcraft.world.ValGround;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.pathfinder.WalkNodeEvaluator;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The pathfinder reads a cell's floor height off the block below, so on Valheim terrain it thought
 * every floor sat at a whole block while the ground is anywhere in between: mobs jumped for steps
 * that weren't there and dropped routes that were fine. It gets the terrain's real height now.
 */
@Mixin(WalkNodeEvaluator.class)
public abstract class WalkNodeFloorValheimMixin {
	@Inject(method = "getFloorLevel(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)D", at = @At("RETURN"), cancellable = true)
	private static void valcraft$terrainFloor(BlockGetter level, BlockPos pos, CallbackInfoReturnable<Double> cir) {
		if (!ValCollision.active()) {
			return;
		}
		double floor = ValGround.floorAt(pos.getX(), pos.getY(), pos.getZ());
		if (!Double.isNaN(floor) && floor > cir.getReturnValueD()) {
			cir.setReturnValue(floor);
		}
	}
}
