package dev.valcraft.client.ui;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.blaze3d.platform.NativeImage;
import dev.valcraft.ValCraft;
import dev.valcraft.client.ValClient;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValheimInfo;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

/**
 * ValCraft's Valheim-style UI ([UI] Style in Valheim's config): Minecraft's hearts and hunger become a
 * health bar and a food bar (ValheimHud), and the inventory gets tabs for Valheim's skills, trophies,
 * compendium and Valheim-only items (InventoryTabs, ValheimTabScreen). What those tabs show comes from
 * Valheim (ValheimInfo); its icons, pixel art made from Valheim's own, go into one small texture.
 */
public final class ValheimUi {
	public record Skill(String icon, String name, String desc, int level, int bonus, float progress) {
	}

	public record Trophy(String icon, String name, String lore) {
	}

	public record Text(String title, String text) {
	}

	public record Effect(String icon, String name) {
	}

	/** A food Valheim's player has eaten (its timer comes with ValState every frame). */
	public record Food(String icon, String name, float health, float regen) {
	}

	public record Item(String icon, String name, String desc, int x, int y, int count, int quality, float weight, boolean equipped, boolean eat) {
	}

	public static List<Skill> skills = List.of();
	public static List<Trophy> trophies = List.of();
	public static List<Text> texts = List.of();
	public static List<Effect> effects = List.of();
	public static List<Item> items = List.of();
	public static Map<Integer, Food> foods = Map.of();
	public static int skillTotal, skillCap, packW = 8, packH = 4, weight, maxWeight;

	private static final Identifier ATLAS = Identifier.fromNamespaceAndPath("valcraft", "dynamic/valheim_icons");
	private static final int ATLAS_SIZE = 256, PER_ROW = ATLAS_SIZE / 16;
	private static final Map<String, Integer> SLOTS = new HashMap<>();
	private static DynamicTexture atlas;
	private static int parsedSeq;

	private ValheimUi() {
	}

	/** Valheim's [UI] Style is Valheim (and Valheim is there to say so). */
	public static boolean active() {
		return ValClient.linked() && (ValClient.sky().flags & Proto.VAL_UI_VALHEIM) != 0;
	}

	/** Client thread: take in Valheim's latest message, if there's a new one. */
	public static void update() {
		int seq = ValheimInfo.seq();
		byte[] message = ValheimInfo.latest();
		if (seq == parsedSeq || message == null) {
			return;
		}
		parsedSeq = seq;
		try {
			read(message);
		} catch (RuntimeException e) {
			ValCraft.LOG.warn("ValCraft: Valheim's inventory info: {}", e.toString());
		}
	}

	private static void read(byte[] message) {
		ByteBuffer b = ByteBuffer.wrap(message).order(ByteOrder.LITTLE_ENDIAN);
		byte[] json = new byte[b.getInt()];
		b.get(json);
		JsonObject root = JsonParser.parseString(new String(json, StandardCharsets.UTF_8)).getAsJsonObject();
		skillTotal = root.get("skillTotal").getAsInt();
		skillCap = root.get("skillCap").getAsInt();
		packW = Math.max(1, root.get("packW").getAsInt());
		packH = Math.max(1, root.get("packH").getAsInt());
		weight = root.get("weight").getAsInt();
		maxWeight = root.get("maxWeight").getAsInt();
		List<Skill> s = new ArrayList<>();
		for (JsonObject o : objects(root, "skills")) {
			s.add(new Skill(str(o, "icon"), str(o, "name"), str(o, "desc"), o.get("level").getAsInt(), Math.round(o.get("bonus").getAsFloat()),
				o.get("progress").getAsFloat()));
		}
		List<Trophy> t = new ArrayList<>();
		for (JsonObject o : objects(root, "trophies")) {
			t.add(new Trophy(str(o, "icon"), str(o, "name"), str(o, "lore")));
		}
		List<Text> x = new ArrayList<>();
		for (JsonObject o : objects(root, "texts")) {
			x.add(new Text(str(o, "title"), str(o, "text")));
		}
		List<Effect> e = new ArrayList<>();
		for (JsonObject o : objects(root, "effects")) {
			e.add(new Effect(str(o, "icon"), str(o, "name")));
		}
		List<Item> it = new ArrayList<>();
		for (JsonObject o : objects(root, "items")) {
			it.add(new Item(str(o, "icon"), str(o, "name"), str(o, "desc"), o.get("x").getAsInt(), o.get("y").getAsInt(), o.get("count").getAsInt(),
				o.get("quality").getAsInt(), o.get("weight").getAsFloat(), o.get("equipped").getAsBoolean(),
				o.has("eat") && o.get("eat").getAsBoolean()));
		}
		Map<Integer, Food> f = new HashMap<>();
		for (JsonObject o : objects(root, "foods")) {
			f.put(o.get("hash").getAsInt(), new Food(str(o, "icon"), str(o, "name"), o.get("health").getAsFloat(), o.get("regen").getAsFloat()));
		}
		foods = f;
		skills = s;
		trophies = t;
		texts = x;
		effects = e;
		items = it;

		int icons = b.getInt();
		if (icons > 0) {
			ensureAtlas();
			NativeImage pixels = atlas.getPixels();
			for (int i = 0; i < icons; i++) {
				byte[] key = new byte[Short.toUnsignedInt(b.getShort())];
				b.get(key);
				int slot = slot(new String(key, StandardCharsets.UTF_8));
				int ox = slot % PER_ROW * 16, oy = slot / PER_ROW * 16;
				for (int p = 0; p < 256; p++) {
					int r = b.get() & 0xFF, g = b.get() & 0xFF, bl = b.get() & 0xFF, a = b.get() & 0xFF;
					pixels.setPixel(ox + p % 16, oy + p / 16, a << 24 | r << 16 | g << 8 | bl);
				}
			}
			atlas.upload();
		}
	}

	private static List<JsonObject> objects(JsonObject root, String name) {
		List<JsonObject> out = new ArrayList<>();
		JsonArray array = root.has(name) ? root.getAsJsonArray(name) : new JsonArray();
		for (JsonElement el : array) {
			out.add(el.getAsJsonObject());
		}
		return out;
	}

	private static String str(JsonObject o, String name) {
		return o.has(name) ? o.get(name).getAsString() : "";
	}

	private static void ensureAtlas() {
		if (atlas == null) {
			atlas = new DynamicTexture(() -> "ValCraft Valheim icons", ATLAS_SIZE, ATLAS_SIZE, true);
			Minecraft.getInstance().getTextureManager().register(ATLAS, atlas);
		}
	}

	private static int slot(String key) {
		Integer slot = SLOTS.get(key);
		if (slot == null) {
			if (SLOTS.size() >= PER_ROW * PER_ROW) {
				SLOTS.clear();  // full (hundreds of different icons): start over; Valheim sends them all again soon
			}
			slot = SLOTS.size();
			SLOTS.put(key, slot);
		}
		return slot;
	}

	/** A Valheim icon at 16 x 16; false if Valheim hasn't sent it (yet). */
	public static boolean icon(GuiGraphicsExtractor g, String key, int x, int y) {
		Integer slot = SLOTS.get(key);
		if (slot == null || atlas == null) {
			return false;
		}
		g.blit(RenderPipelines.GUI_TEXTURED, ATLAS, x, y, slot % PER_ROW * 16, slot / PER_ROW * 16, 16, 16, ATLAS_SIZE, ATLAS_SIZE);
		return true;
	}
}
