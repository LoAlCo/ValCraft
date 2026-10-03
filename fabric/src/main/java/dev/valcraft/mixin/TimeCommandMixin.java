package dev.valcraft.mixin;

import dev.valcraft.world.TimeSync;
import net.minecraft.commands.CommandSourceStack;
import net.minecraft.core.Holder;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.clock.ClockTimeMarker;
import net.minecraft.world.clock.WorldClock;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Minecraft's /time set and /time add move Valheim's clock too (TimeSync.commanded): otherwise
 * TimeSync would put Minecraft straight back on Valheim's time. TimeSync's own "time set" is left out.
 */
@Mixin(net.minecraft.server.commands.TimeCommand.class)
public abstract class TimeCommandMixin {
	private static long valcraft$before;

	@Inject(method = "setTotalTicks", at = @At("HEAD"))
	private static void valcraft$beforeSet(CommandSourceStack source, Holder<WorldClock> clock, int ticks, CallbackInfoReturnable<Integer> cir) {
		valcraft$before = source.getServer().overworld().getDefaultClockTime();
	}

	@Inject(method = "addTime", at = @At("HEAD"))
	private static void valcraft$beforeAdd(CommandSourceStack source, Holder<WorldClock> clock, int ticks, CallbackInfoReturnable<Integer> cir) {
		valcraft$before = source.getServer().overworld().getDefaultClockTime();
	}

	@Inject(method = "setTimeToTimeMarker", at = @At("HEAD"))
	private static void valcraft$beforeMarker(CommandSourceStack source, Holder<WorldClock> clock, ResourceKey<ClockTimeMarker> marker, CallbackInfoReturnable<Integer> cir) {
		valcraft$before = source.getServer().overworld().getDefaultClockTime();
	}

	@Inject(method = "setTotalTicks", at = @At("RETURN"))
	private static void valcraft$set(CommandSourceStack source, Holder<WorldClock> clock, int ticks, CallbackInfoReturnable<Integer> cir) {
		TimeSync.commanded(source.getServer(), valcraft$before, false);
	}

	@Inject(method = "addTime", at = @At("RETURN"))
	private static void valcraft$add(CommandSourceStack source, Holder<WorldClock> clock, int ticks, CallbackInfoReturnable<Integer> cir) {
		TimeSync.commanded(source.getServer(), valcraft$before, true);
	}

	@Inject(method = "setTimeToTimeMarker", at = @At("RETURN"))
	private static void valcraft$marker(CommandSourceStack source, Holder<WorldClock> clock, ResourceKey<ClockTimeMarker> marker, CallbackInfoReturnable<Integer> cir) {
		TimeSync.commanded(source.getServer(), valcraft$before, false);
	}
}
