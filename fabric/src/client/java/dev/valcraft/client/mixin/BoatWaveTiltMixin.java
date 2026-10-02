package dev.valcraft.client.mixin;

import com.mojang.blaze3d.vertex.PoseStack;
import dev.valcraft.world.ValWater;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.entity.AbstractBoatRenderer;
import net.minecraft.client.renderer.entity.state.BoatRenderState;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import org.joml.Quaternionf;
import org.joml.Vector3f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * A boat on Valheim's water tilts with its waves: before Minecraft turns it to its heading, the
 * model is tilted onto the slope of the water surface under it (from the surface on either side).
 */
@Mixin(AbstractBoatRenderer.class)
public abstract class BoatWaveTiltMixin {
	private static final double SPAN = 0.8; // blocks either side of the centre the slope is taken over
	private static final float MAX_TILT = (float) Math.toRadians(20.0);

	@Inject(
		method = "submit(Lnet/minecraft/client/renderer/entity/state/BoatRenderState;Lcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/SubmitNodeCollector;Lnet/minecraft/client/renderer/state/level/CameraRenderState;)V",
		at = @At(value = "INVOKE", target = "Lcom/mojang/blaze3d/vertex/PoseStack;rotateDegrees(Lcom/mojang/math/Axis;F)V", ordinal = 0)
	)
	private void valcraft$tiltWithWaves(BoatRenderState state, PoseStack poseStack, SubmitNodeCollector collector, CameraRenderState camera, CallbackInfo ci) {
		if (!ValWater.active()) {
			return;
		}
		double centre = ValWater.surfaceSmooth(state.x, state.z);
		if (Double.isNaN(centre) || Math.abs(state.y - centre) > 1.0) {
			return; // not floating on Valheim's water (on land, or far below a surface)
		}
		double east = ValWater.surfaceSmooth(state.x + SPAN, state.z), west = ValWater.surfaceSmooth(state.x - SPAN, state.z);
		double south = ValWater.surfaceSmooth(state.x, state.z + SPAN), north = ValWater.surfaceSmooth(state.x, state.z - SPAN);
		if (Double.isNaN(east) || Double.isNaN(west) || Double.isNaN(south) || Double.isNaN(north)) {
			return;
		}
		Vector3f normal = new Vector3f((float) -(east - west), (float) (2.0 * SPAN), (float) -(south - north)).normalize();
		float angle = (float) Math.acos(Math.min(1.0F, normal.y));
		if (angle < 1.0e-3F) {
			return;
		}
		float clamped = Math.min(angle, MAX_TILT);
		Vector3f axis = new Vector3f(0.0F, 1.0F, 0.0F).cross(normal).normalize();
		poseStack.rotate(new Quaternionf().rotationAxis(clamped, axis));
	}
}
