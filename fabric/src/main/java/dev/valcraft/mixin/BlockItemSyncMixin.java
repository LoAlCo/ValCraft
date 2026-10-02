package dev.valcraft.mixin;

import dev.valcraft.world.BuildSync;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.context.BlockPlaceContext;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** A placed block is noted for BuildSync (the other terrain mode's save gets it too). */
@Mixin(BlockItem.class)
public abstract class BlockItemSyncMixin {
	@Inject(method = "place(Lnet/minecraft/world/item/context/BlockPlaceContext;)Lnet/minecraft/world/InteractionResult;", at = @At("RETURN"))
	private void valcraft$notePlaced(BlockPlaceContext context, CallbackInfoReturnable<InteractionResult> cir) {
		if (cir.getReturnValue().consumesAction() && !context.getLevel().isClientSide()) {
			var pos = context.getClickedPos();
			BuildSync.touch(context.getLevel(), pos, context.getLevel().getBlockState(pos));
		}
	}
}
