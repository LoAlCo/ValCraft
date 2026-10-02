package dev.valcraft.mixin;

import dev.valcraft.world.BuildSync;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.BucketItem;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Water or lava poured from a bucket is noted for BuildSync. */
@Mixin(BucketItem.class)
public abstract class BucketItemSyncMixin {
	@Inject(method = "emptyContents", at = @At("RETURN"))
	private void valcraft$notePoured(LivingEntity user, Level level, BlockPos pos, BlockHitResult hit, CallbackInfoReturnable<Boolean> cir) {
		if (cir.getReturnValueZ() && !level.isClientSide()) {
			BuildSync.touch(level, pos, level.getBlockState(pos));
		}
	}
}
