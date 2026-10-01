package dev.valcraft.mixin;

import dev.valcraft.link.ValLink;
import net.minecraft.world.entity.MoverType;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Crouching doesn't stop at edges. Minecraft looks for block collision under the player to decide
 * where an edge is; the Valheim ground the player walks on isn't blocks (the player collides with
 * its exact triangles), so every direction looked like a drop and crouching froze the player.
 */
@Mixin(Player.class)
public abstract class PlayerEdgeMixin {
	@Inject(method = "maybeBackOffFromEdge", at = @At("HEAD"), cancellable = true)
	private void valcraft$crouchWalkAnywhere(Vec3 delta, MoverType moverType, CallbackInfoReturnable<Vec3> cir) {
		if (ValLink.active()) {
			cir.setReturnValue(delta);
		}
	}
}
