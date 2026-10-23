package dev.valcraft.client.mixin;

import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Where the inventory's panel is (it moves with the recipe book), for InventoryTabs. */
@Mixin(AbstractContainerScreen.class)
public interface ContainerScreenAccessor {
	@Accessor("leftPos")
	int valcraft$left();

	@Accessor("topPos")
	int valcraft$top();

	@Accessor("imageWidth")
	int valcraft$width();
}
