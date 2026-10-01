package dev.valcraft.client.mixin;

import dev.valcraft.client.DiscordPresence;
import net.minecraft.client.gui.components.ChatComponent;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/** Every chat line passes by Discord Rich Presence, which picks e4mc's link out of it (unchanged). */
@Mixin(ChatComponent.class)
public abstract class ChatComponentMixin {
	@ModifyVariable(method = "addMessage", at = @At("HEAD"), argsOnly = true, ordinal = 0)
	private Component valcraft$watchForLink(Component contents) {
		DiscordPresence.onChat(contents);
		return contents;
	}
}
