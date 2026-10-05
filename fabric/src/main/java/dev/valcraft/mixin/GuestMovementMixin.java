package dev.valcraft.mixin;

import dev.valcraft.link.ValLink;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.LevelReader;
import net.minecraft.world.phys.AABB;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Guests walk on their own Valheim's ground, which this server knows only as well as their
 * client has forwarded it (GuestLink): its movement checks would pull them back ("moved too
 * quickly" after a Valheim portal, "moved wrongly" against a piece of ground it has slightly
 * differently). Their own client, on their own Valheim's exact ground, is trusted instead, as
 * the host's own player always is.
 */
@Mixin(ServerGamePacketListenerImpl.class)
public abstract class GuestMovementMixin {
	@Inject(method = "shouldCheckPlayerMovement", at = @At("HEAD"), cancellable = true)
	private void valcraft$trustGuests(boolean fallFlying, CallbackInfoReturnable<Boolean> cir) {
		if (ValLink.active()) {
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "isEntityCollidingWithAnythingNew", at = @At("HEAD"), cancellable = true)
	private void valcraft$guestsStandOnTheirValheim(LevelReader level, Entity entity, AABB box, double x, double y, double z, CallbackInfoReturnable<Boolean> cir) {
		if (ValLink.active()) {
			cir.setReturnValue(false);
		}
	}
}
