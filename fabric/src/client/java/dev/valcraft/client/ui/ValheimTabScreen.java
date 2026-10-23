package dev.valcraft.client.ui;

import dev.valcraft.link.ValheimInfo;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.ChatFormatting;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.util.FormattedCharSequence;

/**
 * One of Valheim's inventory tabs inside Minecraft's inventory (InventoryTabs): skills, trophies,
 * the compendium or the Valheim-only items, on a panel the size of Minecraft's inventory and drawn
 * the way Minecraft draws its own (grey panel, sunken list, slots, the creative inventory's scroller).
 */
public final class ValheimTabScreen extends Screen {
	private static final int W = 176, H = 166;
	// the sunken list area, and the scroller's track at its right
	private static final int LIST_X = 7, LIST_Y = 17, LIST_W = 162, LIST_H = 142, TRACK_W = 14;
	private static final int LABEL = 0xFF404040, WHITE = 0xFFFFFFFF, GREY = 0xFFA0A0A0, XP_GREEN = 0xFF80FF20, BONUS = 0xFF7FD4FF;
	private static final Identifier SCROLLER = Identifier.withDefaultNamespace("container/creative_inventory/scroller");
	private static final Identifier SCROLLER_OFF = Identifier.withDefaultNamespace("container/creative_inventory/scroller_disabled");
	private static final int SKILL_ROW = 20, TEXT_ROW = 12, SLOT = 18;

	private final int tab;
	private int left, top, scroll;
	private int reading = -1;  // the compendium entry open, or -1 for the list
	private List<FormattedCharSequence> readingLines = List.of();
	private boolean dragging;

	public ValheimTabScreen(int tab) {
		super(InventoryTabs.name(tab));
		this.tab = tab;
	}

	@Override
	protected void init() {
		left = (width - W) / 2;
		top = (height - H) / 2;
	}

	@Override
	public boolean isPauseScreen() {
		return false;
	}

	@Override
	public void tick() {
		ValheimUi.update();
	}

	// ---- layout ----------------------------------------------------------------------------------

	private int listLeft() {
		return left + LIST_X + 1;
	}

	private int listTop() {
		return top + LIST_Y + 1;
	}

	private int listWidth() {
		return LIST_W - TRACK_W - 2;
	}

	private int listHeight() {
		return LIST_H - 2;
	}

	private int rowHeight() {
		return switch (tab) {
			case InventoryTabs.SKILLS -> SKILL_ROW;
			case InventoryTabs.COMPENDIUM -> reading < 0 ? TEXT_ROW : 9;
			default -> SLOT;
		};
	}

	private int columns() {
		return tab == InventoryTabs.TROPHIES || tab == InventoryTabs.VALHEIM_ITEMS ? listWidth() / SLOT : 1;
	}

	private int rows() {
		return switch (tab) {
			case InventoryTabs.SKILLS -> ValheimUi.skills.size();
			case InventoryTabs.TROPHIES -> (ValheimUi.trophies.size() + columns() - 1) / columns();
			case InventoryTabs.COMPENDIUM -> reading < 0 ? ValheimUi.texts.size() : readingLines.size() + 2 + (reading == 0 && !ValheimUi.effects.isEmpty() ? 2 : 0);
			default -> ValheimUi.packH;
		};
	}

	private int visibleRows() {
		return listHeight() / rowHeight();
	}

	private int maxScroll() {
		return Math.max(0, rows() - visibleRows());
	}

	// ---- drawing ---------------------------------------------------------------------------------

