package dev.valcraft.mixin;

import dev.valcraft.ValCraft;
import dev.valcraft.combat.ValCombat;
import dev.valcraft.combat.ValheimActorEntity;
import dev.valcraft.link.Proto;
import dev.valcraft.link.ValLink;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.server.level.ServerPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ServerPlayer.class)
public abstract class ServerPlayerMixin {
	/** Critical hits on a Valheim actor are flagged so Valheim can play them up. */
	@Inject(method = "crit", at = @At("HEAD"))
	private void valcraft$critValheim(Entity entity, CallbackInfo ci) {
		if (entity instanceof ValheimActorEntity proxy) {
			proxy.markCritical();
		}
	}

	/** Dying in Minecraft is dying in Valheim: the host's through the link, a guest's through theirs. */
	@Inject(method = "die", at = @At("HEAD"))
	private void valcraft$diesInValheim(DamageSource source, CallbackInfo ci) {
		ServerPlayer self = (ServerPlayer) (Object) this;
		int attacker = ValCombat.attackerFormId(source);
		if (!dev.valcraft.net.ValNet.isHost(self)) {
			if (net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking.canSend(self, dev.valcraft.net.ValNet.Died.TYPE)) {
				net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking.send(self, new dev.valcraft.net.ValNet.Died(attacker));
			}
			ValCraft.LOG.info("ValCraft: guest {} died ({}); telling their Valheim", self.getPlainTextName(), source.getMsgId());
			return;
		}
		if (ValLink.active()) {
			ValLink.pushEvent(Proto.EV_PLAYER_DIED, attacker, 0, 0, 0, 0, 0);
			ValCraft.LOG.info("ValCraft: player died ({}); telling Valheim", source.getMsgId());
		}
	}
}
