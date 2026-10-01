package dev.valcraft.mixin;

import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import net.minecraft.world.level.ServerExplosion;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Tells Valheim about every Minecraft explosion (TNT, creepers, beds, ...) once it has gone off, so
 * Valheim's own physics feel it: loose objects are thrown and people are knocked away.
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
		ValLink.pushEvent(Proto.EV_EXPLOSION, 0, (float) center.x, (float) center.y, (float) center.z, self.radius(), 0);
	}
}
