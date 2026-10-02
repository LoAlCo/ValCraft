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
 * gives, through the loot table): the deer trophy and the fuling totem, offered at Eikthyr's and
 * Yagluth's altars with the use key.
 */
public final class ValItems {
	public static final ResourceKey<Item> BUILD_HAMMER_KEY =
		ResourceKey.create(Registries.ITEM, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "build_hammer"));
	public static final Item BUILD_HAMMER = Registry.register(BuiltInRegistries.ITEM, BUILD_HAMMER_KEY,
		new Item(new Item.Properties().setId(BUILD_HAMMER_KEY).stacksTo(1)));

	public static final Item DEER_TROPHY = simple("deer_trophy", 64);
	public static final Item FULING_TOTEM = simple("fuling_totem", 64);

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
		});
	}
}
