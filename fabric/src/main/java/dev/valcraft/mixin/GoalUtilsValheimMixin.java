package dev.valcraft.mixin;

import dev.valcraft.world.ValCollision;
import dev.valcraft.world.ValGround;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.PathfinderMob;
import net.minecraft.world.entity.ai.navigation.PathNavigation;
import net.minecraft.world.entity.ai.util.GoalUtils;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Where a mob may go when it wanders or panics (a hit pig running off) is picked with these
 * checks, which look at real blocks only: on Valheim terrain every spot looked like thin air.
 * Valheim ground counts now, the broad kind only (ValGround): counting any geometry made spots
 * against tree trunks (geometry at every height) the likeliest picks, and mobs ran to the trees.
 */
@Mixin(GoalUtils.class)
public abstract class GoalUtilsValheimMixin {
	@Inject(method = "isNotStable", at = @At("RETURN"), cancellable = true)
	private static void valcraft$standOnValheim(PathNavigation navigation, BlockPos pos, CallbackInfoReturnable<Boolean> cir) {
		if (cir.getReturnValueZ() && ValCollision.active() && ValGround.standable(pos.getX(), pos.getY(), pos.getZ())) {
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "isSolid", at = @At("RETURN"), cancellable = true)
	private static void valcraft$valheimIsSolid(PathfinderMob mob, BlockPos pos, CallbackInfoReturnable<Boolean> cir) {
		if (!cir.getReturnValueZ() && ValCollision.active() && ValGround.solid(pos.getX(), pos.getY(), pos.getZ())) {
			cir.setReturnValue(true);
		}
	}
}
