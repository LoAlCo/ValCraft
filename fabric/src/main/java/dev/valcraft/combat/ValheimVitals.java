package dev.valcraft.combat;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.net.ValNet;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.ai.attributes.AttributeModifier;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;

/**
 * [Valheim Features] FoodAndHealth (VitalsSync.cs): Valheim's food and health for Minecraft's. Each
 * player's own Valheim is in charge: eating a Minecraft food is its Valheim's eating the matching
 * Valheim food (EV_EAT), under Valheim's rules (three foods, not the same one again before half its
 * time is gone); Valheim's max health (base plus the foods, tapering off as they run out) becomes the
 * player's max health; Valheim's healing comes over as IN_HEAL. Minecraft's hunger stays full and
 * neither drains nor heals (FoodDataMixin). Valheim health is Minecraft health x 5, as Valheim's hits are.
 */
public final class ValheimVitals {
	/** Valheim health per Minecraft health (the same as for Valheim's hits on the player). */
	public static final float VALHEIM_PER_MC = ValCombat.VALHEIM_TO_MC_DAMAGE;

	/** One player's Valheim: whether the feature is on, its max health and its foods. */
	public record State(boolean on, float maxHealth, int[] hash, float[] time, float[] burn, float[] health) {
		public int count() {
			return this.hash.length;
		}
	}

