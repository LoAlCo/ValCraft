package dev.valcraft.client.render;

import dev.valcraft.combat.ValheimActorEntity;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.TamableAnimal;
import net.minecraft.world.entity.animal.golem.AbstractGolem;
import net.minecraft.world.entity.monster.Enemy;

/**
 * Minecraft's mobs near the player, for Valheim (REN_MOBS, every tick): Valheim gives each a stand-in
 * its creatures can see, so its monsters go for Minecraft's monsters, its animals run from them, and
 * tamed wolves defend against them (MobProxies.cs). Their hits come back as IN_MOB_HIT.
 */
public final class MobExporter {
	private static final double RANGE = 48.0;
	private static final int MAX = 48;
	private static final List<Mob> NEAR = new ArrayList<>();

	private MobExporter() {
	}

	/** Every client tick while linked. */
	public static void tick(Minecraft minecraft) {
		var player = minecraft.player;
		if (minecraft.level == null || player == null) {
			return;
		}
		NEAR.clear();
		for (Entity e : minecraft.level.entitiesForRendering()) {
			if (e instanceof Mob mob && !(e instanceof ValheimActorEntity) && mob.isAlive() && e.distanceToSqr(player) < RANGE * RANGE) {
				NEAR.add(mob);
			}
		}
		NEAR.sort(Comparator.comparingDouble(e -> e.distanceToSqr(player)));
		int n = Math.min(MAX, NEAR.size());
		ByteBuffer body = ByteBuffer.allocate(4 + n * (4 * 11 + 48)).order(ByteOrder.LITTLE_ENDIAN).putInt(n);
		for (int i = 0; i < n; i++) {
			Mob mob = NEAR.get(i);
			byte[] name = mob.getDisplayName().getString().getBytes(StandardCharsets.UTF_8);
			if (name.length > 40) {
				name = java.util.Arrays.copyOf(name, 40);
			}
			// Monsters, golems and tamed pets fight in Valheim like the player's own side; the rest are animals.
			boolean fights = mob instanceof Enemy || mob instanceof AbstractGolem || mob instanceof TamableAnimal t && t.isTame();
			body.putInt(mob.getId()).putInt(fights ? 1 : 0)
				.putFloat((float) mob.getX()).putFloat((float) mob.getY()).putFloat((float) mob.getZ()).putFloat(mob.getYRot())
				.putFloat(mob.getBbWidth()).putFloat(mob.getBbHeight()).putFloat(mob.getHealth()).putFloat(mob.getMaxHealth())
				.putInt(name.length).put(name);
		}
		ValLink.tryWriteRender(Proto.REN_MOBS, body.flip(), null);
	}
}
