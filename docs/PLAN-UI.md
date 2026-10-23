# Plan: Valheim stats in Minecraft's UI, and a UI rework

Status: Part 1 built 2026-10-20 (health bar, food bar, inventory tabs; [UI] Style), untested. Written 2026-10-13, after 0.7.0.

## Where things stand

- While Minecraft drives, Valheim hides its health, stamina, eitr, adrenaline and food bars,
  its hotbar and its crosshair (`Patches.cs` HudPatch). Minecraft's HUD (hearts, hunger, armor, XP,
  hotbar) is drawn by Minecraft and shown over Valheim through the overlay (triple-buffered
  pixels in shared memory, `OverlayCtl`).
- So the player can't see Valheim's stamina, eitr, food slots, rested/comfort, status effects
  (wet, cold, poisoned, meads, set bonuses later), skills or carry weight.
- Shown already: Valheim's boss bars (BossBars), its hover prompts (with G for use), its messages.

## Part 1: Valheim stats in Minecraft's UI

Valheim sends the numbers, Minecraft draws them in its own style (so the HUD stays one HUD and
scales with Minecraft's GUI scale).

1. **A stats block in shared memory** (Valheim -> MC, new `kOffValStats`, seqlock like ValState):
   health/max, stamina/max, eitr/max (0 = no eitr yet), adrenaline/max, the three food slots
   (item id, time left, health/stamina/eitr given), rested/comfort level and time,
   up to ~16 status effects (id, time left, stacks, icon key), carry weight / max, and
   a skills table on request (32 skills x level and progress). ~1 KB; written a few times a second.
2. **Hearts:** decide how Valheim's health maps to Minecraft's hearts. Options: keep Minecraft's
   hearts as the truth (today), or show Valheim's health bar in Valheim's red bar style.
   Decision for the user.
3. **Stamina bar:** a yellow Valheim-style bar above the hotbar (or replacing the hunger row,
   since Valheim's food is not Minecraft's hunger). Flashes when out of stamina, like Valheim's.
4. **Eitr bar:** purple, only once the player has eitr (Mistlands food).
5. **Food slots:** three small food icons with their timers (Valheim's food icons are already
   ValCraft items for many foods; others need icons — ties in with the 176 unmapped materials).
6. **Status effects:** a row of icons with timers, top right like Valheim (Rested, Wet, Cold,
   Freezing, Poisoned, Burning, Shelter, meads, Forsaken power and cooldown). Icons drawn from
   Valheim's (the IconExport pipeline already exports them).
7. **Skills:** a Minecraft screen (key, or a tab in the inventory) listing Valheim's skills with
   levels and progress bars, the way Valheim's skills tab shows them.
8. **Weight:** carry weight next to the inventory (Valheim's weight still matters for the hidden
   Valheim inventory and any synced items).

## Part 2: UI rework in general (ideas, to pick from)

- **One look:** restyle Minecraft's HUD pieces ValCraft adds (and maybe the vanilla ones via a
  resource pack) with Valheim's colours, frames and font, so it reads as one game.
- **Settings:** ValCraft settings menu — tabs per section instead of one long list, tooltips with
  the full description, "reset to default" per setting, a search box.
- **Notifications:** Valheim-style centre messages for ValCraft events (loot picked up into
  Minecraft, boss summoned, skill up) instead of chat lines.
- **Inventory:** show which items are Valheim items, and their Valheim stats (armor, damage,
  upgrade level) in the tooltip. Pairs with the on-hold inventory sync.
- **Crafting:** at a Valheim station, mark which recipe materials come from the Minecraft
  inventory.
- **Map / compass:** a small compass or minimap pin for the Minecraft world (beds, lodestones).
- **Performance:** keep the overlay cheap — draw stats in Minecraft's GUI pass (already composited),
  no extra textures per frame; update the stats block at ~5 Hz, not every frame.

## Open questions for the user

- Hearts: Minecraft's hearts or Valheim's health bar?
- Hunger: keep Minecraft's hunger, or replace it with Valheim's food slots?
- Valheim look for Minecraft's whole HUD, or only for the new Valheim parts?
- Skills screen: its own key, or a tab in the inventory?
