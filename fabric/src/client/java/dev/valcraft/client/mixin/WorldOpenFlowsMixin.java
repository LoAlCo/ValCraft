package dev.valcraft.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.valcraft.ValCraft;
import net.minecraft.client.gui.screens.worldselection.WorldOpenFlows;
import net.minecraft.world.level.storage.LevelStorageSource;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * The mirror world's custom dimension type marks its worldgen as "experimental", which makes
 * Minecraft ask for a backup every time it opens. Skip that prompt for ValCraft's worlds only.
 */
@Mixin(WorldOpenFlows.class)
public abstract class WorldOpenFlowsMixin {
	@WrapOperation(
		method = "openWorldCheckWorldStemCompatibility",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/gui/screens/worldselection/WorldOpenFlows;askForBackup(Lnet/minecraft/world/level/storage/LevelStorageSource$LevelStorageAccess;ZLjava/lang/Runnable;Ljava/lang/Runnable;)V"
		)
	)
	private void valcraft$skipBackupPrompt(
		WorldOpenFlows self, LevelStorageSource.LevelStorageAccess access, boolean oldCustomized, Runnable proceed, Runnable cancel, Operation<Void> original
	) {
		String id = access.getLevelId();
		if (ValCraft.WORLD_NAME.equals(id) || id.startsWith(ValCraft.WORLD_NAME + "-")) { // one per Valheim world
			proceed.run();
		} else {
			original.call(self, access, oldCustomized, proceed, cancel);
		}
	}
}
