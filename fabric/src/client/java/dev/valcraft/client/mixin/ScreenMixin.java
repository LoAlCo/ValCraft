package dev.valcraft.client.mixin;

import dev.valcraft.client.ValClient;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Valheim keeps running while a Minecraft screen (pause menu, options, inventory) is open, so the
 * Minecraft world must too: arrows keep flying and enemy hits still land.
 */
@Mixin(Screen.class)
public abstract class ScreenMixin {
	@Inject(method = "isPauseScreen", at = @At("HEAD"), cancellable = true)
	private void valcraft$neverPauseWhileLinked(CallbackInfoReturnable<Boolean> cir) {
		if (ValClient.linked()) {
			cir.setReturnValue(false);
		}
	}
}
