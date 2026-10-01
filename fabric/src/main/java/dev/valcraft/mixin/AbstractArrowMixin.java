package dev.valcraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.valcraft.combat.ValheimActorEntity;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import dev.valcraft.world.ValClip;
import net.minecraft.util.Mth;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.entity.projectile.arrow.Arrow;
import net.minecraft.world.entity.projectile.arrow.SpectralArrow;
import net.minecraft.world.phys.EntityHitResult;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Arrows and tridents hit Valheim's exact surfaces. They then stick where they hit: the block state
 * there is air, the same as what they recorded on impact, so vanilla never makes them fall out.
 */
@Mixin(AbstractArrow.class)
public abstract class AbstractArrowMixin {
	@WrapOperation(
		method = "tick",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clipIncludingBorder(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private BlockHitResult valcraft$hitValheim(Level level, ClipContext context, Operation<BlockHitResult> original) {
		return ValClip.refine(context.getFrom(), context.getTo(), original.call(level, context), ValClip.Use.PROJECTILE);
	}

	@Unique
	private Vec3 valcraft$hitAt;

	@Inject(method = "onHitEntity", at = @At("HEAD"))
	private void valcraft$rememberHit(EntityHitResult hitResult, CallbackInfo ci) {
		this.valcraft$hitAt = hitResult.getLocation();
	}

	/**
	 * Where Minecraft counts an arrow as stuck in a creature (it hurt it and didn't pierce): if that
	 * creature is a Valheim NPC's stand-in, Valheim pins the arrow to the NPC's skeleton.
	 */
	@WrapOperation(method = "onHitEntity", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/LivingEntity;setArrowCount(I)V"))
	private void valcraft$stickInValheimActor(LivingEntity mob, int count, Operation<Void> original) {
		original.call(mob, count);
		if (!(mob instanceof ValheimActorEntity actor) || this.valcraft$hitAt == null || !ValLink.active()) {
			return;
		}
		AbstractArrow self = (AbstractArrow) (Object) this;
		Vec3 v = self.getDeltaMovement();
		float yaw = (float) (Mth.atan2(v.x, v.z) * Mth.RAD_TO_DEG);
		float pitch = (float) (Mth.atan2(v.y, v.horizontalDistance()) * Mth.RAD_TO_DEG);
		int texture = self instanceof SpectralArrow ? 2 : self instanceof Arrow tippable && tippable.getColor() > 0 ? 1 : 0;
		Vec3 at = this.valcraft$hitAt;
		ValLink.pushEvent(Proto.EV_ARROW_STUCK, actor.formId(), (float) at.x, (float) at.y, (float) at.z, yaw, Float.floatToRawIntBits(pitch), texture);
	}
}
