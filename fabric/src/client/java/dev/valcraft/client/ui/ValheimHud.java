package dev.valcraft.client.ui;

import dev.valcraft.client.mixin.HudAccessor;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import net.minecraft.util.Mth;
import net.minecraft.util.Util;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.player.Player;
import dev.valcraft.combat.ValheimVitals;

/**
 * Valheim-style health and food in Minecraft's own colours, where Minecraft draws its hearts and
 * hunger: a health bar instead of the row of hearts and a food bar instead of the shanks. Neither
 * grows: more health or more food makes the marks on the bar closer together, not the bar longer.
 * Armor and air are Minecraft's, above them as usual.
 */
public final class ValheimHud {
	private static final int W = 81, H = 9;

	// Minecraft's heart colours (normal, poisoned, withered, frozen, absorption), each as a light top
	// edge, the fill and a dark bottom edge
	private static final int[] RED = { 0xFFFF6E6E, 0xFFDB1414, 0xFF8E0A0A };
	private static final int[] POISON = { 0xFFC4D660, 0xFF8FA51C, 0xFF5B6B0E };
	private static final int[] WITHER = { 0xFF6E6E6E, 0xFF3A3A3A, 0xFF1E1E1E };
	private static final int[] FROZEN = { 0xFFD8F1FF, 0xFF8CC8EA, 0xFF5590B5 };
	private static final int[] GOLD = { 0xFFFFF29A, 0xFFF0C419, 0xFFA97F05 };
	// Minecraft's shank colours, a shade per segment; greenish while hungry (the Hunger effect)
	private static final int[][] FOOD = { { 0xFFF2B36C, 0xFFD08337, 0xFF8C5220 }, { 0xFFE29A55, 0xFFB86A26, 0xFF7A4316 },
		{ 0xFFF7C988, 0xFFDD9A4C, 0xFF9C6428 } };
	private static final int[][] HUNGRY = { { 0xFFBFD47A, 0xFF8BA33C, 0xFF566A1E }, { 0xFFA9C366, 0xFF738E2C, 0xFF465D16 },
		{ 0xFFCFE08F, 0xFF9DB54E, 0xFF62782A } };
	private static final int OUTLINE = 0xFF000000, EMPTY = 0xFF2B2B2B, EMPTY_DARK = 0xFF1C1C1C, MARK = 0x55000000;

	private static float shownHealth = -1, trail;
	private static long trailHold;

	private ValheimHud() {
	}

	/** In place of Hud.extractPlayerHealth. */
	public static void draw(GuiGraphicsExtractor g, Hud hud) {
		HudAccessor h = (HudAccessor) hud;
		Player player = h.valcraft$cameraPlayer();
		if (player == null) {
			return;
		}
		int left = g.guiWidth() / 2 - 91, right = g.guiWidth() / 2 + 91, y = g.guiHeight() - 39;
		HudAccessor.valcraft$extractArmor(g, player, y, 1, 10, left);
		health(g, player, left, y);
		int vehicleHearts = h.valcraft$vehicleMaxHearts(h.valcraft$vehicleWithHealth());
		if (vehicleHearts == 0) {
			food(g, player, right - W, y);
		}
		h.valcraft$extractAirBubbles(g, player, vehicleHearts, y - 10, right);
	}

	private static void frame(GuiGraphicsExtractor g, int x, int y) {
		g.fill(x, y, x + W, y + H, OUTLINE);
		g.fill(x + 1, y + 1, x + W - 1, y + H - 1, EMPTY);
		g.fill(x + 1, y + H - 2, x + W - 1, y + H - 1, EMPTY_DARK);
	}

	/** One stretch of bar, inside the frame, with Minecraft's light top and dark bottom. */
	private static void fillPart(GuiGraphicsExtractor g, int x0, int x1, int y, int[] c) {
		if (x1 <= x0) {
			return;
		}
		g.fill(x0, y + 1, x1, y + H - 1, c[1]);
		g.fill(x0, y + 1, x1, y + 2, c[0]);
		g.fill(x0, y + H - 2, x1, y + H - 1, c[2]);
	}

