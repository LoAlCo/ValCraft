package dev.valcraft.item;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import net.fabricmc.fabric.api.creativetab.v1.CreativeModeTabEvents;
import net.minecraft.core.Registry;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.CreativeModeTabs;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.alchemy.PotionContents;
import net.minecraft.world.item.component.Consumables;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.item.ToolMaterial;
import net.minecraft.tags.BlockTags;
import net.minecraft.tags.ItemTags;
import net.minecraft.tags.TagKey;

/**
 * ValCraft's Minecraft versions of Valheim items that have no Minecraft counterpart (ores, seeds,
 * meads, ...), registered from valcraft/valheim_items.tsv (made by tools/gen_valheim_items.py from
 * tools/valheim_items.tsv). Picking the Valheim item up gives this one (the loot table). A mead
 * drinks like a potion with its Minecraft effects, and the Viking gets the Valheim mead's own effect
 * (EV_CONSUME). A weapon or tool works like Minecraft's of its kind, with its own damage, speed
 * and durability; its hits on Valheim's creatures do the Valheim weapon's own damage (ValCombat).
 */
public final class ValheimItems {
	private static final List<Item> ITEMS = new ArrayList<>();
	private static final List<Item> DRINKS = new ArrayList<>();
	private static final List<Item> WEAPONS = new ArrayList<>();

	private ValheimItems() {
	}

	/** A Valheim weapon or tool: what it hits in Valheim takes the Valheim weapon's damage. */
	public static final class Weapon extends Item {
		public final int prefabHash;
		/** Its Minecraft attack damage: Valheim scales its own damage by the hit's share of it. */
		public final float damage;

		Weapon(Properties properties, String prefab, float damage) {
			super(properties);
			this.prefabHash = prefab.hashCode();
			this.damage = damage;
		}
	}

	private static ToolMaterial material(int tier, int durability) {
		TagKey<Block> incorrect = switch (Math.min(tier, 3)) {
			case 0 -> BlockTags.INCORRECT_FOR_STONE_TOOL;
			case 1 -> BlockTags.INCORRECT_FOR_IRON_TOOL;
			case 2 -> BlockTags.INCORRECT_FOR_DIAMOND_TOOL;
			default -> BlockTags.INCORRECT_FOR_NETHERITE_TOOL;
		};
		float speed = 4.0F + 1.5F * Math.min(tier, 4);
		return new ToolMaterial(incorrect, durability, speed, 0.0F, 14, ItemTags.IRON_TOOL_MATERIALS);
	}

	/** Drinkable: the Valheim side applies the mead's own status effect to the Viking. */
	private static final class Mead extends Item {
		private final String prefab;
		private final boolean curePoison;

		Mead(Properties properties, String prefab, boolean curePoison) {
			super(properties);
			this.prefab = prefab;
			this.curePoison = curePoison;
		}

		@Override
		public ItemStack finishUsingItem(ItemStack stack, Level level, LivingEntity entity) {
			if (!level.isClientSide() && entity instanceof ServerPlayer) {
				if (this.curePoison) {
					entity.removeEffect(MobEffects.POISON);
					entity.removeEffect(MobEffects.WITHER);
				}
				if (ValLink.active()) {
					ValLink.pushEvent(Proto.EV_CONSUME, this.prefab.hashCode(), 0, 0, 0, 0, 0);
				}
			}
			return super.finishUsingItem(stack, level, entity);
		}
	}

	public static void init() {
		var in = ValheimItems.class.getResourceAsStream("/valcraft/valheim_items.tsv");
		if (in == null) {
			ValCraft.LOG.error("ValCraft: valheim_items.tsv is missing");
			return;
		}
		try (BufferedReader reader = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
			for (String line; (line = reader.readLine()) != null; ) {
				if (line.isBlank() || line.startsWith("#")) {
					continue;
				}
				String[] cells = line.split("\t", -1);
				register(cells[0], cells[1], cells[2], Integer.parseInt(cells[3]), cells.length > 4 ? cells[4] : "");
			}
		} catch (IOException | RuntimeException e) {
			ValCraft.LOG.error("ValCraft: couldn't read valheim_items.tsv", e);
		}
		CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.INGREDIENTS).register(output -> ITEMS.forEach(output::accept));
		CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.FOOD_AND_DRINKS).register(output -> DRINKS.forEach(output::accept));
		CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.COMBAT).register(output -> WEAPONS.forEach(output::accept));
		ValCraft.LOG.info("ValCraft: {} Valheim items", ITEMS.size() + DRINKS.size() + WEAPONS.size());
	}

	private static void register(String id, String prefab, String kind, int stack, String effects) {
		ResourceKey<Item> key = ResourceKey.create(Registries.ITEM, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, id));
		Item.Properties properties = new Item.Properties().setId(key).stacksTo(stack);
		if (kind.equals("mead")) {
			boolean cure = false;
			List<MobEffectInstance> list = new ArrayList<>();
			for (String part : effects.split(";")) {
				part = part.trim();
				if (part.isEmpty()) {
					continue;
				}
				if (part.equals("cure:poison")) {
					cure = true;
					continue;
				}
				String[] e = part.split(":");
				var effect = BuiltInRegistries.MOB_EFFECT.get(Identifier.withDefaultNamespace(e[0]));
				if (effect.isEmpty()) {
					ValCraft.LOG.warn("ValCraft: {}: no effect {}", id, e[0]);
					continue;
				}
				list.add(new MobEffectInstance(effect.get(), Math.max(1, Integer.parseInt(e[2]) * 20), Integer.parseInt(e[1])));
			}
			properties.component(DataComponents.CONSUMABLE, Consumables.DEFAULT_DRINK)
				.component(DataComponents.POTION_CONTENTS, new PotionContents(Optional.empty(), Optional.empty(), list, Optional.empty()));
			DRINKS.add(Registry.register(BuiltInRegistries.ITEM, key, new Mead(properties, prefab, cure)));
		} else if (kind.equals("weapon")) {
			java.util.Map<String, String> args = new java.util.HashMap<>();
			for (String part : effects.trim().split(" +")) {
				int eq = part.indexOf('=');
				if (eq > 0) {
					args.put(part.substring(0, eq), part.substring(eq + 1));
				}
			}
			int tier = Integer.parseInt(args.getOrDefault("tier", "0"));
			int durability = Integer.parseInt(args.getOrDefault("durability", "400"));
			float damage = Float.parseFloat(args.getOrDefault("damage", "1"));
			float speed = Float.parseFloat(args.getOrDefault("speed", "1.6"));
			ToolMaterial material = material(tier, durability);
			// Minecraft adds these to the player's own 1 damage and 4 attacks a second.
			float dmg = damage - 1.0F, spd = speed - 4.0F;
			switch (args.getOrDefault("class", "sword")) {
				case "axe", "battleaxe" -> properties.axe(material, dmg, spd);
				case "pickaxe" -> properties.pickaxe(material, dmg, spd);
				case "shovel" -> properties.shovel(material, dmg, spd);
				default -> properties.sword(material, dmg, spd);
			}
			properties.durability(durability);
			WEAPONS.add(Registry.register(BuiltInRegistries.ITEM, key, new Weapon(properties, prefab, damage)));
		} else {
			ITEMS.add(Registry.register(BuiltInRegistries.ITEM, key, new Item(properties)));
		}
	}
}
