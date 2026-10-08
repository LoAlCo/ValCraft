package dev.valcraft.world;

import dev.valcraft.combat.ValheimActorEntity;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.block.BaseFireBlock;
import net.minecraft.world.phys.AABB;

/**
 * Valheim's fire into Minecraft: a Valheim fire (a burning house, tree or field, Ashlands cinders,
 * or one Minecraft's own fire started, see FireBridge.cs) sets Minecraft's flammable blocks beside it
 * alight, and Minecraft's mobs standing in it. Players burn the Valheim way already (their Viking
 * takes the fire's damage, which comes over as a Minecraft hit).
 */
public final class FireBridge {
	private static final double REACH = 1.25;
	private static final int MOB_BURN_SECONDS = 4;

	private FireBridge() {
	}

	/** Server thread. (x, y, z): the Valheim fire, Minecraft coordinates. */
	public static void valheimFire(ServerLevel level, double x, double y, double z) {
		var box = new AABB(x - REACH, y - 0.5, z - REACH, x + REACH, y + 2.0, z + REACH);
		for (LivingEntity e : level.getEntitiesOfClass(LivingEntity.class, box)) {
			if (!(e instanceof Player) && !(e instanceof ValheimActorEntity) && !e.fireImmune()) {
				e.igniteForSeconds(MOB_BURN_SECONDS);
			}
		}
		// The air beside flammable blocks around the fire catches, as Minecraft's own fire spreads.
		BlockPos centre = BlockPos.containing(x, y, z);
		for (BlockPos pos : BlockPos.betweenClosed(centre.offset(-1, -1, -1), centre.offset(1, 1, 1))) {
			if (!level.getBlockState(pos).isAir() || !besideFlammable(level, pos)) {
				continue;
			}
			var fire = BaseFireBlock.getState(level, pos);
			if (fire.canSurvive(level, pos)) {
				level.setBlock(pos, fire, 11);
				return; // one at a time: Minecraft's fire spreads it from there
			}
		}
	}

	private static boolean besideFlammable(ServerLevel level, BlockPos pos) {
		for (Direction d : Direction.values()) {
			if (level.getBlockState(pos.relative(d)).ignitedByLava()) {
				return true;
			}
		}
		return false;
	}
}
