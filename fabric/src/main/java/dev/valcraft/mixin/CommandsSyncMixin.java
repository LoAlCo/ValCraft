package dev.valcraft.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import dev.valcraft.world.BuildSync;
import java.util.function.Consumer;
import net.minecraft.commands.CommandSourceStack;
import net.minecraft.commands.Commands;
import net.minecraft.commands.execution.ExecutionContext;
import org.spongepowered.asm.mixin.Mixin;

/** Every command runs through here: while one does, the blocks it sets are noted for BuildSync (/fill, /setblock, /clone...). */
@Mixin(Commands.class)
public abstract class CommandsSyncMixin {
	@WrapMethod(method = "executeCommandInContext")
	private static void valcraft$noteCommandBlocks(CommandSourceStack source, Consumer<ExecutionContext<CommandSourceStack>> contextConsumer, Operation<Void> original) {
		BuildSync.commandStarted();
		try {
			original.call(source, contextConsumer);
		} finally {
			BuildSync.commandEnded();
		}
	}
}