	/** Marks across the bar every `step` of `scale`: closer together the more there is. */
	private static void marks(GuiGraphicsExtractor g, int x, int y, float scale, float step, int upTo) {
		int inner = W - 2;
		while (inner * step / scale < 3) {
			step *= 5;
		}
		for (float v = step; v < scale - 0.01f; v += step) {
			int mx = x + 1 + Math.round(inner * v / scale);
			if (mx >= upTo) {
				break;
			}
			g.fill(mx, y + 2, mx + 1, y + H - 2, MARK);
		}
	}

	private static void health(GuiGraphicsExtractor g, Player player, int x, int y) {
		float health = Math.max(0, player.getHealth()), max = Math.max(1, player.getMaxHealth()), absorb = player.getAbsorptionAmount();
		long now = Util.getMillis();
		// what was just lost shows a moment longer, then drains away, like Valheim's
		if (shownHealth < 0 || health > shownHealth) {
			trail = health;
		} else if (health < shownHealth) {
			if (trail <= shownHealth) {
				trail = shownHealth;
			}
			trailHold = now + 400;
		}
		shownHealth = health;
		if (now > trailHold) {
			trail = Math.max(health, trail - (max * 0.6f) * 0.016f);
		}
		float scale = max + absorb;
		int inner = W - 2;
		int[] colour = player.isFullyFrozen() ? FROZEN : player.hasEffect(MobEffects.WITHER) ? WITHER : player.hasEffect(MobEffects.POISON) ? POISON : RED;
		frame(g, x, y);
		int healthEnd = x + 1 + Math.round(inner * health / scale);
		int trailEnd = x + 1 + Math.round(inner * Math.min(trail, max) / scale);
		if (trailEnd > healthEnd) {
			g.fill(healthEnd, y + 1, trailEnd, y + H - 1, 0xC0FFFFFF);
		}
		// regenerating: the fill brightens and dims, as Minecraft's hearts bob
		if (player.hasEffect(MobEffects.REGENERATION) && (now / 250) % 2 == 0) {
			colour = new int[] { colour[0], colour[0], colour[1] };
		}
		fillPart(g, x + 1, healthEnd, y, colour);
		if (absorb > 0) {
			int absorbStart = x + 1 + Math.round(inner * max / scale);
			fillPart(g, absorbStart, x + 1 + Math.round(inner * (max + absorb) / scale), y, GOLD);
		}
		marks(g, x, y, scale, 2f, x + W - 1);
		// Valheim's food and health: Valheim's numbers (Minecraft's x 5)
		float k = ValheimVitals.active(player) ? ValheimVitals.VALHEIM_PER_MC : 1f;
		number(g, Mth.ceil(health * k) + "/" + Mth.ceil(max * k), x, y);
	}

	private static void food(GuiGraphicsExtractor g, Player player, int x, int y) {
		if (ValheimVitals.active(player)) {
			valheimFood(g, player, x, y);
			return;
		}
		var segments = FoodTracker.segments();
		float scale = FoodTracker.scale();
		int inner = W - 2;
		int[][] colours = player.hasEffect(MobEffects.HUNGER) ? HUNGRY : FOOD;
		frame(g, x, y);
		float at = 0;
		for (int i = 0; i < segments.size(); i++) {
			var s = segments.get(i);
			int x0 = x + 1 + Math.round(inner * at / scale);
			at += s.left;
			int x1 = x + 1 + Math.round(inner * at / scale);
			fillPart(g, x0, x1, y, colours[i % colours.length]);
			if (i > 0 && x1 > x0) {
				g.fill(x0, y + 1, x0 + 1, y + H - 1, 0x80000000);  // where one food ends and the next starts
			}
			// the food's own icon at half size, where there's room
			if (!s.food.isEmpty() && x1 - x0 >= 10) {
				var pose = g.pose();
				pose.pushMatrix();
				pose.translate(x0 + 1, y + 0.5f);
				pose.scale(0.5f, 0.5f);
				g.fakeItem(s.food, 0, 0);
				pose.popMatrix();
			}
		}
	}

