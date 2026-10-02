package dev.valcraft.mixin;

import dev.valcraft.world.ValCollision;
import dev.valcraft.world.ValGround;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.ai.navigation.GroundPathNavigation;
import net.minecraft.world.entity.ai.navigation.PathNavigation;
import net.minecraft.world.level.pathfinder.Path;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Walking mobs on Valheim terrain: straight there when the way is clear, else TerrainPath's search of the ground. */
@Mixin(PathNavigation.class)
public abstract class PathNavigationValheimMixin {
	@Shadow
	@Final
	protected Mob mob;
	@Shadow
	private BlockPos targetPos;
	@Shadow
	private int reachRange;

	@Shadow
	private void resetStuckTimeout() {
	}

	@Inject(method = "createPath(Ljava/util/Set;IZIF)Lnet/minecraft/world/level/pathfinder/Path;", at = @At("HEAD"), cancellable = true)
	private void valcraft$straightOnValheim(Set<BlockPos> targets, int regionOffset, boolean offsetUpward, int reachRange, float followRange,
		CallbackInfoReturnable<Path> cir) {
		if (targets.size() != 1 || !((Object) this instanceof GroundPathNavigation) || !ValCollision.active() || this.mob.level().isClientSide()) {
			return;
		}
		BlockPos target = targets.iterator().next();
		if (!dev.valcraft.world.TerrainPath.applies(this.mob)) {
			return;  // block terrain, or Valheim's terrain height not known here yet: Minecraft's own pathfinder
		}
		Path path = dev.valcraft.world.TerrainPath.find(this.mob, target, reachRange);
		if (path != null) {
			this.targetPos = target;
			this.reachRange = reachRange;
			this.resetStuckTimeout();
			cir.setReturnValue(path);
		}
	}
}
