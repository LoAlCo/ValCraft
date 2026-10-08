package dev.valcraft.client.mixin;

import dev.valcraft.client.ValClient;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Valheim shows Minecraft's bosses with its own boss bar (BossBars): Minecraft's isn't drawn on top. */
@Mixin(Hud.class)
public abstract class HudBossBarMixin {
	@Inject(method = "extractBossOverlay", at = @At("HEAD"), cancellable = true)
	private void valcraft$valheimDrawsBossBars(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo ci) {
		if (ValClient.linked()) {
			ci.cancel();
		}
	}
}
