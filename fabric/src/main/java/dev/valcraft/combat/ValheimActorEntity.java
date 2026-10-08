package dev.valcraft.combat;

import dev.valcraft.link.Proto;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.network.syncher.EntityDataSerializers;
import net.minecraft.network.syncher.SynchedEntityData;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityDimensions;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.HumanoidArm;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Pose;
import net.minecraft.tags.ItemTags;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.Level;
import org.jspecify.annotations.Nullable;

/**
 * An invisible stand-in for one Valheim actor, so Minecraft's own combat (swords, crits, sweeps,
 * enchantments, attack cooldown, bows, tridents) can target and hit Valheim NPCs. What it receives is
 * collected into one hit per tick and forwarded to the real actor; its own health never drops.
 */
public class ValheimActorEntity extends LivingEntity {
	private static final EntityDataAccessor<Integer> FORM_ID = SynchedEntityData.defineId(ValheimActorEntity.class, EntityDataSerializers.INT);
	private static final EntityDataAccessor<Float> WIDTH = SynchedEntityData.defineId(ValheimActorEntity.class, EntityDataSerializers.FLOAT);
	private static final EntityDataAccessor<Float> HEIGHT = SynchedEntityData.defineId(ValheimActorEntity.class, EntityDataSerializers.FLOAT);

	// This tick's hit, flushed to Valheim by ValCombat after all attacks for the tick have landed
	// (Player.attack adds its sprint/enchantment knockback after hurtServer returns).
	private float pendingDamage;
	private int pendingFlags;
	private int pendingWeapon;
	/** A ValCraft Valheim weapon's prefab hash and Minecraft damage, or 0: Valheim uses its own damage. */
	private int pendingValheimWeapon;
	private float pendingValheimBase;
	/** A Minecraft tool's tier, kind and full damage (toolTier), or 0. */
	private int pendingTier;
	private double pushX, pushZ;
	private float pushStrength;
	private boolean hitThisTick;
	/** The player whose hit this tick is (their own Valheim applies it), or null for mobs. */
	private java.util.@Nullable UUID pendingAttacker;
	private static final int TORCH_BURN_TICKS = 30;
	// What Valheim was last told about this one burning (ValCombat.tellBurning): when, and for how long.
	private int burnToldAt = -1000, burnToldTicks;

	/**
	 * True when Valheim should hear that this creature is burning: it just caught fire, or the fire
	 * was renewed (still in lava or fire) since Valheim was told, at most twice a second.
	 */
	public boolean burningNews() {
		int fire = this.isOnFire() && !this.fireImmune() ? this.getRemainingFireTicks() : 0;
		if (fire <= 0) {
			this.burnToldTicks = 0;
			return false;
		}
		int since = this.tickCount - this.burnToldAt;
		int expected = this.burnToldTicks - since;
		if (this.burnToldTicks > 0 && (since < 10 || fire <= expected + 10)) {
			return false;
		}
		this.burnToldAt = this.tickCount;
		this.burnToldTicks = fire;
		return true;
	}

	/** Hostile to the player in Valheim: Minecraft's monsters and iron golems go after it. */
	private boolean hostile;

	public ValheimActorEntity(EntityType<? extends ValheimActorEntity> type, Level level) {
		super(type, level);
		this.setNoGravity(true);
		this.noPhysics = true;
		this.setInvisible(true);
		this.setSilent(true);
	}

	public boolean hostile() {
		return this.hostile;
	}

	public void setHostile(boolean hostile) {
		this.hostile = hostile;
	}

	public int formId() {
		return this.entityData.get(FORM_ID);
	}

	public void setFormId(int formId) {
		this.entityData.set(FORM_ID, formId);
	}

	@Override
	protected void defineSynchedData(SynchedEntityData.Builder builder) {
		super.defineSynchedData(builder);
		builder.define(FORM_ID, 0);
		builder.define(WIDTH, 0.6F);
		builder.define(HEIGHT, 1.8F);
	}

	public void setSize(float width, float height) {
		if (Math.abs(this.entityData.get(WIDTH) - width) > 0.01F || Math.abs(this.entityData.get(HEIGHT) - height) > 0.01F) {
			this.entityData.set(WIDTH, width);
			this.entityData.set(HEIGHT, height);
			this.refreshDimensions();
		}
	}

	@Override
	public void onSyncedDataUpdated(EntityDataAccessor<?> accessor) {
		super.onSyncedDataUpdated(accessor);
		if (WIDTH.equals(accessor) || HEIGHT.equals(accessor)) {
			this.refreshDimensions();
		}
	}

