package dev.valcraft.world;

import dev.valcraft.ValCraft;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.List;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.ProblemReporter;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.level.storage.TagValueOutput;
import org.jspecify.annotations.Nullable;

/**
 * Items lying on the ground follow the player across F8: the two saves of a Valheim world (Valheim
 * terrain and block terrain, see PlayerSync) are the same place, so what was dropped in one is
 * still lying there in the other. When a save closes, its loose items are taken out of it into
 * ValCraft-&lt;id&gt;.items.dat next to the saves; whichever of the two opens next puts them back
 * where they lay, a little above, to settle on that save's ground. Builds already follow (BuildSync).
 */
public final class ItemCarry {
	private static final int MAX_ITEMS = 2048;

	private ItemCarry() {
	}

	public static void init() {
		ServerLifecycleEvents.SERVER_STOPPING.register(ItemCarry::takeOut);
		ServerLifecycleEvents.SERVER_STARTED.register(ItemCarry::putBack);
	}

	private static @Nullable Path file(MinecraftServer server) {
		Path root = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
		String name = root.getFileName().toString();
		if (!name.startsWith(ValCraft.WORLD_NAME)) {
			return null;
		}
		if (name.endsWith("-blocks")) {
			name = name.substring(0, name.length() - "-blocks".length());
		}
		return root.getParent().resolve(name + ".items.dat");
	}

	/** The save is closing (F8, leaving, quitting): its loose items go into the shared file instead. */
	private static void takeOut(MinecraftServer server) {
		Path path = file(server);
		if (path == null || server.isDedicatedServer()) {
			return;
		}
		ServerLevel level = server.overworld();
		List<ItemEntity> items = new ArrayList<>();
		for (var e : level.getAllEntities()) {
			if (e instanceof ItemEntity item && !item.isRemoved() && !item.getItem().isEmpty() && items.size() < MAX_ITEMS) {
				items.add(item);
			}
		}
		if (items.isEmpty()) {
			return;
		}
		try {
			TagValueOutput out = TagValueOutput.createWithContext(ProblemReporter.DISCARDING, level.registryAccess());
			var list = out.childrenList("Items");
			for (ItemEntity item : items) {
				var c = list.addChild();
				c.store("Item", ItemStack.CODEC, item.getItem());
				c.putDouble("X", item.getX());
				c.putDouble("Y", item.getY());
				c.putDouble("Z", item.getZ());
				c.putInt("Age", item.getAge());
			}
			Path tmp = path.resolveSibling(path.getFileName() + ".tmp");
			NbtIo.writeCompressed(out.buildResult(), tmp);
			Files.move(tmp, path, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
			// Written: now they leave this save (it saves its chunks after this), so they're never in both.
			items.forEach(ItemEntity::discard);
			ValCraft.LOG.info("ValCraft: {} items on the ground kept for the other terrain mode ({})", items.size(), path.getFileName());
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: couldn't keep the items on the ground for the other terrain mode", e);
		}
	}

	/** A save opened: the items kept when the last one closed lie where they were. */
	private static void putBack(MinecraftServer server) {
		Path path = file(server);
		if (path == null || server.isDedicatedServer() || !Files.exists(path)) {
			return;
		}
		ServerLevel level = server.overworld();
		int placed = 0;
		try {
			var tag = NbtIo.readCompressed(path, NbtAccounter.unlimitedHeap());
			var in = TagValueInput.create(ProblemReporter.DISCARDING, level.registryAccess(), tag);
			for (var c : in.childrenListOrEmpty("Items")) {
				var stack = c.read("Item", ItemStack.CODEC);
				if (stack.isEmpty() || stack.get().isEmpty()) {
					continue;
				}
				ItemEntity item = new ItemEntity(level, c.getDoubleOr("X", 0.0), c.getDoubleOr("Y", 0.0) + 0.5, c.getDoubleOr("Z", 0.0), stack.get(), 0.0, 0.0, 0.0);
				item.setPickUpDelay(10);
				if (level.addFreshEntity(item)) {
					placed++;
				}
			}
			Files.delete(path);
			ValCraft.LOG.info("ValCraft: {} items on the ground brought over from the other terrain mode", placed);
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: couldn't bring over the items on the ground from {}", path, e);
		}
	}
}
