package dev.valcraft.mixin;

import dev.valcraft.combat.ValheimActorEntity;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.ai.goal.GoalSelector;
import net.minecraft.world.entity.ai.goal.target.NearestAttackableTargetGoal;
import net.minecraft.world.entity.animal.golem.IronGolem;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Minecraft's monsters (zombies, skeletons, creepers, spiders...) and iron golems go after Valheim's
 * hostile creatures as they would after an enemy of their own: chase, then fight (their hits land
 * in Valheim). After the player in priority: a player in reach still comes first.
 */
@Mixin(Mob.class)
public abstract class MobValheimTargetMixin {
	@Shadow
	@Final
	protected GoalSelector targetSelector;

	@Inject(method = "<init>", at = @At("TAIL"))
	private void valcraft$huntValheimCreatures(EntityType<? extends Mob> type, Level level, CallbackInfo ci) {
		Mob self = (Mob) (Object) this;
		if (level == null || level.isClientSide() || !(self instanceof Enemy || self instanceof IronGolem)) {
			return;
		}
		this.targetSelector.addGoal(3, new NearestAttackableTargetGoal<>(self, ValheimActorEntity.class, 10, true, false,
			(target, server) -> target instanceof ValheimActorEntity actor && actor.hostile() && !actor.isDeadOrDying()));
	}
}
