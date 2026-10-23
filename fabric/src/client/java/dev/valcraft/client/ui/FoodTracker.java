package dev.valcraft.client.ui;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.core.component.DataComponents;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;

/**
 * The food bar's segments: Minecraft's food (hunger plus saturation) split by the food it came from,
 * like Valheim's food slots. Eating adds a segment the size of what that food gave; going hungry
 * uses up the oldest one first, so each segment is how much of that food is left. Up to three, as
 * in Valheim: a fourth folds the oldest two together.
 */
public final class FoodTracker {
	public static final class Segment {
		public final ItemStack food;
		public float left, size;

		Segment(ItemStack food, float amount) {
			this.food = food;
			this.left = amount;
			this.size = amount;
		}
	}

	public static final int MAX = 3;
	private static final List<Segment> SEGMENTS = new ArrayList<>();
	private static float lastTotal = -1;
	private static ItemStack eating = ItemStack.EMPTY;
	private static int eatingTicks;

	private FoodTracker() {
	}

	// [Valheim Features] FoodAndHealth: the Minecraft food last eaten as each Valheim food, for its icon
	private static final java.util.Map<Integer, ItemStack> EATEN = new java.util.HashMap<>();

	/** The Minecraft food eaten as this Valheim food (by its hash), or empty. */
	public static ItemStack eatenAs(int valheimHash) {
		return EATEN.getOrDefault(valheimHash, ItemStack.EMPTY);
	}

	public static List<Segment> segments() {
		return SEGMENTS;
	}

	/** What the bar is measured against: the foods' full sizes, at least a full Minecraft hunger bar. */
	public static float scale() {
		float sum = 0;
		for (Segment s : SEGMENTS) {
			sum += s.size;
		}
		return Math.max(20f, sum);
	}

	/** Every client tick. */
	public static void tick(Minecraft minecraft) {
		Player player = minecraft.player;
		if (player == null) {
			SEGMENTS.clear();
			lastTotal = -1;
			return;
		}
		ItemStack using = player.isUsingItem() ? player.getUseItem() : ItemStack.EMPTY;
		if (!using.isEmpty() && using.has(DataComponents.FOOD)) {
			eating = using.copyWithCount(1);
			EATEN.put(dev.valcraft.combat.ValheimVitals.valheimFoodHash(eating), eating);
			eatingTicks = 0;
		} else if (eatingTicks++ > 10) {
			eating = ItemStack.EMPTY;
		}
		var food = player.getFoodData();
		float total = food.getFoodLevel() + food.getSaturationLevel();
		if (lastTotal < 0) {
			lastTotal = total;
			if (total > 0) {
				SEGMENTS.add(new Segment(ItemStack.EMPTY, total));
			}
			return;
		}
		float change = total - lastTotal;
		lastTotal = total;
		if (change > 0.01f) {
			if (!eating.isEmpty() || SEGMENTS.isEmpty()) {
				SEGMENTS.add(new Segment(eating, change));
				eating = ItemStack.EMPTY;
				if (SEGMENTS.size() > MAX) {
					Segment oldest = SEGMENTS.remove(0), next = SEGMENTS.get(0);
					next.left += oldest.left;
					next.size += oldest.size;
				}
			} else {
				Segment newest = SEGMENTS.get(SEGMENTS.size() - 1);  // cake, or food from elsewhere
				newest.left += change;
				newest.size = Math.max(newest.size, newest.left);
			}
		} else if (change < -0.01f) {
			float use = -change;
			while (use > 0 && !SEGMENTS.isEmpty()) {
				Segment oldest = SEGMENTS.get(0);
				float take = Math.min(use, oldest.left);
				oldest.left -= take;
				use -= take;
				if (oldest.left <= 0.01f) {
					SEGMENTS.remove(0);
				}
			}
		}
	}
}
