package dev.valcraft.client.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.valcraft.client.ValClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** ValClient.paceFrame() locks us to Valheim's frame rate; don't let MC throttle on its own. */
@Mixin(FramerateLimitTracker.class)
public abstract class FramerateLimitTrackerMixin {
	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void valcraft$unlimited(CallbackInfoReturnable<Integer> cir) {
		if (ValClient.linked()) {
			cir.setReturnValue(260);
		}
	}
}
