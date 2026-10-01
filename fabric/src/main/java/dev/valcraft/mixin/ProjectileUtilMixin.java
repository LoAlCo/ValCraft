package dev.valcraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.valcraft.world.ValClip;
import net.minecraft.world.entity.projectile.ProjectileUtil;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Thrown projectiles (snowballs, eggs, pearls, potions) and spear reach checks see Valheim surfaces. */
@Mixin(ProjectileUtil.class)
public abstract class ProjectileUtilMixin {
	@WrapOperation(
		method = { "getHitResult", "getHitEntitiesAlong" },
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clipIncludingBorder(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private static BlockHitResult valcraft$hitValheim(Level level, ClipContext context, Operation<BlockHitResult> original) {
		return ValClip.refine(context.getFrom(), context.getTo(), original.call(level, context), ValClip.Use.PROJECTILE);
	}
}
