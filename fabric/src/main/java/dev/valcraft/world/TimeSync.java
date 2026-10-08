package dev.valcraft.world;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import net.minecraft.server.MinecraftServer;

/**
 * Minecraft's time of day follows Valheim's (Minecraft's own clock is stopped, see
 * ValCraft.configureServer): night falls in both at once, so the hand, HUD and sky light dim with
 * Valheim's night, and beds and anything else that cares about night agree with it. Minecraft's
 * /time set and /time add go the other way: they move Valheim's clock (EV_SET_TIME, forward only,
 * the host's world), and until Valheim's sky has caught up Minecraft keeps the commanded time.
 */
public final class TimeSync {
	private static final ValLink.ValState STATE = new ValLink.ValState();
	private static int ticks;
	private static long lastSet = Long.MIN_VALUE;
	/** Set while TimeSync runs its own "time set", so TimeCommandMixin leaves it out. */
	private static boolean applying;
	/** After a /time command: Minecraft's time of day that Valheim is moving to, until when (ticks). */
	private static long holdTarget = -1, holdUntil;

	private TimeSync() {
	}

	/**
	 * A player's /time command changed Minecraft's clock (it was {@code before}): Valheim's goes
	 * forward to the same time of day, plus the whole days a /time add skipped.
	 */
	public static void commanded(MinecraftServer server, long before, boolean add) {
		if (applying || !ValLink.active()) {
			return;
		}
		long after = server.overworld().getDefaultClockTime();
		long timeOfDay = Math.floorMod(after, 24000L);
		long days = add ? Math.max(0, (after - before) / 24000L) : 0;
		float hour = (float) ((timeOfDay / 1000.0 + 6.0) % 24.0);
		ValLink.pushEvent(Proto.EV_SET_TIME, 0, hour, days, 0, 0, 0);
		holdTarget = timeOfDay;
		holdUntil = server.getTickCount() + 20 * 20;
		lastSet = Long.MIN_VALUE;
		ValCraft.LOG.info("ValCraft: /time: Valheim goes to {}:{} (+{} days)", (int) hour, String.format("%02d", (int) (hour % 1 * 60)), days);
	}

	private static int weatherSet = -1;

	/**
	 * Minecraft's weather follows Valheim's (its own is stopped, see ValCraft.configureServer): rain
	 * when it rains or snows where the player is, thunder in a thunderstorm, clear otherwise. Minecraft
	 * isn't drawn, so this is what its rain does: monsters out in it don't burn in the day, fire goes
	 * out, crops get water, and so on. Valheim's interiors are dry.
	 */
	private static void weather(MinecraftServer server) {
		int want = (STATE.flags & Proto.VAL_THUNDER) != 0 ? 2 : (STATE.flags & Proto.VAL_WET) != 0 ? 1 : 0;
		var level = server.overworld();
		int now = level.isThundering() ? 2 : level.isRaining() ? 1 : 0;
		if (want == now && want == weatherSet) {
			return;
		}
		weatherSet = want;
		applying = true;
		try {
			server.getCommands().performPrefixedCommand(server.createCommandSourceStack().withSuppressedOutput(),
				want == 2 ? "weather thunder" : want == 1 ? "weather rain" : "weather clear");
		} finally {
			applying = false;
		}
		ValCraft.LOG.info("ValCraft: Minecraft's weather follows Valheim's: {}", want == 2 ? "thunderstorm" : want == 1 ? "rain" : "clear");
	}

	/** Server thread, every tick. */
	public static void tick(MinecraftServer server) {
		if (++ticks % 20 != 0 || !ValLink.active() || !ValLink.readValState(STATE) || !STATE.inGame()) {
			return;
		}
		weather(server);
		// Minecraft time 0 is 06:00, 6000 noon, 18000 midnight.
		long target = Math.floorMod(Math.round((STATE.gameHour - 6.0F) * 1000.0F), 24000L);
		var level = server.overworld();
		long now = Math.floorMod(level.getDefaultClockTime(), 24000L);
		long drift = Math.abs(target - now);
		drift = Math.min(drift, 24000L - drift);
		if (holdTarget >= 0) {
			// Valheim's sky turns to the new time over a few seconds: don't pull Minecraft back meanwhile.
			long off = Math.abs(target - holdTarget);
			if (Math.min(off, 24000L - off) > 200 && server.getTickCount() < holdUntil) {
				return;
			}
			holdTarget = -1;
		}
		if (drift < 100 || target == lastSet) {
			return;
		}
		lastSet = target;
		applying = true;
		try {
			server.getCommands().performPrefixedCommand(server.createCommandSourceStack().withSuppressedOutput(), "time set " + target);
		} finally {
			applying = false;
		}
		if (drift > 2000) {
			ValCraft.LOG.info("ValCraft: Minecraft time follows Valheim's {}:00 (time {})", (int) STATE.gameHour, target);
		}
	}
}