	@Override
	protected EntityDimensions getDefaultDimensions(Pose pose) {
		return EntityDimensions.scalable(this.entityData.get(WIDTH), this.entityData.get(HEIGHT));
	}

	@Override
	protected void actuallyHurt(ServerLevel level, DamageSource source, float dmg) {
		// Minecraft has applied everything (crit, sharpness, strength, cooldown, invulnerability
		// frames). Hand the result to Valheim instead of lowering our own health.
		if (this.isInvulnerableTo(level, source) || dmg <= 0.0F) {
			return;
		}
		// Burning: Valheim's own Burning effect does the damage over time (EV_IGNITE, see ValCombat).
		if (source.is(net.minecraft.world.damagesource.DamageTypes.ON_FIRE)) {
			return;
		}
		this.pendingDamage += dmg;
		if (source.getDirectEntity() instanceof Projectile) {
			this.pendingFlags |= Proto.HIT_PROJECTILE;
		}
		this.pendingWeapon = weaponClass(source);
		ItemStack held = source.getWeaponItem();
		if (held == null && source.getEntity() instanceof LivingEntity attacker) {
			held = attacker.getMainHandItem();
		}
		if (!(source.getDirectEntity() instanceof Projectile) && held != null && held.getItem() instanceof dev.valcraft.item.ValheimItems.Weapon w) {
			this.pendingValheimWeapon = w.prefabHash;
			this.pendingValheimBase = w.damage;
		} else if (!(source.getDirectEntity() instanceof Projectile) && held != null && source.getEntity() instanceof net.minecraft.world.entity.player.Player) {
			this.pendingTier = toolTier(held);
		}
		if (source.is(net.minecraft.tags.DamageTypeTags.IS_FIRE)) {
			this.pendingFlags |= Proto.HIT_FIRE;
			// lava, fire, a fireball, a magma block: fire alone, no blow (Valheim's fire-proof creatures take none)
			this.pendingFlags |= Proto.HIT_PURE_FIRE;
		}
		// A Minecraft torch sets Valheim creatures alight, as Valheim's torch does; only briefly, as it never wears out.
		if (!(source.getDirectEntity() instanceof Projectile) && held != null && (held.is(Items.TORCH) || held.is(Items.SOUL_TORCH))) {
			this.igniteForTicks(TORCH_BURN_TICKS);
		}
		if (!(source.getEntity() instanceof net.minecraft.world.entity.player.Player)) {
			this.pendingFlags |= Proto.HIT_MOB;
			// which mob, so Valheim's creature turns on its stand-in there (MobProxies)
			if (source.getEntity() instanceof net.minecraft.world.entity.Mob mob) {
				this.pendingTier = mob.getId() & 0xFFFFFF;
			}
		}
		if (source.getEntity() instanceof net.minecraft.server.level.ServerPlayer attacker) {
			this.pendingAttacker = attacker.getUUID();
		}
		this.hitThisTick = true;
		this.getCombatTracker().recordDamage(source, dmg);
	}

	@Override
	public void knockback(double power, double xd, double zd, DamageSource source, float damage, boolean comesFromEffect) {
		// Valheim owns this actor's position. Remember the strongest push for Valheim's stagger:
		// Minecraft pushes towards -(xd, zd).
		double len = Math.sqrt(xd * xd + zd * zd);
		if (len > 1e-6 && power > this.pushStrength) {
			this.pushStrength = (float) power;
			this.pushX = -xd / len;
			this.pushZ = -zd / len;
		}
		this.hitThisTick = true;
	}

	/** Player.crit() was called on us this tick. */
	public void markCritical() {
		this.pendingFlags |= Proto.HIT_CRITICAL;
	}

