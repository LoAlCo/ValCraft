package dev.valcraft.client;

import dev.valcraft.client.mixin.BossHealthOverlayAccessor;
import dev.valcraft.client.mixin.HudAccessor;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.LerpingBossEvent;
import net.minecraft.network.chat.contents.TranslatableContents;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.boss.enderdragon.EnderDragon;
import net.minecraft.world.entity.boss.wither.WitherBoss;
import net.minecraft.world.entity.monster.ElderGuardian;
import net.minecraft.world.entity.monster.warden.Warden;

/**
 * Minecraft's bosses get Valheim's boss health bar (Valheim's BossBars.cs draws them): the Wither,
 * the Ender Dragon, the Warden and Elder Guardians near the player, at their exact health, and
 * raids (what's left of the raid, as Minecraft's own raid bar shows it). Minecraft's own boss bars
 * are hidden while linked (HudBossBarMixin), so there's one bar per boss.
 */
public final class BossBars {
	/** Valheim shows its own bosses' bars out to 100 m. */
	private static final double BOSS_RANGE = 100.0;
	/** The Warden and Elder Guardians are mini-bosses: a bar once you're in the fight. */
	private static final double MINI_BOSS_RANGE = 40.0;

	private static final List<ValLink.Boss> BOSSES = new ArrayList<>();
	private static final List<LivingEntity> NEAR = new ArrayList<>();
	private static int lastCount = -1;

	private BossBars() {
	}

	/** Every client tick while linked. */
	public static void tick(Minecraft minecraft) {
		BOSSES.clear();
		var player = minecraft.player;
		if (minecraft.level != null && player != null) {
			NEAR.clear();
			for (Entity e : minecraft.level.entitiesForRendering()) {
				if (!(e instanceof LivingEntity living) || !living.isAlive() || kindOf(e) == Proto.BOSS_OTHER) {
					continue;
				}
				double range = e instanceof WitherBoss || e instanceof EnderDragon ? BOSS_RANGE : MINI_BOSS_RANGE;
				if (e.distanceToSqr(player) <= range * range) {
					NEAR.add(living);
				}
			}
			NEAR.sort(Comparator.comparingDouble(e -> e.distanceToSqr(player)));
			for (LivingEntity e : NEAR) {
				float max = e.getMaxHealth();
				BOSSES.add(new ValLink.Boss(e.getUUID().hashCode(), max > 0 ? Math.clamp(e.getHealth() / max, 0.0F, 1.0F) : 0.0F, kindOf(e),
					e.getDisplayName().getString()));
			}
			// Raids (and any /bossbar): Minecraft's own boss bars. Wither and dragon bars are the entities above.
			var overlay = ((HudAccessor) minecraft.gui.hud).valcraft$bossOverlay();
			for (LerpingBossEvent event : ((BossHealthOverlayAccessor) overlay).valcraft$events().values()) {
				String key = event.getName().getContents() instanceof TranslatableContents t ? t.getKey() : "";
				if (key.equals("entity.minecraft.wither") || key.equals("entity.minecraft.ender_dragon")) {
					continue;
				}
				BOSSES.add(new ValLink.Boss(event.getId().hashCode(), Math.clamp(event.getProgress(), 0.0F, 1.0F),
					key.startsWith("event.minecraft.raid") ? Proto.BOSS_RAID : Proto.BOSS_OTHER, event.getName().getString()));
			}
		}
		if (BOSSES.size() != lastCount) {
			lastCount = BOSSES.size();
			if (!BOSSES.isEmpty()) {
				dev.valcraft.ValCraft.LOG.info("ValCraft: boss bars: {}", BOSSES.stream().map(ValLink.Boss::name).toList());
			}
		}
		ValLink.writeBosses(BOSSES);
	}

	private static int kindOf(Entity e) {
		if (e instanceof WitherBoss) {
			return Proto.BOSS_WITHER;
		}
		if (e instanceof EnderDragon) {
			return Proto.BOSS_DRAGON;
		}
		if (e instanceof Warden) {
			return Proto.BOSS_WARDEN;
		}
		if (e instanceof ElderGuardian) {
			return Proto.BOSS_ELDER_GUARDIAN;
		}
		return Proto.BOSS_OTHER;
	}
}
