package dev.valcraft.item;

import dev.valcraft.ValCraft;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.EnumMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import net.fabricmc.fabric.api.creativetab.v1.CreativeModeTabEvents;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.tags.ItemTags;
import net.minecraft.tags.TagKey;
import net.minecraft.world.item.CreativeModeTabs;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.equipment.ArmorMaterial;
import net.minecraft.world.item.equipment.ArmorType;
import net.minecraft.world.item.equipment.EquipmentAssets;

/**
 * Valheim's armor sets as Minecraft armor, from valcraft/valheim_armor.tsv (tools/armor_sets.py): each
 * set is a Minecraft armor material with its own worn look (equipment/&lt;set&gt;.json), defense
 * and toughness from Valheim's armor, durability from Valheim's, and its helmet, chestplate and
 * leggings (Valheim has no boots: its legs piece covers the feet). Valheim's hits on the player come
 * over as Minecraft damage (ValCombat.hurtPlayer), so this armor protects against them the Minecraft way.
 */
public final class ValheimArmor {
	public static final List<Item> ARMOR = new ArrayList<>();

	private ValheimArmor() {
	}

	private record Row(String id, String prefab, ArmorType type, String set, int defense, float toughness, float knockback, int durability,
		String sound, String repair) {
	}

	public static void init() {
		var in = ValheimArmor.class.getResourceAsStream("/valcraft/valheim_armor.tsv");
		if (in == null) {
			ValCraft.LOG.error("ValCraft: valheim_armor.tsv is missing");
			return;
		}
		Map<String, List<Row>> sets = new LinkedHashMap<>();
		try (BufferedReader reader = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
			for (String line; (line = reader.readLine()) != null; ) {
				if (line.isBlank() || line.startsWith("#")) {
					continue;
				}
				String[] c = line.split("\t", -1);
				ArmorType type = switch (c[2]) {
					case "helmet" -> ArmorType.HELMET;
					case "chestplate" -> ArmorType.CHESTPLATE;
					case "boots" -> ArmorType.BOOTS;
					default -> ArmorType.LEGGINGS;
				};
				sets.computeIfAbsent(c[3], k -> new ArrayList<>()).add(new Row(c[0], c[1], type, c[3], Integer.parseInt(c[4]), Float.parseFloat(c[5]),
					Float.parseFloat(c[6]), Integer.parseInt(c[7]), c[8], c[9]));
			}
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.error("ValCraft: couldn't read valheim_armor.tsv", e);
			return;
		}
		for (var set : sets.entrySet()) {
			List<Row> rows = set.getValue();
			Row first = rows.getFirst();
			Map<ArmorType, Integer> defense = new EnumMap<>(ArmorType.class);
			for (ArmorType t : ArmorType.values()) {
				defense.put(t, 0);
			}
			rows.forEach(r -> defense.put(r.type, r.defense));
			// Minecraft multiplies this by 11 (helmet) to 16 (chestplate): about 60% of Valheim's durability
			int durability = Math.max(5, Math.round(first.durability * 0.6F / 16.0F));
			ArmorMaterial material = new ArmorMaterial(durability, defense, 12, sound(first.sound), first.toughness, first.knockback, repair(first.repair),
				ResourceKey.create(EquipmentAssets.ROOT_ID, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, set.getKey())));
			for (Row r : rows) {
				ResourceKey<Item> key = ResourceKey.create(Registries.ITEM, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, r.id));
				Item.Properties properties = new Item.Properties().setId(key).humanoidArmor(material, r.type);
				ARMOR.add(Registry.register(BuiltInRegistries.ITEM, key, new Item(properties)));
			}
		}
		CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.COMBAT).register(output -> ARMOR.forEach(output::accept));
		ValCraft.LOG.info("ValCraft: {} Valheim armor pieces in {} sets", ARMOR.size(), sets.size());
	}

	private static Holder<SoundEvent> sound(String name) {
		return switch (name) {
			case "iron" -> SoundEvents.ARMOR_EQUIP_IRON;
			case "netherite" -> SoundEvents.ARMOR_EQUIP_NETHERITE;
			case "gold" -> SoundEvents.ARMOR_EQUIP_GOLD;
			default -> SoundEvents.ARMOR_EQUIP_LEATHER;
		};
	}

	private static TagKey<Item> repair(String name) {
		return switch (name) {
			case "iron" -> ItemTags.REPAIRS_IRON_ARMOR;
			case "netherite" -> ItemTags.REPAIRS_NETHERITE_ARMOR;
			case "gold" -> ItemTags.REPAIRS_GOLD_ARMOR;
			default -> ItemTags.REPAIRS_LEATHER_ARMOR;
		};
	}
}
