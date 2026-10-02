package dev.valcraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.valcraft.world.ValWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.vehicle.boat.AbstractBoat;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Boats float on Valheim's water (lakes, rivers, the sea) like on Minecraft water: they sit at its
 * surface, so they bob on its waves, row at water speed, and stop being "on land". See {@link ValWater}.
 */
@Mixin(AbstractBoat.class)
public abstract class AbstractBoatValheimMixin {
	@WrapOperation(
		// Not isUnderwater: a wave crest rolling over a boat would count as water above it, and an
		// "underwater" boat sinks. Valheim's waves only lift a boat (its buoyancy in checkInWater).
		method = { "checkInWater", "getWaterLevelAbove", "checkFallDamage" },
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;getFluidState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/material/FluidState;")
	)
	private FluidState valcraft$valheimWater(Level level, BlockPos pos, Operation<FluidState> original) {
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
		method = { "checkInWater", "getWaterLevelAbove" },
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/material/FluidState;getHeight(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)F")
	)
	private float valcraft$valheimWaterHeight(FluidState state, BlockGetter level, BlockPos pos, Operation<Float> original) {
		float height = ValWater.active() ? ValWater.surfaceAbove(level, pos) : -1.0F;
		return height >= 0.0F ? height : original.call(state, level, pos);
	}
}
