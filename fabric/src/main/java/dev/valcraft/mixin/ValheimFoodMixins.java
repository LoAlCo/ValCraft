package dev.valcraft.mixin;

import dev.valcraft.combat.ValheimVitals;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.food.FoodData;
import net.minecraft.world.food.FoodProperties;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.Consumable;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** [Valheim Features] FoodAndHealth: eating and hunger the Valheim way (ValheimVitals). */
public final class ValheimFoodMixins {
	private ValheimFoodMixins() {
	}

	/** Hunger stays full and does nothing: no draining, no healing from it, no starving. */
	@Mixin(FoodData.class)
	public abstract static class Hunger {
		@Inject(method = "tick", at = @At("HEAD"), cancellable = true)
		private void valcraft$valheimFood(ServerPlayer player, CallbackInfo ci) {
			if (ValheimVitals.active(player)) {
				FoodData self = (FoodData) (Object) this;
				self.setFoodLevel(20);
				self.setSaturation(0.0F);
				ci.cancel();
			}
		}
	}

	/** Whether a food can be eaten now is Valheim's rule (three foods, not the same again too soon). */
	@Mixin(Consumable.class)
	public abstract static class CanEat {
		private static long valcraft$lastMessage;

		@Inject(method = "canConsume", at = @At("HEAD"), cancellable = true)
		private void valcraft$valheimRules(LivingEntity entity, ItemStack stack, CallbackInfoReturnable<Boolean> cir) {
			if (!(entity instanceof Player player) || !stack.has(net.minecraft.core.component.DataComponents.FOOD) || !ValheimVitals.active(player)) {
				return;
			}
			int why = ValheimVitals.canEat(player, stack);
			cir.setReturnValue(why == 0);
			if (why != 0 && player.level().isClientSide()) {
				long now = System.currentTimeMillis();
				if (now - valcraft$lastMessage > 1000) {
					valcraft$lastMessage = now;
					player.sendOverlayMessage(Component.literal(why == 1 ? "You can't eat more of that yet" : "You can't eat any more"));
				}
			}
		}
	}

	/** Eating gives Minecraft nothing: the player's Valheim eats the matching Valheim food instead. */
	@Mixin(FoodProperties.class)
	public abstract static class Eat {
		@Inject(method = "onConsume", at = @At("HEAD"), cancellable = true)
		private void valcraft$valheimEats(Level level, LivingEntity entity, ItemStack stack, Consumable consumable, CallbackInfo ci) {
			if (entity instanceof Player player && ValheimVitals.active(player)) {
				if (!level.isClientSide() && player instanceof ServerPlayer sp) {
					ValheimVitals.ate(sp, stack);
				}
				ci.cancel();
			}
		}
	}
}