	public static final State OFF = new State(false, 0, new int[0], new float[0], new float[0], new float[0]);
	private static volatile State local = OFF;
	private static final Map<UUID, State> GUESTS = new ConcurrentHashMap<>();
	private static final Identifier MODIFIER = Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "valheim_food");

	private ValheimVitals() {
	}

	/** This machine's Valheim (the client sets it every tick from the link). */
	public static void setLocal(State state) {
		local = state;
	}

	public static State local() {
		return local;
	}

	/** A guest's Valheim, sent by their client (ValNet.Vitals). */
	public static void setGuest(UUID player, State state) {
		GUESTS.put(player, state);
	}

	public static State of(Player player) {
		if (player.level().isClientSide() || !(player instanceof ServerPlayer sp) || ValNet.isHost(sp)) {
			return local;
		}
		return GUESTS.getOrDefault(player.getUUID(), OFF);
	}

	public static boolean active(Player player) {
		return of(player).on();
	}

	// ---- which Valheim food a Minecraft food is ------------------------------------------------

	// Minecraft food -> the Valheim food it counts as (its health, stamina, time and regen). The
	// loot bridge's pairs first (sweet berries are Valheim's raspberries), then the closest in kind and
	// strength; different foods get different Valheim foods where they can, so they can be eaten together.
	private static final String[][] TABLE = {
		{ "sweet_berries", "Raspberry" }, { "apple", "Blueberries" }, { "glow_berries", "Cloudberry" }, { "melon_slice", "Raspberry" },
		{ "carrot", "Carrot" }, { "potato", "Onion" }, { "beetroot", "Mushroom" }, { "dried_kelp", "MushroomYellow" },
		{ "chorus_fruit", "MushroomBlue" }, { "honey_bottle", "Honey" }, { "cookie", "QueensJam" },
		{ "bread", "Bread" }, { "baked_potato", "OnionSoup" }, { "pumpkin_pie", "Salad" }, { "mushroom_stew", "TurnipStew" },
		{ "beetroot_soup", "CarrotSoup" }, { "rabbit_stew", "DeerStew" }, { "suspicious_stew", "Eyescream" },
		{ "golden_carrot", "VikingCupcake" }, { "golden_apple", "MeatPlatter" }, { "enchanted_golden_apple", "MashedMeat" },
		{ "porkchop", "RawMeat" }, { "beef", "DeerMeat" }, { "mutton", "LoxMeat" }, { "chicken", "FishAnglerRaw" }, { "rabbit", "RawMeat" },
		{ "cod", "FishRaw" }, { "salmon", "FishRaw" }, { "tropical_fish", "FishAnglerRaw" }, { "pufferfish", "FishAnglerRaw" },
		{ "rotten_flesh", "MushroomYellow" }, { "spider_eye", "MushroomYellow" }, { "poisonous_potato", "Onion" },
		{ "cooked_porkchop", "CookedMeat" }, { "cooked_beef", "CookedLoxMeat" }, { "cooked_mutton", "CookedWolfMeat" },
		{ "cooked_chicken", "CookedDeerMeat" }, { "cooked_rabbit", "CookedBjornMeat" }, { "cooked_cod", "FishCooked" },
		{ "cooked_salmon", "FishAndBread" },
	};
	private static Map<Item, String> foods;

	/** The Valheim food (prefab name) this Minecraft food counts as. */
	public static String valheimFood(ItemStack stack) {
		if (foods == null) {
			Map<Item, String> m = new HashMap<>();
			for (String[] row : TABLE) {
				BuiltInRegistries.ITEM.getOptional(Identifier.withDefaultNamespace(row[0])).ifPresent(item -> m.put(item, row[1]));
			}
			foods = m;
		}
		String name = foods.get(stack.getItem());
		if (name != null) {
			return name;
		}
		// anything else (other mods' foods): by how filling it is
		var food = stack.get(DataComponents.FOOD);
		int n = food != null ? food.nutrition() : 2;
		return n <= 2 ? "Raspberry" : n <= 4 ? "Carrot" : n <= 6 ? "Bread" : "CookedMeat";
	}

	public static int valheimFoodHash(ItemStack stack) {
		return valheimFood(stack).hashCode();
	}

	/** Valheim's Player.CanEat: 0 yes, 1 not that one again yet, 2 full. */
	public static int canEat(Player player, ItemStack stack) {
		State s = of(player);
		int hash = valheimFoodHash(stack);
		for (int i = 0; i < s.count(); i++) {
			if (s.hash()[i] == hash) {
				return s.time()[i] < s.burn()[i] / 2 ? 0 : 1;
			}
		}
		for (int i = 0; i < s.count(); i++) {
			if (s.time()[i] < s.burn()[i] / 2) {
				return 0;
			}
		}
		return s.count() >= 3 ? 2 : 0;
	}

	/** Server thread: the player ate (FoodPropertiesMixin): their Valheim eats the Valheim food. */
	public static void ate(ServerPlayer player, ItemStack stack) {
		ValNet.pushEvent(player, Proto.EV_EAT, valheimFoodHash(stack), 0, 0, 0, 0, 0);
	}

	/** Valheim healed the player (Valheim health). */
	public static void heal(ServerPlayer player, float valheimHealth) {
		if (active(player) && player.isAlive()) {
			player.heal(valheimHealth / VALHEIM_PER_MC);
		}
	}

	// ---- max health ------------------------------------------------------------------------------

	/** Server thread, every tick: each player's max health follows their Valheim's. */
	public static void tick(MinecraftServer server) {
		for (ServerPlayer player : server.getPlayerList().getPlayers()) {
			var attribute = player.getAttribute(Attributes.MAX_HEALTH);
			if (attribute == null) {
				continue;
			}
			State s = of(player);
			if (!s.on()) {
				if (attribute.getModifier(MODIFIER) != null) {
					attribute.removeModifier(MODIFIER);
				}
				continue;
			}
			if (s.maxHealth() <= 0) {
				continue;  // Valheim hasn't said yet
			}
			double add = Math.max(1.0, s.maxHealth() / VALHEIM_PER_MC) - attribute.getBaseValue();
			var current = attribute.getModifier(MODIFIER);
			if (current == null || Math.abs(current.amount() - add) > 0.01) {
				attribute.addOrReplacePermanentModifier(new AttributeModifier(MODIFIER, add, AttributeModifier.Operation.ADD_VALUE));
			}
			if (player.getHealth() > player.getMaxHealth()) {
				player.setHealth(player.getMaxHealth());
			}
		}
	}
}
