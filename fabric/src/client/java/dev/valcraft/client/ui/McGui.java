package dev.valcraft.client.ui;

import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Minecraft's container look, drawn: the grey panel with its light and dark edges, sunken areas, slots. */
public final class McGui {
	private static final int BLACK = 0xFF000000, PANEL = 0xFFC6C6C6, LIGHT = 0xFFFFFFFF, SHADE = 0xFF555555;
	private static final int SUNK = 0xFF8B8B8B, SUNK_DARK = 0xFF373737;

	private McGui() {
	}

	/** A container panel like the inventory's: rounded black outline, white top-left, grey bottom-right. */
	public static void panel(GuiGraphicsExtractor g, int x, int y, int w, int h) {
		g.fill(x + 1, y + 2, x + w - 1, y + h - 2, PANEL);  // the grey, leaving the corners out
		g.fill(x + 2, y + 1, x + w - 2, y + 2, PANEL);
		g.fill(x + 2, y + h - 2, x + w - 2, y + h - 1, PANEL);
		g.fill(x + 2, y + 1, x + w - 3, y + 3, LIGHT);
		g.fill(x + 1, y + 2, x + 3, y + h - 3, LIGHT);
		g.fill(x + 3, y + h - 3, x + w - 2, y + h - 1, SHADE);
		g.fill(x + w - 3, y + 3, x + w - 1, y + h - 2, SHADE);
		// the outline, with Minecraft's cut corners
		g.fill(x + 2, y, x + w - 3, y + 1, BLACK);
		g.fill(x + 3, y + h - 1, x + w - 2, y + h, BLACK);
		g.fill(x, y + 2, x + 1, y + h - 3, BLACK);
		g.fill(x + w - 1, y + 3, x + w, y + h - 2, BLACK);
		g.fill(x + 1, y + 1, x + 2, y + 2, BLACK);
		g.fill(x + w - 3, y + 1, x + w - 2, y + 2, BLACK);
		g.fill(x + w - 2, y + 2, x + w - 1, y + 3, BLACK);
		g.fill(x + 1, y + h - 3, x + 2, y + h - 2, BLACK);
		g.fill(x + 2, y + h - 2, x + 3, y + h - 1, BLACK);
		g.fill(x + w - 2, y + h - 2, x + w - 1, y + h - 1, BLACK);
	}

	/** A sunken area (like a slot, any size): dark top-left edge, white bottom-right. */
	public static void inset(GuiGraphicsExtractor g, int x, int y, int w, int h) {
		g.fill(x, y, x + w, y + h, SUNK);
		g.fill(x, y, x + w - 1, y + 1, SUNK_DARK);
		g.fill(x, y, x + 1, y + h - 1, SUNK_DARK);
		g.fill(x + 1, y + h - 1, x + w, y + h, LIGHT);
		g.fill(x + w - 1, y + 1, x + w, y + h, LIGHT);
	}

	/** An inventory slot, 18 x 18. */
	public static void slot(GuiGraphicsExtractor g, int x, int y) {
		inset(g, x, y, 18, 18);
	}

	public static boolean over(double mx, double my, int x, int y, int w, int h) {
		return mx >= x && mx < x + w && my >= y && my < y + h;
	}
}
