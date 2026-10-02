package dev.valcraft.client.render;

import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.TreeMap;
import net.minecraft.client.Minecraft;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.item.ItemStack;

/**
 * What the player carries (REN_INVENTORY), for Valheim's building: the hammer and hoe pay their
 * costs out of Minecraft's inventory. Sent whenever it changes. Render thread only.
 */
public final class InventoryExporter {
	private static final Map<String, Integer> COUNTS = new TreeMap<>();
	private static int sentHash;
	private static boolean sent;

	private InventoryExporter() {
	}

	/** Valheim lost what we sent (Minecraft reconnected, a world change): send again. */
	public static void reset() {
		sent = false;
	}

	public static void frame(Minecraft minecraft) {
		if (minecraft.player == null) {
			return;
		}
		COUNTS.clear();
		for (ItemStack stack : minecraft.player.getInventory().getNonEquipmentItems()) {
			if (!stack.isEmpty()) {
				COUNTS.merge(BuiltInRegistries.ITEM.getKey(stack.getItem()).toString(), stack.getCount(), Integer::sum);
			}
		}
		int hash = COUNTS.hashCode();
		if (sent && hash == sentHash) {
			return;
		}
		int bytes = 4;
		for (String id : COUNTS.keySet()) {
			bytes += 8 + id.getBytes(StandardCharsets.UTF_8).length;
		}
		// entries: int count, int id length, id bytes (UTF-8)
		ByteBuffer body = ByteBuffer.allocate(bytes).order(ByteOrder.LITTLE_ENDIAN).putInt(COUNTS.size());
		for (var e : COUNTS.entrySet()) {
			byte[] id = e.getKey().getBytes(StandardCharsets.UTF_8);
			body.putInt(e.getValue()).putInt(id.length).put(id);
		}
		if (ValLink.tryWriteRender(Proto.REN_INVENTORY, body.flip(), null)) {
			sent = true;
			sentHash = hash;
		}
	}
}