	@Override
	public void extractRenderState(GuiGraphicsExtractor g, int mouseX, int mouseY, float delta) {
		ValheimUi.update();
		scroll = Math.clamp(scroll, 0, maxScroll());
		InventoryTabs.draw(g, left, top, tab, mouseX, mouseY, false);
		McGui.panel(g, left, top, W, H);
		InventoryTabs.tab(g, left, top, tab, true);
		super.extractRenderState(g, mouseX, mouseY, delta);

		g.text(font, title, left + 8, top + 6, LABEL, false);
		String right = switch (tab) {
			case InventoryTabs.SKILLS -> ValheimUi.skills.isEmpty() ? "" : ValheimUi.skillTotal + " / " + ValheimUi.skillCap;
			case InventoryTabs.TROPHIES -> ValheimUi.trophies.isEmpty() ? "" : String.valueOf(ValheimUi.trophies.size());
			case InventoryTabs.VALHEIM_ITEMS -> ValheimUi.maxWeight > 0 ? "Weight " + ValheimUi.weight + "/" + ValheimUi.maxWeight : "";
			default -> "";
		};
		g.text(font, right, left + W - 8 - font.width(right), top + 6, LABEL, false);

		McGui.inset(g, left + LIST_X, top + LIST_Y, LIST_W - TRACK_W, LIST_H);
		McGui.inset(g, left + LIST_X + LIST_W - TRACK_W, top + LIST_Y, TRACK_W, LIST_H);
		int max = maxScroll();
		int knobY = top + LIST_Y + 1 + (max == 0 ? 0 : Math.round((LIST_H - 2 - 15) * (float) scroll / max));
		g.blitSprite(RenderPipelines.GUI_TEXTURED, max == 0 ? SCROLLER_OFF : SCROLLER, left + LIST_X + LIST_W - TRACK_W + 1, knobY, 12, 15);

		g.enableScissor(listLeft(), listTop(), listLeft() + listWidth(), listTop() + listHeight());
		if (ValheimInfo.latest() == null) {
			centred(g, "Waiting for Valheim...");
		} else {
			switch (tab) {
				case InventoryTabs.SKILLS -> skills(g, mouseX, mouseY);
				case InventoryTabs.TROPHIES -> trophies(g, mouseX, mouseY);
				case InventoryTabs.COMPENDIUM -> compendium(g, mouseX, mouseY);
				default -> items(g, mouseX, mouseY);
			}
		}
		g.disableScissor();
	}

	private void centred(GuiGraphicsExtractor g, String text) {
		g.text(font, text, listLeft() + (listWidth() - font.width(text)) / 2, listTop() + listHeight() / 2 - 4, GREY, true);
	}

	private boolean inList(int mouseX, int mouseY) {
		return mouseX >= listLeft() && mouseX < listLeft() + listWidth() && mouseY >= listTop() && mouseY < listTop() + listHeight();
	}

	private void tooltip(GuiGraphicsExtractor g, String title, String body, int mouseX, int mouseY, String... extra) {
		List<FormattedCharSequence> lines = new ArrayList<>();
		lines.add(Component.literal(title).getVisualOrderText());
		for (String e : extra) {
			if (!e.isEmpty()) {
				lines.add(Component.literal(e).withStyle(ChatFormatting.BLUE).getVisualOrderText());
			}
		}
		if (body != null && !body.isEmpty()) {
			lines.addAll(font.split(Component.literal(body).withStyle(ChatFormatting.GRAY), 200));
		}
		g.setTooltipForNextFrame(lines, mouseX, mouseY);
	}

	private void iconOr(GuiGraphicsExtractor g, String icon, String name, int x, int y) {
		if (!ValheimUi.icon(g, icon, x, y)) {
			String s = name.isEmpty() ? "?" : name.substring(0, 1);
			g.text(font, s, x + 8 - font.width(s) / 2, y + 4, GREY, true);
		}
	}

	private void skills(GuiGraphicsExtractor g, int mouseX, int mouseY) {
		var list = ValheimUi.skills;
		if (list.isEmpty()) {
			centred(g, "No skills yet");
			return;
		}
		int x = listLeft(), w = listWidth();
		for (int i = scroll; i < list.size() && i < scroll + visibleRows() + 1; i++) {
			var s = list.get(i);
			int y = listTop() + (i - scroll) * SKILL_ROW;
			boolean over = inList(mouseX, mouseY) && mouseY >= y && mouseY < y + SKILL_ROW;
			if (over) {
				g.fill(x, y, x + w, y + SKILL_ROW, 0x30FFFFFF);
			}
			iconOr(g, s.icon(), s.name(), x + 2, y + 2);
			g.text(font, s.name(), x + 21, y + 2, WHITE, true);
			String level = String.valueOf(s.level());
			int lx = x + w - 3 - font.width(level);
			g.text(font, level, lx, y + 2, XP_GREEN, true);
			if (s.bonus() != 0) {
				String bonus = (s.bonus() > 0 ? "+" : "") + s.bonus();
				g.text(font, bonus, lx - 2 - font.width(bonus), y + 2, BONUS, true);
			}
			// progress to the next level, as Minecraft's experience bar
			int bx = x + 21, bw = w - 24, by = y + 13;
			g.fill(bx, by, bx + bw, by + 3, 0xFF000000);
			g.fill(bx + 1, by + 1, bx + 1 + Math.round((bw - 2) * s.progress()), by + 2, XP_GREEN);
			if (over) {
				tooltip(g, s.name(), s.desc(), mouseX, mouseY,
					"Level " + s.level() + (s.bonus() != 0 ? " (" + (s.level() + s.bonus()) + " with bonuses)" : "") + ", "
						+ Math.round(s.progress() * 100) + "% to the next");
			}
		}
	}

