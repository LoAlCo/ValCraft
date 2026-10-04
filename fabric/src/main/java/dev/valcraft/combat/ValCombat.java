package dev.valcraft.combat;

import dev.valcraft.ValCraft;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.damagesource.DamageSources;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.MobCategory;
import org.jspecify.annotations.Nullable;

/**
 * Combat between the Minecraft player and Valheim actors, server side.
 *
 * <p>Every Valheim actor near the player gets an invisible {@link ValheimActorEntity} at its exact
 * position. Minecraft weapons hit those like any mob; the resulting damage is sent to Valheim, which
 * applies it to the real actor (scaled by level) and makes it fight back. Valheim's hits on the player
 * come back as Minecraft damage from the attacker's stand-in, so armor, shields, knockback, hurt
 * sounds and death all work the Minecraft way.
 */
public final class ValCombat {
	public static final ResourceKey<EntityType<?>> VALHEIM_ACTOR_KEY =
		ResourceKey.create(Registries.ENTITY_TYPE, Identifier.fromNamespaceAndPath(ValCraft.MOD_ID, "valheim_actor"));
	public static final EntityType<ValheimActorEntity> VALHEIM_ACTOR = Registry.register(
		BuiltInRegistries.ENTITY_TYPE,
		VALHEIM_ACTOR_KEY,
		EntityType.Builder.<ValheimActorEntity>of(ValheimActorEntity::new, MobCategory.MISC)
			.sized(0.6F, 1.8F)
			.noSave()
			.noSummon()
			.noLootTable()
			.clientTrackingRange(10)
			.updateInterval(1)
			.build(VALHEIM_ACTOR_KEY)
	);

	/** Valheim damage is divided by this for Minecraft (a 15-damage bandit swing = 3 = 1.5 hearts). */
	public static final float VALHEIM_TO_MC_DAMAGE = 5.0F;

	private static final Map<Integer, ValheimActorEntity> PROXIES = new HashMap<>();
	private static final List<ValLink.Actor> ACTORS = new ArrayList<>();

	private ValCombat() {
	}

	public static void init() {
		FabricDefaultAttributeRegistry.register(VALHEIM_ACTOR, LivingEntity.createLivingAttributes());
		ServerTickEvents.END_SERVER_TICK.register(ValCombat::serverTick);
	}

	public static @Nullable ValheimActorEntity proxy(int formId) {
		return PROXIES.get(formId);
	}

	private static void serverTick(MinecraftServer server) {
		List<ServerPlayer> players = server.getPlayerList().getPlayers();
		if (!ValLink.active() || players.isEmpty()) {
			removeAll();
			return;
		}
		ServerLevel level = players.getFirst().level();
		for (ServerPlayer player : players) {
			pickUpNearby(player);
		}
		if (ValLink.readActors(ACTORS)) {
			sync(level);
		}
		// Hits land during the tick (melee, sweeps, arrows, fire); send one combined hit per actor.
		for (ValheimActorEntity proxy : PROXIES.values()) {
			float[] hit = proxy.takeHit();
			if (hit != null && (hit[0] > 0.0F || hit[3] > 0.0F)) {
				int valheimWeapon = Float.floatToRawIntBits(hit[6]);
				if (valheimWeapon != 0) {
					ValLink.pushEvent(Proto.EV_HIT_WEAPON, valheimWeapon, hit[7], 0, 0, 0, 0);
				}
				ValLink.pushEvent(
					Proto.EV_HIT_ACTOR, proxy.formId(), hit[0], hit[1], hit[2], hit[3], Float.floatToRawIntBits(hit[4]), Float.floatToRawIntBits(hit[5])
				);
				ValCraft.LOG.info("ValCraft: hit {} for {} (knockback {})", proxy.getName().getString(), hit[0], hit[3]);
			}
		}
	}

