package dev.valcraft.mixin;

import dev.valcraft.world.BuildSync;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Blocks set while a command runs are noted for BuildSync (nothing else: terrain, fluids, the sync's own writes). */
@Mixin(Level.class)
public abstract class LevelSetBlockSyncMixin {
	@Inject(method = "setBlock(Lnet/minecraft/core/BlockPos;Lnet/minecraft/world/level/block/state/BlockState;II)Z", at = @At("RETURN"))
	private void valcraft$noteCommandBlock(BlockPos pos, BlockState state, int flags, int recursionLeft, CallbackInfoReturnable<Boolean> cir) {
		if (cir.getReturnValueZ() && BuildSync.inCommand()) {
			BuildSync.touch((Level) (Object) this, pos, state);
		}
	}
}
