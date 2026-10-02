package dev.valcraft.item;

import dev.valcraft.ValCraft;
import net.fabricmc.fabric.api.creativetab.v1.CreativeModeTabEvents;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.item.CreativeModeTabs;
import net.minecraft.world.item.Item;

/**
 * ValCraft's own items. The Build Hammer: held in the main hand, the Viking holds Valheim's build
 * hammer, and Valheim's building (its menu, ghost pieces, workbench rules) runs off Minecraft's
 * inventory. It does nothing on its own in Minecraft.
 *
 * Valheim items with no Minecraft counterpart, as Minecraft items (what picking the Valheim one up
 * gives, through the loot table): each boss's offering and drops, offered at altars with the use key.
 */
public final class ValItems {
	public static final ResourceKey<Item> BUILD_HAMMER_KEY =
		ResourceKey.create(Registries.ITEM, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "build_hammer"));
	public static final Item BUILD_HAMMER = Registry.register(BuiltInRegistries.ITEM, BUILD_HAMMER_KEY,
		new Item(new Item.Properties().setId(BUILD_HAMMER_KEY).stacksTo(1)));

	// Valheim's boss offerings and boss drops (see the loot table's boss blocks).
	public static final Item DEER_TROPHY = simple("deer_trophy", 64);
	public static final Item FULING_TOTEM = simple("fuling_totem", 64);
	public static final Item ANCIENT_SEED = simple("ancient_seed", 64);
	public static final Item WITHERED_BONE = simple("withered_bone", 64);
	public static final Item DRAGON_EGG = simple("dragon_egg", 64);
	public static final Item SWAMP_KEY = simple("swamp_key", 64);
	public static final Item BELL = simple("bell", 64);
	public static final Item BELL_FRAGMENT = simple("bell_fragment", 64);
	public static final Item HARD_ANTLER = simple("hard_antler", 64);
	public static final Item WISHBONE = simple("wishbone", 64);
	public static final Item DRAGON_TEAR = simple("dragon_tear", 64);
	public static final Item TORN_SPIRIT = simple("torn_spirit", 64);
	public static final Item MAJESTIC_CARAPACE = simple("majestic_carapace", 64);
	public static final Item SEALBREAKER = simple("sealbreaker", 64);
	public static final Item SEALBREAKER_FRAGMENT = simple("sealbreaker_fragment", 64);
	public static final Item KINDLED_RIBS = simple("kindled_ribs", 64);
	public static final Item EIKTHYR_TROPHY = simple("eikthyr_trophy", 64);
	public static final Item ELDER_TROPHY = simple("elder_trophy", 64);
	public static final Item BONEMASS_TROPHY = simple("bonemass_trophy", 64);
	public static final Item MODER_TROPHY = simple("moder_trophy", 64);
	public static final Item YAGLUTH_TROPHY = simple("yagluth_trophy", 64);
	public static final Item QUEEN_TROPHY = simple("queen_trophy", 64);
	public static final Item FADER_TROPHY = simple("fader_trophy", 64);

	private static Item simple(String name, int stack) {
		ResourceKey<Item> key = ResourceKey.create(Registries.ITEM, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, name));
		return Registry.register(BuiltInRegistries.ITEM, key, new Item(new Item.Properties().setId(key).stacksTo(stack)));
	}

	private ValItems() {
	}

	public static void init() {
		CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.TOOLS_AND_UTILITIES).register(output -> output.accept(BUILD_HAMMER));
		CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.INGREDIENTS).register(output -> {
			output.accept(DEER_TROPHY);
			output.accept(FULING_TOTEM);
			output.accept(ANCIENT_SEED);
			output.accept(WITHERED_BONE);
			output.accept(DRAGON_EGG);
			output.accept(SWAMP_KEY);
			output.accept(BELL);
			output.accept(BELL_FRAGMENT);
			output.accept(HARD_ANTLER);
			output.accept(WISHBONE);
			output.accept(DRAGON_TEAR);
			output.accept(TORN_SPIRIT);
			output.accept(MAJESTIC_CARAPACE);
			output.accept(SEALBREAKER);
			output.accept(SEALBREAKER_FRAGMENT);
			output.accept(KINDLED_RIBS);
			output.accept(EIKTHYR_TROPHY);
			output.accept(ELDER_TROPHY);
			output.accept(BONEMASS_TROPHY);
			output.accept(MODER_TROPHY);
			output.accept(YAGLUTH_TROPHY);
			output.accept(QUEEN_TROPHY);
			output.accept(FADER_TROPHY);
		});
	}
}