	private static void sync(ServerLevel level) {
		Map<Integer, ValLink.Actor> live = new HashMap<>();
		for (ValLink.Actor a : ACTORS) {
			if (!a.dead()) {
				live.put(a.formId(), a);
			}
		}
		for (Iterator<Map.Entry<Integer, ValheimActorEntity>> it = PROXIES.entrySet().iterator(); it.hasNext(); ) {
			Map.Entry<Integer, ValheimActorEntity> e = it.next();
			ValheimActorEntity proxy = e.getValue();
			if (!live.containsKey(e.getKey()) || proxy.isRemoved() || proxy.level() != level) {
				proxy.discard();
				it.remove();
			}
		}
		int before = PROXIES.size();
		for (ValLink.Actor a : live.values()) {
			ValheimActorEntity proxy = PROXIES.get(a.formId());
			if (proxy == null) {
				proxy = new ValheimActorEntity(VALHEIM_ACTOR, level);
				proxy.setFormId(a.formId());
				proxy.setSize(a.width(), a.height());
				proxy.setHostile(a.hostile());
				proxy.snapTo(a.x(), a.y(), a.z(), a.yaw(), 0.0F);
				if (!a.name().isEmpty()) {
					proxy.setCustomName(Component.literal(a.name()));
				}
				if (!level.addFreshEntity(proxy)) {
					continue;
				}
				PROXIES.put(a.formId(), proxy);
				continue;
			}
			proxy.setSize(a.width(), a.height());
			proxy.setHostile(a.hostile());
			proxy.setPos(a.x(), a.y(), a.z());
			proxy.setYRot(a.yaw());
			proxy.setYHeadRot(a.yaw());
			stepOnTriggers(level, proxy);
		}
		if (PROXIES.size() != before && (PROXIES.size() % 5 == 0 || PROXIES.size() < 5)) {
			ValCraft.LOG.info("ValCraft: {} Valheim actors mirrored as hittable stand-ins", PROXIES.size());
		}
	}

	/**
	 * Valheim's NPCs press pressure plates and trip tripwires. Their stand-ins are placed, not moved
	 * (no physics), so Minecraft never checks what they step into; do it for those blocks here.
	 */
	private static void stepOnTriggers(ServerLevel level, ValheimActorEntity proxy) {
		var box = proxy.getBoundingBox().deflate(1.0E-5);
		var from = net.minecraft.core.BlockPos.containing(box.minX, box.minY, box.minZ);
		var to = net.minecraft.core.BlockPos.containing(box.maxX, box.maxY, box.maxZ);
		for (var pos : net.minecraft.core.BlockPos.betweenClosed(from, to)) {
			var state = level.getBlockState(pos);
			if (state.getBlock() instanceof net.minecraft.world.level.block.BasePressurePlateBlock
				|| state.getBlock() instanceof net.minecraft.world.level.block.TripWireBlock) {
				state.entityInside(level, pos, proxy, net.minecraft.world.entity.InsideBlockEffectApplier.NOOP, true);
			}
		}
	}

	/**
	 * Items and stuck arrows on Valheim ground rest on its collision voxels, which on steep or rough
	 * terrain can sit a little off from where the player (on Valheim's exact triangles) stands.
	 * Touch them over a slightly bigger area than vanilla's so walking over them picks them up.
	 * playerTouch applies all of Minecraft's own rules (pickup delay, owner, inventory space).
	 */
	private static void pickUpNearby(ServerPlayer player) {
		if (!player.isAlive() || player.isSpectator()) {
			return;
		}
		for (Entity entity : player.level().getEntities(player, player.getBoundingBox().inflate(1.25, 1.0, 1.25))) {
			if (!entity.isRemoved() && (entity instanceof net.minecraft.world.entity.item.ItemEntity
				|| entity instanceof net.minecraft.world.entity.projectile.arrow.AbstractArrow)) {
				entity.playerTouch(player);
			}
		}
	}

	private static void removeAll() {
		if (PROXIES.isEmpty()) {
			return;
		}
		PROXIES.values().forEach(Entity::discard);
		PROXIES.clear();
	}