	private void trophies(GuiGraphicsExtractor g, int mouseX, int mouseY) {
		var list = ValheimUi.trophies;
		if (list.isEmpty()) {
			centred(g, "No trophies yet");
			return;
		}
		int cols = columns();
		for (int i = scroll * cols; i < list.size() && i < (scroll + visibleRows() + 1) * cols; i++) {
			var t = list.get(i);
			int x = listLeft() + i % cols * SLOT, y = listTop() + (i / cols - scroll) * SLOT;
			McGui.slot(g, x, y);
			iconOr(g, t.icon(), t.name(), x + 1, y + 1);
			if (inList(mouseX, mouseY) && McGui.over(mouseX, mouseY, x, y, SLOT, SLOT)) {
				g.fill(x + 1, y + 1, x + 17, y + 17, 0x80FFFFFF);
				tooltip(g, t.name(), t.lore(), mouseX, mouseY);
			}
		}
	}

	private void items(GuiGraphicsExtractor g, int mouseX, int mouseY) {
		int cols = Math.min(columns(), ValheimUi.packW);
		int x0 = listLeft() + (listWidth() - cols * SLOT) / 2;
		for (int row = scroll; row < ValheimUi.packH && row < scroll + visibleRows() + 1; row++) {
			for (int col = 0; col < cols; col++) {
				McGui.slot(g, x0 + col * SLOT, listTop() + (row - scroll) * SLOT);
			}
		}
		if (ValheimUi.items.isEmpty()) {
			centred(g, "Nothing here");
			return;
		}
		for (var item : ValheimUi.items) {
			if (item.y() < scroll || item.y() > scroll + visibleRows() || item.x() >= cols) {
				continue;
			}
			int x = x0 + item.x() * SLOT, y = listTop() + (item.y() - scroll) * SLOT;
			if (item.equipped()) {
				g.fill(x + 1, y + 1, x + 17, y + 17, 0x804A7FD0);  // Valheim marks what you have equipped in blue
			}
			iconOr(g, item.icon(), item.name(), x + 1, y + 1);
			if (item.count() > 1) {
				String n = String.valueOf(item.count());
				g.text(font, n, x + 18 - font.width(n), y + 10, WHITE, true);
			}
			if (inList(mouseX, mouseY) && McGui.over(mouseX, mouseY, x, y, SLOT, SLOT)) {
				g.fill(x + 1, y + 1, x + 17, y + 17, 0x80FFFFFF);
				tooltip(g, item.name() + (item.count() > 1 ? " x" + item.count() : ""), item.desc(), mouseX, mouseY,
					(item.quality() > 1 ? "Quality " + item.quality() + "  " : "") + "Weight " + item.weight() + (item.equipped() ? "  (equipped)" : ""),
					item.eat() ? "Right-click to eat or drink" : "");
			}
		}
	}

