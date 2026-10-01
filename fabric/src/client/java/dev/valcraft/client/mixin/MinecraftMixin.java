package dev.valcraft.client.mixin;

import dev.valcraft.client.ValClient;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
	@Inject(method = "runTick", at = @At("HEAD"))
	private void valcraft$beginFrame(boolean advanceGameTime, CallbackInfo ci) {
		ValClient.beginFrame();
	}

	// Before extract, not render: GameRenderer.extract() is where the hand's sway angles are captured.
	@Inject(
		method = "renderFrame",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/renderer/GameRenderer;extract(Lnet/minecraft/client/DeltaTracker;Z)V")
	)
	private void valcraft$beforeRender(boolean advanceGameTime, CallbackInfo ci) {
		ValClient.beforeRender();
	}

	@Inject(
		method = "renderFrame",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/renderer/GameRenderer;render()V", shift = At.Shift.AFTER)
	)
	private void valcraft$afterRender(boolean advanceGameTime, CallbackInfo ci) {
		ValClient.afterRender();
	}

	@Inject(method = "renderFrame", at = @At("TAIL"))
	private void valcraft$pace(boolean advanceGameTime, CallbackInfo ci) {
		ValClient.paceFrame();
	}
}
