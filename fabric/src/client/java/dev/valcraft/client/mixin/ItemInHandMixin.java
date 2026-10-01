package dev.valcraft.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import com.mojang.blaze3d.vertex.PoseStack;
import dev.valcraft.client.ValClient;
import dev.valcraft.client.render.AvatarExporter;
import net.minecraft.client.renderer.FirstPersonHandsAndItemsRenderer;
import net.minecraft.client.renderer.GameRenderer;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.FirstPersonHandsAndItemsRenderState;
import net.minecraft.client.renderer.state.level.PlayerRenderState;
import org.joml.Matrix3f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * While linked, the first-person hands and held items aren't drawn into Minecraft's frame (which
 * Valheim lays over its picture, unlit by its world and see-through at the edges): they're captured
 * as a mesh, already posed (swing, sway, bob, eating), for Valheim to draw in its own scene.
 */
@Mixin(GameRenderer.class)
public abstract class ItemInHandMixin {
	@WrapOperation(
		method = "renderItemInHand",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/renderer/FirstPersonHandsAndItemsRenderer;submitHandsWithItems(FLcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/SubmitNodeCollector;Lnet/minecraft/client/renderer/state/level/PlayerRenderState;Lnet/minecraft/client/renderer/state/level/FirstPersonHandsAndItemsRenderState;)V"
		)
	)
	private void valcraft$handsToValheim(FirstPersonHandsAndItemsRenderer renderer, float partialTick, PoseStack poseStack, SubmitNodeCollector collector,
		PlayerRenderState player, FirstPersonHandsAndItemsRenderState hands, Operation<Void> original, @Local(argsOnly = true) CameraRenderState camera) {
		SubmitNodeCollector capture = ValClient.linked() ? AvatarExporter.beginViewModel() : null;
		if (capture == null) {
			original.call(renderer, partialTick, poseStack, collector, player, hands);
			return;
		}
		// renderItemInHand starts the pose with the inverse view rotation (its model-view matrix puts
		// it back). Valheim wants the hands in view space, as they sit on screen: take it out again.
		poseStack.pushPose();
		poseStack.last().pose().mulLocal(camera.viewRotationMatrix);
		poseStack.last().normal().mulLocal(new Matrix3f(camera.viewRotationMatrix));
		try {
			original.call(renderer, partialTick, poseStack, capture, player, hands);
		} finally {
			poseStack.popPose();
		}
		AvatarExporter.endViewModel();
	}
}