	private void compendium(GuiGraphicsExtractor g, int mouseX, int mouseY) {
		var list = ValheimUi.texts;
		int x = listLeft(), w = listWidth();
		if (reading >= 0 && reading < list.size()) {
			int y = listTop() - scroll * 9;
			// back to the list
			boolean overBack = inList(mouseX, mouseY) && McGui.over(mouseX, mouseY, x + 1, y + 1, 12, 9);
			g.text(font, "<", x + 3, y + 1, overBack ? 0xFFFFFFA0 : WHITE, true);
			g.text(font, list.get(reading).title(), x + 14, y + 1, 0xFFFFC040, true);
			y += 12;
			if (reading == 0 && !ValheimUi.effects.isEmpty()) {  // the active effects: their icons first
				int ix = x + 2;
				for (var e : ValheimUi.effects) {
					ValheimUi.icon(g, e.icon(), ix, y);
					if (inList(mouseX, mouseY) && McGui.over(mouseX, mouseY, ix, y, 16, 16)) {
						g.setTooltipForNextFrame(font, Component.literal(e.name()), mouseX, mouseY);
					}
					ix += 18;
				}
				y += 18;
			}
			for (var line : readingLines) {
				g.text(font, line, x + 3, y, WHITE, true);
				y += 9;
			}
			return;
		}
		reading = -1;
		if (list.isEmpty()) {
			centred(g, "Nothing read yet");
			return;
		}
		for (int i = scroll; i < list.size() && i < scroll + visibleRows() + 1; i++) {
			int y = listTop() + (i - scroll) * TEXT_ROW;
			boolean over = inList(mouseX, mouseY) && mouseY >= y && mouseY < y + TEXT_ROW;
			if (over) {
				g.fill(x, y, x + w, y + TEXT_ROW, 0x30FFFFFF);
			}
			String t = font.plainSubstrByWidth(list.get(i).title(), w - 6);
			g.text(font, t, x + 3, y + 2, i == 0 ? 0xFFFFC040 : WHITE, true);
		}
	}

	// ---- input -----------------------------------------------------------------------------------

	@Override
	public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
		int t = InventoryTabs.hit(left, top, event.x(), event.y());
		if (t >= 0) {
			if (t != tab) {
				InventoryTabs.open(t);
			}
			return true;
		}
		int trackX = left + LIST_X + LIST_W - TRACK_W;
		if (event.x() >= trackX && event.x() < trackX + TRACK_W && event.y() >= top + LIST_Y && event.y() < top + LIST_Y + LIST_H) {
			dragging = true;
			scrollTo(event.y());
			return true;
		}
		// right-click a food or mead in the Valheim inventory: the Viking eats or drinks it, the Valheim way
		if (tab == InventoryTabs.VALHEIM_ITEMS && event.button() == 1 && inList((int) event.x(), (int) event.y())) {
			int cols = Math.min(columns(), ValheimUi.packW);
			int x0 = listLeft() + (listWidth() - cols * SLOT) / 2;
			int gx = (int) Math.floor((event.x() - x0) / SLOT), gy = scroll + (int) ((event.y() - listTop()) / SLOT);
			for (var item : ValheimUi.items) {
				if (item.x() == gx && item.y() == gy && item.eat()) {
					dev.valcraft.link.ValLink.pushEvent(dev.valcraft.link.Proto.EV_EAT_VALHEIM_ITEM, 0, gx, gy, 0, 0, 0);
					return true;
				}
			}
		}
		if (tab == InventoryTabs.COMPENDIUM && inList((int) event.x(), (int) event.y())) {
			if (reading >= 0) {
				if (event.y() < listTop() - scroll * 9 + 11) {
					reading = -1;
					scroll = 0;
				}
			} else {
				int i = scroll + (int) (event.y() - listTop()) / TEXT_ROW;
				if (i >= 0 && i < ValheimUi.texts.size()) {
					reading = i;
					scroll = 0;
					readingLines = font.split(Component.literal(ValheimUi.texts.get(i).text()), listWidth() - 6);
				}
			}
			return true;
		}
		return super.mouseClicked(event, doubleClick);
	}

	@Override
	public boolean mouseReleased(MouseButtonEvent event) {
		dragging = false;
		return super.mouseReleased(event);
	}

	@Override
	public boolean mouseDragged(MouseButtonEvent event, double dx, double dy) {
		if (dragging) {
			scrollTo(event.y());
			return true;
		}
		return super.mouseDragged(event, dx, dy);
	}

	private void scrollTo(double mouseY) {
		float f = (float) (mouseY - top - LIST_Y - 7.5f) / (LIST_H - 15);
		scroll = Math.clamp(Math.round(f * maxScroll()), 0, maxScroll());
	}

	@Override
	public boolean mouseScrolled(double x, double y, double dx, double dy) {
		scroll = Math.clamp(scroll - (int) Math.signum(dy) * (tab == InventoryTabs.COMPENDIUM && reading >= 0 ? 3 : 1), 0, maxScroll());
		return true;
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		if (minecraft.options.keyInventory.matches(event)) {
			onClose();
			return true;
		}
		return super.keyPressed(event);
	}
}
