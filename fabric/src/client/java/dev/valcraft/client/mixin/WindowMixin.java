package dev.valcraft.client.mixin;

import com.mojang.blaze3d.platform.Window;
import dev.valcraft.client.ValClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The MC window is hidden while linked; Valheim has the real focus, so pretend we do too. */
@Mixin(Window.class)
public abstract class WindowMixin {
	@Inject(method = "isFocused", at = @At("HEAD"), cancellable = true)
	private void valcraft$focused(CallbackInfoReturnable<Boolean> cir) {
		if (ValClient.tookOver()) {
			// Focused while Valheim is connected; if Valheim goes away, act unfocused so MC
			// never tries to grab the (hidden) mouse.
			cir.setReturnValue(ValClient.linked());
		}
	}

	@Inject(method = "isIconified", at = @At("HEAD"), cancellable = true)
	private void valcraft$notIconified(CallbackInfoReturnable<Boolean> cir) {
		if (ValClient.linked()) {
			cir.setReturnValue(false);
		}
	}
}
