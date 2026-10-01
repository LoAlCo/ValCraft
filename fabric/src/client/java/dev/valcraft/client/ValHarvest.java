package dev.valcraft.client;

import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import dev.valcraft.world.ValClip;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.tags.ItemTags;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.phys.Vec3;

/**
 * Swinging at Valheim's world (trees, logs, rocks, ore, the ground, its buildings) with a Minecraft
 * tool: every full-strength swing becomes one Valheim hit (EV_VALHEIM_HIT), the way chopping and
 * mining work in Valheim. Valheim decides what it does: axes fell trees, pickaxes break rocks and
 * dig, and a tool's tier decides what it can get through.
 */
final class ValHarvest {
	private static boolean wasDown;
	private static long lastHitMs;
	/** Even a fully charged fast tool lands at most this often (Valheim's own axes swing about once a second). */
	private static final long MIN_INTERVAL_MS = 700;

	private ValHarvest() {
	}

	/** Client tick. */
	static void tick(Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (player == null || minecraft.gui.screen() != null || !ValLink.active()) {
			wasDown = false;
			return;
		}
		boolean down = minecraft.options.keyAttack.isDown();
		boolean fresh = down && !wasDown;
		wasDown = down;
		if (!down || !(minecraft.hitResult instanceof ValClip.ValheimHitResult hit)) {
			return;
		}
		float strength = player.getAttackStrengthScale(0.5F);
		// A click always swings; holding the button swings again whenever the tool has recharged.
		long now = System.currentTimeMillis();
		if ((!fresh && strength < 1.0F) || now - lastHitMs < MIN_INTERVAL_MS) {
			return;
		}
		lastHitMs = now;
		ItemStack held = player.getMainHandItem();
		player.swingAndResetAttackStrength(InteractionHand.MAIN_HAND, held.getAttackAnimation(), true);
		player.resetAttackStrengthTicker();
		Vec3 at = hit.getLocation();
		ValLink.pushEvent(Proto.EV_VALHEIM_HIT, toolInfo(held), (float) at.x, (float) at.y, (float) at.z, strength, 0);
	}

	/** Proto.TOOL_* kind in bits 0-3, tier (0 wood/gold .. 4 netherite) in bits 4-7. */
	static int toolInfo(ItemStack stack) {
		if (stack.isEmpty()) {
			return Proto.TOOL_NONE;
		}
		int kind = stack.is(ItemTags.AXES) ? Proto.TOOL_AXE
			: stack.is(ItemTags.PICKAXES) ? Proto.TOOL_PICKAXE
			: stack.is(ItemTags.SHOVELS) ? Proto.TOOL_SHOVEL
			: stack.is(ItemTags.HOES) ? Proto.TOOL_HOE
			: stack.is(ItemTags.SWORDS) ? Proto.TOOL_SWORD
			: Proto.TOOL_NONE;
		String path = BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath();
		int tier = path.startsWith("netherite_") ? 4 : path.startsWith("diamond_") ? 3 : path.startsWith("iron_") ? 2
			: path.startsWith("copper_") ? 2 : path.startsWith("stone_") ? 1 : 0;
		return kind | tier << 4;
	}
}
