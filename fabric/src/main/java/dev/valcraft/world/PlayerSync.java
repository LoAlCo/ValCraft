package dev.valcraft.world;

import dev.valcraft.ValCraft;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.ProblemReporter;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.level.storage.TagValueOutput;
import org.jspecify.annotations.Nullable;

/**
 * Each Valheim world has two Minecraft saves: ValCraft-&lt;id&gt; (Valheim terrain) and
 * ValCraft-&lt;id&gt;-blocks (block terrain, F8), and each save keeps its own player. So the
 * inventory follows across F8, the player (inventory, ender chest, XP, health, food, recipes, game
 * mode, flying) is kept in one file both saves share, ValCraft-&lt;id&gt;.player.dat next to them:
 * written on leaving, every 30 s and on shutdown, and loaded on joining. Where the player stands
 * and respawns stays each save's own. Only the host's own player in a ValCraft save; guests are
 * left alone.
 */
public final class PlayerSync {
	/** The player data that follows across the two saves (the rest, e.g. position, stays per save). */
	private static final String[] SHARED = {
		"Inventory", "equipment", "EnderItems", "SelectedItemSlot",
		"XpLevel", "XpP", "XpTotal", "XpSeed", "Score",
		"Health", "AbsorptionAmount", "foodLevel", "foodSaturationLevel", "foodExhaustionLevel", "foodTickTimer",
		"recipeBook", "Tags", "abilities",
	};
	private static final int SAVE_EVERY_TICKS = 30 * 20;
	private static int ticks;

	private PlayerSync() {
	}

	public static void init() {
		ServerPlayConnectionEvents.DISCONNECT.register((handler, server) -> save(server, handler.getPlayer()));
		ServerLifecycleEvents.SERVER_STOPPING.register(server -> server.getPlayerList().getPlayers().forEach(p -> save(server, p)));
		ServerTickEvents.END_SERVER_TICK.register(server -> {
			if (++ticks >= SAVE_EVERY_TICKS) {
				ticks = 0;
				server.getPlayerList().getPlayers().forEach(p -> save(server, p));
			}
		});
	}

	/** The shared file for this save's pair, or null when this isn't a ValCraft save. */
	private static @Nullable Path file(MinecraftServer server) {
		Path root = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
		String name = root.getFileName().toString();
		if (!name.startsWith(ValCraft.WORLD_NAME)) {
			return null;
		}
		if (name.endsWith("-blocks")) {
			name = name.substring(0, name.length() - "-blocks".length());
		}
		return root.getParent().resolve(name + ".player.dat");
	}

	private static boolean isHost(MinecraftServer server, ServerPlayer player) {
		return !server.isDedicatedServer() && server.isSingleplayerOwner(player.nameAndId());
	}

	private static CompoundTag full(ServerPlayer player) {
		TagValueOutput out = TagValueOutput.createWithContext(ProblemReporter.DISCARDING, player.registryAccess());
		player.saveWithoutId(out);
		return out.buildResult();
	}

	private static void save(MinecraftServer server, ServerPlayer player) {
		Path path = file(server);
		if (path == null || !isHost(server, player) || player.isDeadOrDying()) {
			return;
		}
		try {
			CompoundTag all = full(player);
			CompoundTag shared = new CompoundTag();
			for (String key : SHARED) {
				if (all.contains(key)) {
					shared.put(key, all.get(key).copy());
				}
			}
			if (all.contains("playerGameType")) {
				shared.put("playerGameType", all.get("playerGameType").copy());
			}
			Path tmp = path.resolveSibling(path.getFileName() + ".tmp");
			NbtIo.writeCompressed(shared, tmp);
			Files.move(tmp, path, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: couldn't save the shared player data to {}", path, e);
		}
	}

	/** Joining: the shared player replaces this save's (its position stays). */
	public static void join(MinecraftServer server, ServerPlayer player) {
		Path path = file(server);
		if (path == null || !isHost(server, player) || !Files.exists(path)) {
			return;
		}
		try {
			CompoundTag shared = NbtIo.readCompressed(path, NbtAccounter.unlimitedHeap());
			CompoundTag merged = full(player);
			for (String key : SHARED) {
				if (shared.contains(key)) {
					merged.put(key, shared.get(key).copy());
				}
			}
			player.load(TagValueInput.create(ProblemReporter.DISCARDING, player.registryAccess(), merged));
			int mode = shared.getIntOr("playerGameType", -1);
			if (mode >= 0) {
				player.setGameMode(GameType.byId(mode));
			}
			// The client got this save's player when it joined: send it the shared one.
			player.onUpdateAbilities();
			player.resetSentInfo();
			server.getPlayerList().sendAllPlayerInfo(player);
			ValCraft.LOG.info("ValCraft: player data shared with the other terrain mode loaded from {}", path.getFileName());
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: couldn't load the shared player data from {}", path, e);
		}
	}
}
