package dev.valcraft.client.mixin;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import net.minecraft.client.gui.components.BossHealthOverlay;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.gen.Invoker;

@Mixin(Hud.class)
public interface HudAccessor {
	@Accessor("bossOverlay")
	BossHealthOverlay valcraft$bossOverlay();

	// For ValheimHud, which draws Minecraft's armor and air around its own health and food bars
	@Invoker("getCameraPlayer")
	Player valcraft$cameraPlayer();

	@Invoker("getPlayerVehicleWithHealth")
	LivingEntity valcraft$vehicleWithHealth();

	@Invoker("getVehicleMaxHearts")
	int valcraft$vehicleMaxHearts(LivingEntity vehicle);

	@Invoker("extractAirBubbles")
	void valcraft$extractAirBubbles(GuiGraphicsExtractor graphics, Player player, int vehicleHearts, int y, int right);

	@Invoker("extractArmor")
	static void valcraft$extractArmor(GuiGraphicsExtractor graphics, Player player, int y, int heartRows, int rowHeight, int left) {
		throw new AssertionError();
	}
}
