package dev.valcraft.mixin;

import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import net.minecraft.world.level.Explosion;
import net.minecraft.world.level.ServerExplosion;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Tells Valheim about every Minecraft explosion (TNT, creepers, beds, ...) once it has gone off, so
 * Valheim's own physics feel it: loose objects are thrown and people are knocked away. One that
 * leaves blocks alone (a creeper with mobGriefing off) is flagged EXPLOSION_KEEPS_BLOCKS: Valheim
 * then hurts creatures only, with no craters or broken trees, rocks and buildings.
 */
@Mixin(ServerExplosion.class)
public abstract class ServerExplosionMixin {
	@Inject(method = "explode", at = @At("RETURN"))
	private void valcraft$tellValheim(CallbackInfoReturnable<Integer> cir) {
		if (!ValLink.active()) {
			return;
		}
		ServerExplosion self = (ServerExplosion) (Object) this;
		var center = self.center();
		int flags = self.getBlockInteraction() == Explosion.BlockInteraction.KEEP ? Proto.EXPLOSION_KEEPS_BLOCKS : 0;
		// The nearest player's Valheim has the ground and creatures there (just one: it hurts and digs once).
		dev.valcraft.net.ValNet.pushEventNearest(self.level(), center.x, center.y, center.z, Proto.EV_EXPLOSION, 0, (float) center.x, (float) center.y,
			(float) center.z, self.radius(), flags);
	}
}