	/**
	 * Valheim's foods: a segment per food, as long as the health it gives when fresh (measured against
	 * at least a good meal's worth, so one berry doesn't fill the bar), filled for the time it has left.
	 */
	private static void valheimFood(GuiGraphicsExtractor g, Player player, int x, int y) {
		var s = ValheimVitals.local();
		int inner = W - 2, n = s.count();
		float[] full = new float[n];
		float sum = 0;
		for (int i = 0; i < n; i++) {
			var info = ValheimUi.foods.get(s.hash()[i]);
			full[i] = Math.max(1f, info != null ? info.health() : s.health()[i]);
			sum += full[i];
		}
		float scale = Math.max(90f, sum);
		frame(g, x, y);
		float at = 0;
		for (int i = 0; i < n; i++) {
			int x0 = x + 1 + Math.round(inner * at / scale);
			at += full[i];
			int x1 = x + 1 + Math.round(inner * at / scale);
			float left = s.burn()[i] > 0 ? Math.clamp(s.time()[i] / s.burn()[i], 0f, 1f) : 0f;
			int xf = x0 + Math.round((x1 - x0) * left);
			int[] c = FOOD[i % FOOD.length];
			fillPart(g, x0, xf, y, c);
			if (xf < x1) {
				g.fill(xf, y + 1, x1, y + H - 1, 0x40FFFFFF & c[1] | 0x40000000);  // what's been used up, faint
			}
			if (i > 0) {
				g.fill(x0, y + 1, x0 + 1, y + H - 1, 0xC0000000);
			}
			// the food: the Minecraft food it was eaten as, else Valheim's icon
			var eaten = FoodTracker.eatenAs(s.hash()[i]);
			var info = ValheimUi.foods.get(s.hash()[i]);
			if (x1 - x0 >= 10) {
				var pose = g.pose();
				pose.pushMatrix();
				pose.translate(x0 + 1, y + 0.5f);
				pose.scale(0.5f, 0.5f);
				if (!eaten.isEmpty()) {
					g.fakeItem(eaten, 0, 0);
				} else if (info != null) {
					ValheimUi.icon(g, info.icon(), 0, 0);
				}
				pose.popMatrix();
			}
			// minutes (then seconds) left, where there's room
			if (x1 - x0 >= 26) {
				float secs = s.time()[i];
				String t = secs >= 60 ? (int) Math.ceil(secs / 60) + "m" : Math.max(0, (int) secs) + "s";
				smallText(g, t, x1 - 2, y, true);
			}
		}
	}

	/** Small text in a bar, right-aligned at x if `right`. */
	private static void smallText(GuiGraphicsExtractor g, String text, int x, int y, boolean right) {
		Font font = Minecraft.getInstance().font;
		float s = 0.5f;
		var pose = g.pose();
		pose.pushMatrix();
		pose.translate(right ? x - font.width(text) * s : x, y + (H - 7 * s) / 2f);
		pose.scale(s, s);
		g.text(font, text, 0, 0, 0xFFFFFFFF, true);
		pose.popMatrix();
	}

	/** Small numbers in the middle of a bar. */
	private static void number(GuiGraphicsExtractor g, String text, int x, int y) {
		Font font = Minecraft.getInstance().font;
		float s = 0.75f;
		var pose = g.pose();
		pose.pushMatrix();
		pose.translate(x + (W - font.width(text) * s) / 2f, y + (H - 7 * s) / 2f);
		pose.scale(s, s);
		g.text(font, text, 0, 0, 0xFFFFFFFF, true);
		pose.popMatrix();
	}
}
