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

	@Shadow
	protected net.minecraft.world.level.pathfinder.Path path;
	@Shadow
	protected float maxDistanceToWaypoint;

	@org.spongepowered.asm.mixin.Unique
	private int valcraft$nearIndex = -1;
	@org.spongepowered.asm.mixin.Unique
	private int valcraft$nearTicks;

	/**
	 * Following a path over Valheim's ground: a point is reached when the mob is over it, whatever
	 * the height (a rock, a log or a slope under it), and a point the mob has been circling for a
	 * second without reaching it (something Valheim put in the way) is skipped. Vanilla follows the
	 * rest. Without this, mobs spun in place until their stuck check gave up on the path.
	 */
	@Inject(method = "followThePath", at = @At("HEAD"))
	private void valcraft$followValheimGround(org.spongepowered.asm.mixin.injection.callback.CallbackInfo ci) {
		Path p = this.path;
		if (p == null || p.isDone() || !dev.valcraft.world.TerrainPath.isOurs(p)) {
			return;
		}
		double reach = Math.max(this.maxDistanceToWaypoint, 0.5);
		// never past the last point: vanilla reads the next point right after this, and finishes the path itself
		while (p.getNextNodeIndex() < p.getNodeCount() - 1) {
			var node = p.getNextNode();
			double dx = this.mob.getX() - (node.x + 0.5), dz = this.mob.getZ() - (node.z + 0.5);
			double d2 = dx * dx + dz * dz;
			if (d2 < reach * reach) {
				p.advance();
				this.valcraft$nearTicks = 0;
				continue;
			}
			if (d2 < 1.5 * 1.5) {
				if (p.getNextNodeIndex() == this.valcraft$nearIndex) {
					if (++this.valcraft$nearTicks > 20) {
						p.advance();
						this.valcraft$nearTicks = 0;
						continue;
					}
				} else {
					this.valcraft$nearIndex = p.getNextNodeIndex();
					this.valcraft$nearTicks = 0;
				}
			}
			break;
		}
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