	/**
	 * A Minecraft tool's place in Valheim's progression, for Valheim's damage (Combat.cs): bits 0-3
	 * the material's tier (1 wood, 2 stone or gold, 3 copper, 4 iron, 5 diamond, 6 netherite), bits
	 * 4-7 the kind (1 sword, 2 axe, 3 pickaxe/shovel/hoe, 4 spear), bits 8-15 its full hit's damage
	 * x10 (so Valheim scales crits, Sharpness and cooldown the same). 0: not a tool, damage as is.
	 */
	private static int toolTier(ItemStack held) {
		int kind = held.is(ItemTags.SWORDS) ? 1 : held.is(ItemTags.AXES) ? 2
			: held.is(ItemTags.PICKAXES) || held.is(ItemTags.SHOVELS) || held.is(ItemTags.HOES) ? 3 : held.is(ItemTags.SPEARS) ? 4 : 0;
		if (kind == 0) {
			return 0;
		}
		String id = net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(held.getItem()).getPath();
		int tier = id.startsWith("wooden_") ? 1 : id.startsWith("stone_") || id.startsWith("golden_") ? 2 : id.startsWith("copper_") ? 3
			: id.startsWith("iron_") ? 4 : id.startsWith("diamond_") ? 5 : id.startsWith("netherite_") ? 6 : 0;
		if (tier == 0) {
			return 0;
		}
		var modifiers = held.getOrDefault(net.minecraft.core.component.DataComponents.ATTRIBUTE_MODIFIERS,
			net.minecraft.world.item.component.ItemAttributeModifiers.EMPTY);
		double full = modifiers.compute(net.minecraft.world.entity.ai.attributes.Attributes.ATTACK_DAMAGE, 1.0, net.minecraft.world.entity.EquipmentSlot.MAINHAND);
		int base = (int) Math.round(Math.clamp(full, 1.0, 25.0) * 10.0);
		return tier | kind << 4 | base << 8;
	}

	/** Which kind of Valheim weapon impact this hit should look and sound like. */
	private static int weaponClass(DamageSource source) {
		if (source.getDirectEntity() instanceof net.minecraft.world.entity.projectile.arrow.ThrownTrident) {
			return Proto.WEAPON_PIERCE;
		}
		if (source.getDirectEntity() instanceof Projectile) {
			return Proto.WEAPON_ARROW;
		}
		ItemStack weapon = source.getWeaponItem();
		if (weapon == null && source.getEntity() instanceof LivingEntity attacker) {
			weapon = attacker.getMainHandItem();
		}
		if (weapon == null || weapon.isEmpty()) {
			return Proto.WEAPON_UNARMED;
		}
		if (weapon.getItem() instanceof dev.valcraft.item.ValheimItems.Weapon) {
			return weapon.is(ItemTags.AXES) ? Proto.WEAPON_AXE : Proto.WEAPON_BLADE;
		}
		if (weapon.is(ItemTags.SWORDS)) {
			return Proto.WEAPON_BLADE;
		}
		if (weapon.is(ItemTags.AXES)) {
			return Proto.WEAPON_AXE;
		}
		if (weapon.is(Items.TRIDENT)) {
			return Proto.WEAPON_PIERCE;
		}
		return Proto.WEAPON_BLUNT;
	}

	/** Who landed this tick's hit (call before takeHit): a player, or null (a mob, fire, ...). */
	public java.util.@Nullable UUID hitBy() {
		return this.pendingAttacker;
	}

	/** Returns this tick's hit (damage, flags, push, weapon) and clears it; null if nothing hit us. */
	public float[] takeHit() {
		if (!this.hitThisTick) {
			return null;
		}
		float[] hit = { this.pendingDamage, (float) this.pushX, (float) this.pushZ, this.pushStrength, Float.intBitsToFloat(this.pendingFlags),
			Float.intBitsToFloat(this.pendingWeapon | this.pendingTier << 8), Float.intBitsToFloat(this.pendingValheimWeapon), this.pendingValheimBase };
		this.pendingTier = 0;
		this.pendingValheimWeapon = 0;
		this.pendingValheimBase = 0.0F;
		this.pendingDamage = 0.0F;
		this.pendingFlags = 0;
		this.pushX = this.pushZ = 0.0;
		this.pushStrength = 0.0F;
		this.hitThisTick = false;
		this.pendingAttacker = null;
		return hit;
	}

	@Override
	public void tick() {
		// Position and rotation come from Valheim (ValCombat); keep hurt timers and fire ticking.
		this.baseTick();
		this.setHealth(this.getMaxHealth());
	}

	@Override
	public boolean isPushable() {
		return false;
	}

	@Override
	protected void doPush(Entity entity) {
	}

	@Override
	public boolean canBeCollidedWith(@Nullable Entity other) {
		return false;
	}

	@Override
	public boolean shouldShowName() {
		return false;
	}

	@Override
	public boolean shouldBeSaved() {
		return false;
	}

	@Override
	protected @Nullable SoundEvent getHurtSound(DamageSource source) {
		return null; // Valheim plays the NPC's own pain sounds
	}

	@Override
	protected @Nullable SoundEvent getDeathSound() {
		return null;
	}

	@Override
	public HumanoidArm getMainArm() {
		return HumanoidArm.RIGHT;
	}
}
