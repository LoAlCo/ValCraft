package dev.valcraft.client;

import dev.valcraft.ValCraft;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPreset;

/** Opens (or creates) the void "mirror" world automatically once Valheim is connected. */
public final class MirrorWorld {
	private static final ResourceKey<WorldPreset> PRESET =
		ResourceKey.create(Registries.WORLD_PRESET, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "mirror"));
	private static boolean attempted;
	private static long lastLog;
	// /join: a friend's world for this session (the e4mc link their "Open to LAN" shows); null: our own.
	private static @org.jspecify.annotations.Nullable String sessionJoin;
	// Shown in chat once the player is in a world again (why they're back in their own, ...).
	private static @org.jspecify.annotations.Nullable String pendingNote;

	// ValCraft: one Minecraft save per Valheim world, named after the world's id. The save we opened.
	private static @org.jspecify.annotations.Nullable String openedWorld;

	private MirrorWorld() {
	}

	/** The save for the Valheim world we're in ("ValCraft-1a2b3c4d"), or null before Valheim is in one. */
	private static @org.jspecify.annotations.Nullable String wantedWorld() {
		var sky = ValClient.sky();
		if (!sky.inGame() || sky.worldId == 0) {
			return null;
		}
		// Interiors (bit 31) live in the same save, shifted away from the surface (see Coords on the Valheim side).
		return ValCraft.WORLD_NAME + "-" + Integer.toHexString(sky.worldId & 0x7FFFFFFF);
	}

	/**
	 * Before ValCraft kept a save per Valheim world there was one, "ValCraft": the first Valheim world
	 * seen while it's still the only one adopts it, builds and all.
	 */
	private static void adoptLegacyWorld(Minecraft minecraft, String name) {
		var source = minecraft.getLevelSource();
		if (source.levelExists(name) || !source.levelExists(ValCraft.WORLD_NAME)) {
			return;
		}
		try {
			java.nio.file.Path saves = source.getBaseDir();
			try (var others = java.nio.file.Files.list(saves)) {
				if (others.anyMatch(p -> p.getFileName().toString().startsWith(ValCraft.WORLD_NAME + "-"))) {
					return;
				}
			}
			java.nio.file.Files.move(saves.resolve(ValCraft.WORLD_NAME), saves.resolve(name));
			ValCraft.LOG.info("ValCraft: the old mirror world is now {}'s", name);
		} catch (java.io.IOException e) {
			ValCraft.LOG.warn("ValCraft: couldn't rename the old mirror world to {}", name, e);
		}
	}

	/** Valheim went to a different world: leave this save (it saves) and open that world's. */
	public static void followValheimWorld(Minecraft minecraft) {
		String wanted = wantedWorld();
		if (wanted == null || openedWorld == null || wanted.equals(openedWorld) || sessionJoin != null || minecraft.level == null) {
			return;
		}
		ValCraft.LOG.info("ValCraft: Valheim is in another world; switching from {} to {}", openedWorld, wanted);
		openedWorld = null;
		leaveWorld(minecraft);
	}

	/**
	 * The address in config/valcraft.properties ({@code join=abc-def.e4mc.link}), if any. Written
	 * with the template below the first time, so there's something to fill in.
	 */
	private static @org.jspecify.annotations.Nullable String joinAddress(Minecraft minecraft) {
		java.nio.file.Path file = minecraft.gameDirectory.toPath().resolve("config").resolve("valcraft.properties");
		java.util.Properties props = new java.util.Properties();
		try {
			if (!java.nio.file.Files.exists(file)) {
				java.nio.file.Files.createDirectories(file.getParent());
				java.nio.file.Files.writeString(file, """
					# ValCraft
					# To play in a friend's world instead of your own: put their address after join=
					# (the link e4mc shows them when they open their world to LAN), then restart Minecraft.
					join=
					""");
			}
			try (var in = java.nio.file.Files.newBufferedReader(file)) {
				props.load(in);
			}
		} catch (java.io.IOException e) {
			ValCraft.LOG.warn("ValCraft: couldn't read {}", file, e);
			return null;
		}
		String join = props.getProperty("join", "").trim();
		return join.isEmpty() ? null : join;
	}

	/** /join: leave this world and play in a friend's (their e4mc link, or any server address). */
	public static void joinFriend(Minecraft minecraft, String link) {
		// People paste all sorts: "https://abc-def.e4mc.link/", " abc-def.e4mc.link ".
		String address = link.trim().replaceFirst("^[A-Za-z]+://", "").replaceAll("/+$", "");
		if (address.isEmpty()) {
			return;
		}
		ValCraft.LOG.info("ValCraft: /join {}", address);
		sessionJoin = address;
		leaveWorld(minecraft);
	}

	/** The friend's world we're in (the address we joined), or null in our own. */
	public static @org.jspecify.annotations.Nullable String friendAddress(Minecraft minecraft) {
		if (sessionJoin != null) {
			return sessionJoin;
		}
		var server = minecraft.isLocalServer() ? null : minecraft.getCurrentServer();
		return server != null ? server.ip : null;
	}

	/** /leave: back to our own world. */
	public static void leaveFriend(Minecraft minecraft) {
		if (sessionJoin == null) {
			minecraft.gui.hud.getChat().addClientSystemMessage(net.minecraft.network.chat.Component.literal("You're already in your own world."));
			return;
		}
		ValCraft.LOG.info("ValCraft: /leave {}", sessionJoin);
		sessionJoin = null;
		pendingNote = "Back in your own world.";
		leaveWorld(minecraft);
	}

	private static void leaveWorld(Minecraft minecraft) {
		attempted = false;
		minecraft.disconnectFromWorld(net.minecraft.client.multiplayer.ClientLevel.DEFAULT_QUIT_MESSAGE);
		minecraft.gui.setScreen(new TitleScreen());  // openWhenReady takes it from the title screen
	}

	/** Every client tick: a note for the player once they're in a world again. */
	public static void tick(Minecraft minecraft) {
		if (pendingNote != null && minecraft.player != null) {
			minecraft.gui.hud.getChat().addClientSystemMessage(net.minecraft.network.chat.Component.literal(pendingNote));
			pendingNote = null;
		}
	}

	public static void openWhenReady(Minecraft minecraft) {
		// Couldn't reach a friend's world, or it closed under us: back to our own, and say why.
		if (minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.DisconnectedScreen && minecraft.level == null) {
			pendingNote = sessionJoin != null
				? "Couldn't stay in " + sessionJoin + " (check the link, and that your friend's world is still open to LAN). You're back in your own world."
				: "Disconnected. You're back in your own world.";
			ValCraft.LOG.info("ValCraft: disconnected; back to the mirror world");
			sessionJoin = null;
			attempted = false;
			minecraft.gui.setScreen(new TitleScreen());
			return;
		}
		if (attempted && minecraft.level == null && minecraft.gui.screen() != null && System.currentTimeMillis() - lastLog > 5000) {
			lastLog = System.currentTimeMillis();
			ValCraft.LOG.info("ValCraft: still not in the mirror world; current screen {}", minecraft.gui.screen().getClass().getName());
		}
		if (attempted || minecraft.level != null || minecraft.gui.overlay() != null) {
			return;
		}
		// Wait for the menu to settle on the title screen; skip any first-launch prompts in front of it.
		if (!(minecraft.gui.screen() instanceof TitleScreen)) {
			if (minecraft.gui.screen() != null && System.currentTimeMillis() - lastLog > 5000) {
				lastLog = System.currentTimeMillis();
				ValCraft.LOG.info("ValCraft: waiting on screen {} before opening the mirror world", minecraft.gui.screen().getClass().getName());
			}
			if (minecraft.gui.screen() == null || minecraft.gui.screen().getClass().getName().contains("Onboarding")) {
				minecraft.gui.setScreen(new TitleScreen());
			}
			return;
		}
		TitleScreen title = (TitleScreen) minecraft.gui.screen();
		attempted = true;
		// Multiplayer: join a friend's world (their e4mc link, or any server address) instead.
		String join = sessionJoin != null ? sessionJoin : joinAddress(minecraft);
		if (join != null) {
			ValCraft.LOG.info("ValCraft: joining {}", join);
			pendingNote = "Joined " + join + ". Type /leave to go back to your own world.";
			net.minecraft.client.gui.screens.ConnectScreen.startConnecting(title, minecraft, net.minecraft.client.multiplayer.resolver.ServerAddress.parseString(join),
				new net.minecraft.client.multiplayer.ServerData("ValCraft", join, net.minecraft.client.multiplayer.ServerData.Type.OTHER), false, null);
			return;
		}
		String world = wantedWorld();
		if (world == null) {
			attempted = false; // Valheim isn't in a world yet: which save to open depends on which one
			return;
		}
		adoptLegacyWorld(minecraft, world);
		openedWorld = world;
		if (minecraft.getLevelSource().levelExists(world)) {
			ValCraft.LOG.info("ValCraft: opening mirror world {}", world);
			minecraft.createWorldOpenFlows().openWorld(world, () -> minecraft.gui.setScreen(title));
			return;
		}
		ValCraft.LOG.info("ValCraft: creating mirror world {}", world);
		LevelSettings settings = new LevelSettings(
			world,
			GameType.SURVIVAL,
			new LevelSettings.DifficultySettings(Difficulty.NORMAL, false, false),
			true,
			WorldDataConfiguration.DEFAULT
		);
		minecraft.createWorldOpenFlows().createFreshLevel(
			world,
			settings,
			new WorldOptions(0L, false, false),
			registries -> registries.lookupOrThrow(Registries.WORLD_PRESET).getOrThrow(PRESET).value().createWorldDimensions(),
			title
		);
	}
}
