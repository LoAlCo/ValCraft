package dev.valcraft.client;

import dev.valcraft.combat.ValheimVitals;
import dev.valcraft.link.Proto;
import dev.valcraft.net.ValNet;
import java.util.Arrays;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.minecraft.client.Minecraft;
import net.minecraft.server.level.ServerPlayer;

/**
 * [Valheim Features] FoodAndHealth on this side: what this machine's Valheim says about food and max
 * health, for this game (ValheimVitals' local state, the HUD, the eating rules) and, in a friend's
 * world, for their server; and Valheim's healing (IN_HEAL) for the player.
 */
public final class VitalsClient {
	private static ValheimVitals.State sent;
	private static int sentAge;

	private VitalsClient() {
	}

	/** Every client tick. */
	public static void tick(Minecraft minecraft) {
		var sky = ValClient.sky();
		ValheimVitals.State state = ValheimVitals.OFF;
		if (ValClient.linked() && (sky.flags & Proto.VAL_FOOD_VALHEIM) != 0) {
			int n = sky.foodCount;
			state = new ValheimVitals.State(true, sky.maxHealth, Arrays.copyOf(sky.foodHash, n), Arrays.copyOf(sky.foodTime, n),
				Arrays.copyOf(sky.foodBurn, n), Arrays.copyOf(sky.foodHealth, n));
		}
		ValheimVitals.setLocal(state);
		// a guest: the host's server needs it too (when it changes, and now and then for the timers)
		if (minecraft.player != null && minecraft.getSingleplayerServer() == null && ClientPlayNetworking.canSend(ValNet.Vitals.TYPE)) {
			boolean changed = sent == null || sent.on() != state.on() || Math.abs(sent.maxHealth() - state.maxHealth()) > 0.5f
				|| !Arrays.equals(sent.hash(), state.hash());
			if (changed || ++sentAge >= 20) {
				ClientPlayNetworking.send(new ValNet.Vitals(state.on(), state.maxHealth(), state.hash(), state.time(), state.burn(), state.health()));
				sent = state;
				sentAge = 0;
			}
		}
	}

	/** Valheim healed the player (Valheim health). */
	public static void heal(Minecraft minecraft, float valheimHealth) {
		if (minecraft.player == null || valheimHealth <= 0) {
			return;
		}
		var server = minecraft.getSingleplayerServer();
		if (server == null) {
			if (ClientPlayNetworking.canSend(ValNet.Heal.TYPE)) {
				ClientPlayNetworking.send(new ValNet.Heal(valheimHealth));
			}
			return;
		}
		var uuid = minecraft.player.getUUID();
		server.execute(() -> {
			ServerPlayer player = server.getPlayerList().getPlayer(uuid);
			if (player != null) {
				ValheimVitals.heal(player, valheimHealth);
			}
		});
	}
}
