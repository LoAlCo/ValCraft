package dev.valcraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.valcraft.world.ValWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.EntityFluidInteraction;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Entities meet Valheim's water (lakes, rivers, the sea) as Minecraft water: swimming, floating,
 * slow movement, drowning, splashes. See {@link ValWater}.
 */
@Mixin(EntityFluidInteraction.class)
public abstract class EntityFluidInteractionMixin {
	@WrapOperation(
		method = "update",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/world/entity/EntityFluidInteraction;hasFluidAndLoaded(Lnet/minecraft/world/level/Level;IIIIII)Z"
		)
	)
	private static boolean valcraft$valheimWaterNearby(Level level, int x0, int y0, int z0, int x1, int y1, int z1, Operation<Boolean> original) {
		return original.call(level, x0, y0, z0, x1, y1, z1) || ValWater.anyIn(x0, y0, z0, x1, y1, z1);
	}

	@WrapOperation(
		method = "update",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/BlockGetter;getFluidState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/material/FluidState;")
	)
	private FluidState valcraft$valheimWater(BlockGetter level, BlockPos pos, Operation<FluidState> original) {
		FluidState state = original.call(level, pos);
		if (state.isEmpty() && ValWater.active()) {
			FluidState water = ValWater.fluidAt(level, pos);
			if (water != null) {
				return water;
			}
		}
		return state;
	}

	@WrapOperation(
		method = "update",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/world/level/material/FluidState;getHeight(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)F"
		)
	)
	private float valcraft$valheimWaterHeight(FluidState state, BlockGetter level, BlockPos pos, Operation<Float> original) {
		float height = ValWater.active() ? ValWater.substitutedHeight(level, pos) : -1.0F;
		return height >= 0.0F ? height : original.call(state, level, pos);
	}

	@WrapOperation(
		method = "update",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/world/level/material/FluidState;getHeightForCamera(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)F"
		)
	)
	private float valcraft$valheimWaterEyeHeight(FluidState state, BlockGetter level, BlockPos pos, Operation<Float> original) {
		float height = ValWater.active() ? ValWater.substitutedHeight(level, pos) : -1.0F;
		return height >= 0.0F ? height : original.call(state, level, pos);
	}
}
