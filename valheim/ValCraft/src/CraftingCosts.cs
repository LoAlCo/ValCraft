using HarmonyLib;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Valheim's crafting stations (workbench, forge, ...) with the materials in Minecraft's inventory:
    // Valheim's loot goes to Minecraft (Loot), so the Viking never carries it. A recipe's mapped
    // materials (ValCraft.loot.txt) count from Minecraft too, are taken from there when crafting, and
    // make the recipe known once Minecraft has some, as Valheim's own pickups would. What's crafted
    // goes to Minecraft when it has a counterpart there (keys like the Sealbreaker, Fader's bells, food,
    // materials, and ValCraft's versions of Valheim's armor and weapons); upgrading stays Valheim's.
    public static class CraftingCosts
    {
        static bool Ours(Player player) => Puppet.Puppeting && player == Player.m_localPlayer && Shm.Valid;

        static bool Mapped(Piece.Requirement r, int amount, out string id, out int need)
        {
            need = 0;
            if (!Loot.TryMap(r.m_resItem.gameObject.name, out id, out float per)) return false;
            need = Mathf.CeilToInt(amount * per - 1e-4f);
            return true;
        }

        [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
        static class HaveRecipeItems
        {
            static bool Prefix(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
            {
                if (!Ours(__instance) || piece == null || piece.m_resources == null) return true;
                var station = __instance.GetCurrentCraftingStation();
                var inv = __instance.GetInventory();
                bool any = false;
                foreach (var r in piece.m_resources)
                {
                    if ((!discover && station != null && station.m_upgrader != r.m_upgraderResource) || (station == null && r.m_upgraderResource) || !r.m_resItem) continue;
                    var shared = r.m_resItem.m_itemData.m_shared;
                    bool ok;
                    if (discover)
                    {
                        if (r.m_amount <= 0) continue;
                        ok = __instance.IsMaterialKnown(shared.m_name) || (Mapped(r, 1, out string kid, out _) && BuildTools.McHave(kid) > 0);
                    }
                    else
                    {
                        int need = r.GetAmount(qualityLevel) * amount;
                        int have = 0;
                        for (int q = 1; q <= shared.m_maxQuality; q++) have = Mathf.Max(have, inv.CountItems(shared.m_name, q));
                        ok = have >= need || (Mapped(r, need, out string id, out int mcNeed) && BuildTools.McHave(id) >= mcNeed);
                    }
                    if (piece.m_requireOnlyOneIngredient) { if (ok) { any = true; break; } }
                    else if (!ok) { __result = false; return false; }
                }
                __result = !piece.m_requireOnlyOneIngredient || any;
                return false;
            }
        }

        // Crafting spends what Valheim's inventory has, else the Minecraft counterpart. (Building's own
        // spending is BuildTools': ConsumeResources from a placement.)
        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        [HarmonyPriority(Priority.Low)]
        static class SpendRecipeItems
        {
            static bool Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
            {
                if (!Ours(__instance) || requirements == null || BuildTools.InPlacement) return true;
                var station = __instance.GetCurrentCraftingStation();
                var inv = __instance.GetInventory();
                foreach (var r in requirements)
                {
                    if ((station != null && station.m_upgrader != r.m_upgraderResource) || (station == null && r.m_upgraderResource) || !r.m_resItem) continue;
                    int need = r.GetAmount(qualityLevel) * multiplier;
                    if (need <= 0) continue;
                    string name = r.m_resItem.m_itemData.m_shared.m_name;
                    if (inv.CountItems(name, itemQuality) < need && Mapped(r, need, out string id, out int mcNeed))
                    {
                        BuildTools.Spend(id, mcNeed);
                        Plugin.Log($"craft: {need} x {r.m_resItem.gameObject.name} from Minecraft ({mcNeed} x {id})");
                    }
                    else inv.RemoveItem(name, need, itemQuality);
                }
                return false;
            }
        }

        // The recipe's list: a material Minecraft has enough of isn't shown as missing.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
        static class RecipeListCounts
        {
            static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, bool craft, int quality, int craftMultiplier, bool __result)
            {
                if (!__result || !craft || req == null || !req.m_resItem || !Ours(player)) return;
                int need = req.GetAmount(quality) * craftMultiplier;
                if (player.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name) >= need) return;
                if (!Mapped(req, need, out string id, out int mcNeed) || BuildTools.McHave(id) < mcNeed) return;
                var amount = elementRoot.Find("res_amount")?.GetComponent<TMPro.TMP_Text>();
                if (amount) amount.color = Color.white;
            }
        }

        // What's crafted goes to Minecraft when it has a counterpart there and nothing to upgrade.
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        static class CraftedToMinecraft
        {
            static readonly AccessTools.FieldRef<InventoryGui, Recipe> CraftRecipe = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");

            static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> UpgradeItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");

            static void Prefix(InventoryGui __instance, Player player, out (string name, int before) __state)
            {
                __state = (null, 0);
                var recipe = CraftRecipe(__instance);
                if (!Ours(player) || recipe == null || !recipe.m_item || UpgradeItem(__instance) != null) return;  // upgrades stay in Valheim
                __state = (recipe.m_item.m_itemData.m_shared.m_name, player.GetInventory().CountItems(recipe.m_item.m_itemData.m_shared.m_name));
            }

            static void Postfix(InventoryGui __instance, Player player, (string name, int before) __state)
            {
                if (__state.name == null) return;
                var recipe = CraftRecipe(__instance);
                // Upgradeable gear goes over only if it has a Minecraft version made for it (ValCraft's armor and
                // weapons); a material mapped to a vanilla item stays (Valheim's quality would be lost on nothing).
                if (recipe == null || !recipe.m_item) return;
                if (recipe.m_item.m_itemData.m_shared.m_maxQuality > 1 && !(Loot.TryMap(recipe.m_item.gameObject.name, out string vid, out _) && vid.StartsWith("valcraft:"))) return;
                if (!Loot.TryMap(recipe.m_item.gameObject.name, out string id, out float per)) return;
                int made = player.GetInventory().CountItems(__state.name) - __state.before;
                if (made <= 0) return;
                player.GetInventory().RemoveItem(__state.name, made);
                int count = Mathf.Max(1, Mathf.FloorToInt(made * per + 1e-4f));
                Loot.Give(id, count);
                Plugin.Log($"craft: made {made} x {recipe.m_item.gameObject.name} -> {count} x {id} in Minecraft");
            }
        }
    }
}
