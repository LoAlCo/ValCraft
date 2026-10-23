package dev.valcraft.client.mixin;

import dev.valcraft.client.ui.ValheimHud;
import dev.valcraft.client.ui.ValheimUi;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** [UI] Style Valheim: a health bar and a food bar instead of Minecraft's hearts and hunger (ValheimHud). */
@Mixin(Hud.class)
public abstract class HudValheimStyleMixin {
	@Inject(method = "extractPlayerHealth", at = @At("HEAD"), cancellable = true)
	private void valcraft$valheimBars(GuiGraphicsExtractor graphics, CallbackInfo ci) {
		if (ValheimUi.active()) {
			ValheimHud.draw(graphics, (Hud) (Object) this);
			ci.cancel();
		}
	}
}
