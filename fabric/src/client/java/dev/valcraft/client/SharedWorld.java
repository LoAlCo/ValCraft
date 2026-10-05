package dev.valcraft.client;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import net.minecraft.client.Minecraft;
import org.jspecify.annotations.Nullable;

/**
 * Multiplayer: everyone in the same Valheim world plays in one Minecraft world, so they see each
 * other's Minecraft avatars, blocks and mobs. Valheim decides who hosts (Multiplayer.cs: the
 * ValCraft players in a Valheim world agree on one) and says so in ValState.mpMode:
 * <ul>
 * <li>MP_HOST: open our own world to friends (Open to LAN; e4mc gives it a link), and report the
 * link in McState.mpLink for Valheim to hand out;</li>
 * <li>MP_JOIN: play in the friend's world at ValState.mpLink (as /join does);</li>
 * <li>MP_OWN: our own world (leaving a friend's we were put in).</li>
 * </ul>
 */
public final class SharedWorld {
	private static final long RETRY_MS = 20_000;
	private static final int MAX_JOIN_TRIES = 3;

	private static int seenSeq = -1;
	private static int mode = Proto.MP_OWN;
	private static String link = "";
	// The friend's world we went to because Valheim said so (/join and /leave stay the player's own).
	private static @Nullable String autoJoined;
	private static long nextTry;
	private static int joinTries;
	private static boolean weOpened;

	private SharedWorld() {
	}

	/** Every client tick while linked. */
	public static void tick(Minecraft minecraft) {
		var sky = ValClient.sky();
		if (sky.mpSeq != seenSeq) {
			seenSeq = sky.mpSeq;
			if (sky.mpMode != mode || !sky.mpLink.equals(link)) {
				ValCraft.LOG.info("ValCraft: multiplayer: Valheim says {}{}", modeName(sky.mpMode), sky.mpMode == Proto.MP_JOIN ? " " + sky.mpLink : "");
				mode = sky.mpMode;
				link = sky.mpLink;
				joinTries = 0;
				nextTry = 0;
			}
		}
		long now = System.currentTimeMillis();
		String friend = MirrorWorld.friendAddress(minecraft);
		switch (mode) {
			case Proto.MP_HOST -> {
				leaveAutoJoined(minecraft, friend);
				var server = minecraft.getSingleplayerServer();
				if (server != null && minecraft.player != null && !server.isPublished() && now >= nextTry) {
					nextTry = now + RETRY_MS;
					if (System.getenv("VALCRAFT_LAN_OFFLINE") != null) {
						server.setUsesAuthentication(false); // dev: offline test guests (tools/run_guest.ps1)
					}
					boolean ok = server.publishServer(net.minecraft.server.MinecraftServer.MultiplayerScope.LAN, false,
						net.minecraft.util.HttpUtil.getAvailablePort());
					weOpened |= ok;
					ValCraft.LOG.info("ValCraft: multiplayer: opened this world to friends ({})", ok ? "ok" : "FAILED");
				}
			}
			case Proto.MP_JOIN -> {
				if (link.isEmpty() || link.equalsIgnoreCase(friend) || MirrorWorld.joining() || now < nextTry || joinTries >= MAX_JOIN_TRIES) {
					return;
				}
				// Our own world isn't open yet, or we're in it (or another): go to the friend's.
				if (minecraft.player == null && minecraft.level == null && minecraft.gui.screen() != null
					&& !(minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.TitleScreen)) {
					return; // mid-load or connecting: once it settles
				}
				joinTries++;
				nextTry = now + RETRY_MS;
				autoJoined = link;
				ValCraft.LOG.info("ValCraft: multiplayer: joining the shared world at {} (try {})", link, joinTries);
				MirrorWorld.joinFriend(minecraft, link);
			}
			default -> {
				leaveAutoJoined(minecraft, friend);
				var server = minecraft.getSingleplayerServer();
				if (weOpened && server != null && server.isPublished()) {
					weOpened = false;
					server.unpublishServer();
					ValCraft.LOG.info("ValCraft: multiplayer: this world is no longer open to friends");
				}
			}
		}
	}

	private static void leaveAutoJoined(Minecraft minecraft, @Nullable String friend) {
		if (autoJoined != null && autoJoined.equalsIgnoreCase(friend)) {
			ValCraft.LOG.info("ValCraft: multiplayer: leaving the shared world at {}", autoJoined);
			autoJoined = null;
			MirrorWorld.leaveFriend(minecraft);
		}
	}

	/** What Valheim hears back: our world's link while it's open to friends, and whether we're in a friend's. */
	public static void report(Minecraft minecraft, ValLink.McState mc) {
		var server = minecraft.getSingleplayerServer();
		String hostLink = server != null && server.isPublished() ? DiscordPresence.hostLink() : null;
		int state = 0;
		if (hostLink != null) {
			state |= Proto.MP_PUBLISHED;
		}
		String friend = server == null && minecraft.player != null ? MirrorWorld.friendAddress(minecraft) : null;
		if (friend != null) {
			state |= Proto.MP_IN_FRIEND_WORLD;
		}
		mc.mpState = state;
		mc.mpLink = hostLink != null ? hostLink : friend != null ? friend : "";
	}

	private static String modeName(int mode) {
		return switch (mode) {
			case Proto.MP_HOST -> "host";
			case Proto.MP_JOIN -> "join";
			default -> "own world";
		};
	}
}
