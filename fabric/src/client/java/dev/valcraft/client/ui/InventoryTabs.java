package dev.valcraft.client.ui;

import dev.valcraft.client.mixin.ContainerScreenAccessor;
import net.fabricmc.fabric.api.client.screen.v1.ScreenEvents;
import net.fabricmc.fabric.api.client.screen.v1.ScreenMouseEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/**
 * Tabs along the top of Minecraft's inventory, like its creative inventory's, for the tabs of
 * Valheim's inventory: the inventory itself, then Valheim's skills, trophies, compendium and the
 * Valheim-only items (ValheimTabScreen). Only with [UI] Style Valheim.
 */
public final class InventoryTabs {
	public static final int INVENTORY = 0, SKILLS = 1, TROPHIES = 2, COMPENDIUM = 3, VALHEIM_ITEMS = 4, COUNT = 5;
	public static final int TAB_W = 26, TAB_H = 32, TAB_STEP = 27;
	private static final Component[] NAMES = { Component.literal("Inventory"), Component.literal("Skills"), Component.literal("Trophies"),
		Component.literal("Compendium"), Component.literal("Valheim Items") };
	private static ItemStack[] icons;

	private InventoryTabs() {
	}

	public static void install() {
		ScreenEvents.AFTER_INIT.register((minecraft, screen, width, height) -> {
			if (!(screen instanceof InventoryScreen)) {
				return;
			}
			ScreenEvents.afterExtract(screen).register((s, g, mouseX, mouseY, delta) -> {
				if (ValheimUi.active()) {
					var pos = (ContainerScreenAccessor) s;
					draw(g, pos.valcraft$left(), pos.valcraft$top(), INVENTORY, mouseX, mouseY, true);
				}
			});
			ScreenMouseEvents.allowMouseClick(screen).register((s, event) -> {
				if (!ValheimUi.active()) {
					return true;
				}
				var pos = (ContainerScreenAccessor) s;
				int tab = hit(pos.valcraft$left(), pos.valcraft$top(), event.x(), event.y());
				if (tab > INVENTORY) {
					open(tab);
					return false;
				}
				return true;
			});
		});
	}

	public static void open(int tab) {
		var minecraft = Minecraft.getInstance();
		if (minecraft.player == null) {
			return;
		}
		minecraft.gui.setScreen(tab == INVENTORY ? new InventoryScreen(minecraft.player) : new ValheimTabScreen(tab));
	}

	/** Which tab is at the mouse, or -1. */
	public static int hit(int left, int top, double x, double y) {
		if (y < top - TAB_H + 4 || y >= top) {
			return -1;
		}
		for (int i = 0; i < COUNT; i++) {
			int tx = left + i * TAB_STEP;
			if (x >= tx && x < tx + TAB_W) {
				return i;
			}
		}
		return -1;
	}

	/**
	 * The tab strip above a panel at left, top. Unselected tabs stop at the panel's edge; the
	 * selected one runs into it, joined to it like Minecraft's creative tabs (drawn after the panel).
	 */
	public static void draw(GuiGraphicsExtractor g, int left, int top, int selected, int mouseX, int mouseY, boolean selectedToo) {
		for (int i = 0; i < COUNT; i++) {
			if (i != selected) {
				tab(g, left, top, i, false);
			}
		}
		if (selectedToo) {
			tab(g, left, top, selected, true);
		}
		int over = hit(left, top, mouseX, mouseY);
		if (over >= 0) {
			g.setTooltipForNextFrame(Minecraft.getInstance().font, NAMES[over], mouseX, mouseY);
		}
	}

	public static void tab(GuiGraphicsExtractor g, int left, int top, int i, boolean selected) {
		int x = left + i * TAB_STEP, y = top - 28;
		Identifier sprite = Identifier.withDefaultNamespace("container/creative_inventory/tab_top_" + (selected ? "selected_" : "unselected_") + (i + 1));
		if (selected) {
			g.blitSprite(RenderPipelines.GUI_TEXTURED, sprite, x, y, TAB_W, TAB_H);
		} else {
			g.blitSprite(RenderPipelines.GUI_TEXTURED, sprite, TAB_W, TAB_H, 0, 0, x, y, TAB_W, 28);
		}
		g.fakeItem(icon(i), x + 5, y + 8 + (selected ? 1 : 2));
	}

	private static ItemStack icon(int i) {
		if (icons == null) {
			var trophy = BuiltInRegistries.ITEM.getOptional(Identifier.fromNamespaceAndPath("valcraft", "deer_trophy")).orElse(Items.SKELETON_SKULL);
			icons = new ItemStack[] { new ItemStack(Items.CHEST), new ItemStack(Items.EXPERIENCE_BOTTLE), new ItemStack(trophy),
				new ItemStack(Items.WRITABLE_BOOK), new ItemStack(Items.BUNDLE) };
		}
		return icons[i];
	}

	public static Component name(int tab) {
		return NAMES[tab];
	}
}
