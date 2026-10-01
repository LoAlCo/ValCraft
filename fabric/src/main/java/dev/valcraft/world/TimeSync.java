package dev.valcraft.world;

import dev.valcraft.ValCraft;
import dev.valcraft.link.ValLink;
import net.minecraft.server.MinecraftServer;

/**
 * Minecraft's time of day follows Valheim's (Minecraft's own clock is stopped, see
 * ValCraft.configureServer): night falls in both at once, so the hand, HUD and sky light dim with
 * Valheim's night, and beds and anything else that cares about night agree with it.
 */
public final class TimeSync {
	private static final ValLink.ValState STATE = new ValLink.ValState();
	private static int ticks;
	private static long lastSet = Long.MIN_VALUE;

	private TimeSync() {
	}

	/** Server thread, every tick. */
	public static void tick(MinecraftServer server) {
		if (++ticks % 20 != 0 || !ValLink.active() || !ValLink.readValState(STATE) || !STATE.inGame()) {
			return;
		}
		// Minecraft time 0 is 06:00, 6000 noon, 18000 midnight.
		long target = Math.floorMod(Math.round((STATE.gameHour - 6.0F) * 1000.0F), 24000L);
		var level = server.overworld();
		long now = Math.floorMod(level.getDefaultClockTime(), 24000L);
		long drift = Math.abs(target - now);
		drift = Math.min(drift, 24000L - drift);
		if (drift < 100 || target == lastSet) {
			return;
		}
		lastSet = target;
		server.getCommands().performPrefixedCommand(server.createCommandSourceStack().withSuppressedOutput(), "time set " + target);
		if (drift > 2000) {
			ValCraft.LOG.info("ValCraft: Minecraft time follows Valheim's {}:00 (time {})", (int) STATE.gameHour, target);
		}
	}
}