	/**
	 * Valheim hit the player. Runs on the server thread. {@code kind} is a Proto.HURT_* value and
	 * {@code valheimDamage} is what Valheim would have taken off the player's health.
	 */
	public static void hurtPlayer(ServerPlayer player, int kind, float valheimDamage, int attackerFormId, int flags) {
		if (!player.isAlive() || valheimDamage <= 0.0F) {
			return;
		}
		ServerLevel level = player.level();
		ValheimActorEntity attacker = PROXIES.get(attackerFormId);
		if (attacker != null && attacker.distanceToSqr(player) > 24.0 * 24.0) {
			attacker = null; // a guest's own NPC with the same form id as one of the host's
		}
		DamageSources sources = level.damageSources();
		DamageSource source = switch (kind) {
			case Proto.HURT_MELEE -> attacker != null ? sources.mobAttack(attacker) : sources.generic();
			case Proto.HURT_PROJECTILE -> attacker != null ? sources.mobProjectile(attacker, attacker) : sources.generic();
			case Proto.HURT_MAGIC -> attacker != null ? sources.indirectMagic(attacker, attacker) : sources.magic();
			default -> sources.generic();
		};
		float damage = valheimDamage / VALHEIM_TO_MC_DAMAGE;
		float healthBefore = player.getHealth();
		boolean blocking = player.isBlocking();
		boolean hurt = player.hurtServer(level, source, damage);
		trainDefence(player, damage, blocking && player.getHealth() >= healthBefore - 1.0E-3F);
		ValCraft.LOG.info("ValCraft: Valheim hit the player for {} ({} Minecraft): health {} -> {}{}", valheimDamage, damage, healthBefore, player.getHealth(),
			hurt ? "" : " (blocked/immune)");
		if (hurt && attacker != null && (flags & Proto.HURT_POWER_ATTACK) != 0 && !player.isBlocking()) {
			// Power attacks shove harder, like a sprint hit does in Minecraft.
			player.knockback(0.5, attacker.getX() - player.getX(), attacker.getZ() - player.getZ(), source, damage);
		}
	}

	/**
	 * Valheim skills for taking a hit: Block when the shield caught it, otherwise Light or Heavy
	 * Armor by what the player mostly wears (leather, chainmail, gold, copper and turtle count as
	 * light; iron, diamond and netherite as heavy). Only the host's own Valheim is told.
	 */
	private static void trainDefence(ServerPlayer player, float damage, boolean blocked) {
		if (!dev.valcraft.net.ValNet.isHost(player) || damage <= 0.0F) {
			return;
		}
		if (blocked) {
			ValLink.pushEvent(Proto.EV_SKILL_USE, Proto.SKILL_BLOCK, damage, 0.0F, 0.0F, 0.0F, 0);
			return;
		}
		int light = 0, heavy = 0;
		for (var slot : new net.minecraft.world.entity.EquipmentSlot[] { net.minecraft.world.entity.EquipmentSlot.HEAD, net.minecraft.world.entity.EquipmentSlot.CHEST,
			net.minecraft.world.entity.EquipmentSlot.LEGS, net.minecraft.world.entity.EquipmentSlot.FEET }) {
			var stack = player.getItemBySlot(slot);
			if (stack.isEmpty()) {
				continue;
			}
			String path = net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath();
			if (path.startsWith("iron_") || path.startsWith("diamond_") || path.startsWith("netherite_")) {
				heavy++;
			} else {
				light++;
			}
		}
		if (light + heavy > 0) {
			ValLink.pushEvent(Proto.EV_SKILL_USE, heavy > light ? Proto.SKILL_HEAVY_ARMOR : Proto.SKILL_LIGHT_ARMOR, damage * (light + heavy) / 4.0F, 0.0F, 0.0F,
				0.0F, 0);
		}
	}

	/** Form id of the Valheim actor behind a damage source, or 0. */
	public static int attackerFormId(DamageSource source) {
		return source.getEntity() instanceof ValheimActorEntity proxy ? proxy.formId() : 0;
	}
}
