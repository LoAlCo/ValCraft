using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValCraft
{
    // For making ValCraft's Minecraft versions of Valheim items: saves Valheim's own item icons as
    // PNGs (BepInEx/ValCraft icons/<prefab>.png), the reference they're redrawn from. Turn on
    // [Debug] ExportIcons in game (it switches itself back off once done).
    public static class IconExport
    {
        const string DefaultList =
            "TrophyDeer,HardAntler,TrophyEikthyr," +
            "AncientSeed,CryptKey,TrophyTheElder," +
            "WitheredBone,Wishbone,TrophyBonemass," +
            "DragonEgg,DragonTear,TrophyDragonQueen," +
            "GoblinTotem,YagluthDrop,TrophyGoblinKing," +
            "DvergrKey,DvergrKeyFragment,QueenDrop,TrophySeekerQueen," +
            "Bell,BellFragment,FaderDrop,TrophyFader";

        static ConfigEntry<bool> _export, _exportData;
        static ConfigEntry<string> _list;

        public static void Init(ConfigFile config)
        {
            _export = config.Bind("Debug", "ExportIcons", false,
                "Save Valheim's icons for the items in ExportIconsList as PNGs in BepInEx/ValCraft icons (for making ValCraft's Minecraft versions of them). Switches itself off when done.");
            _list = config.Bind("Debug", "ExportIconsList", DefaultList, "Item prefab names to export, comma-separated; * for every item.");
            _exportData = config.Bind("Debug", "ExportData", false,
                "Save every item's stats, every recipe and every creature as tables in BepInEx/ValCraft icons (items.tsv, recipes.tsv, creatures.tsv), for making ValCraft's Minecraft versions of them. Switches itself off when done.");
        }

        public static void Frame()
        {
            if (_exportData != null && _exportData.Value && ObjectDB.instance && ObjectDB.instance.m_items.Count > 0 && ZNetScene.instance)
            {
                _exportData.Value = false;
                try { ExportData(); } catch (Exception e) { Plugin.Warn($"data export: {e}"); }
            }
            if (_export == null || !_export.Value || !ObjectDB.instance || ObjectDB.instance.m_items.Count == 0) return;
            _export.Value = false;
            string dir = Path.Combine(Paths.BepInExRootPath, "ValCraft icons");
            Directory.CreateDirectory(dir);
            int saved = 0;
            var names = new System.Text.StringBuilder();
            bool all = _list.Value.Trim() == "*";
            var wanted = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in _list.Value.Split(',')) if (n.Trim().Length > 0) wanted.Add(n.Trim());
            foreach (var go in ObjectDB.instance.m_items)
            {
                if (!go || (!all && !wanted.Contains(go.name))) continue;
                var drop = go.GetComponent<ItemDrop>();
                // Some items have no icon at all (GetIcon throws for those): skip them.
                var icons = drop ? drop.m_itemData.m_shared.m_icons : null;
                var icon = icons != null && icons.Length > 0 ? drop.m_itemData.GetIcon() : null;
                if (!icon) { if (!all) Plugin.Warn($"icon export: {go.name} has no icon"); continue; }
                try
                {
                    File.WriteAllBytes(Path.Combine(dir, go.name + ".png"), Png(icon));
                    saved++;
                    wanted.Remove(go.name);
                    names.Append(go.name).Append(" = ").Append(Localization.instance.Localize(drop.m_itemData.m_shared.m_name)).Append('\n');
                }
                catch (Exception e) { Plugin.Warn($"icon export: {go.name}: {e.Message}"); }
            }
            if (!all) foreach (var missing in wanted) Plugin.Warn($"icon export: no item called {missing}");
            File.WriteAllText(Path.Combine(dir, "names.txt"), names.ToString());  // prefab = English name
            Plugin.Message($"ValCraft: saved {saved} Valheim icons to {dir}");
        }

        const string T = "\t";

        static string D(HitData.DamageTypes d) =>
            string.Join(T, d.m_blunt, d.m_slash, d.m_pierce, d.m_chop, d.m_pickaxe, d.m_fire, d.m_frost, d.m_lightning, d.m_poison, d.m_spirit);

        static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        static string Row(params object[] cells) => string.Join(T, cells) + "\n";

        static void ExportData()
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "ValCraft icons");
            Directory.CreateDirectory(dir);
            string dmg = string.Join(T, "blunt", "slash", "pierce", "chop", "pickaxe", "fire", "frost", "lightning", "poison", "spirit");
            var items = new System.Text.StringBuilder();
            items.Append(Row("prefab", "name", "type", "stack", "maxQuality", "weight", "teleportable", "skill", "toolTier", "durability", "durabilityPerLevel",
                "armor", "armorPerLevel", "blockPower", "set", "food", "foodStamina", "foodEitr", "foodBurnTime", "foodRegen", "drink", "ammoType",
                "attackStamina", "attackForce", "value", dmg, dmg.Replace(T, "PerLevel" + T) + "PerLevel", "description"));
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go ? go.GetComponent<ItemDrop>() : null;
                if (!drop) continue;
                var s = drop.m_itemData.m_shared;
                items.Append(Row(go.name, Clean(Localization.instance.Localize(s.m_name)), s.m_itemType, s.m_maxStackSize, s.m_maxQuality, s.m_weight,
                    s.m_teleportable, s.m_skillType, s.m_toolTier, s.m_useDurability ? s.m_maxDurability : 0f, s.m_durabilityPerLevel,
                    s.m_armor, s.m_armorPerLevel, s.m_blockPower, Clean(s.m_setName), s.m_food, s.m_foodStamina, s.m_foodEitr, s.m_foodBurnTime,
                    s.m_foodRegen, s.m_isDrink, Clean(s.m_ammoType), s.m_attack != null ? s.m_attack.m_attackStamina : 0f, s.m_attackForce, s.m_value,
                    D(s.m_damages), D(s.m_damagesPerLevel), Clean(Localization.instance.Localize(s.m_description))));
            }
            File.WriteAllText(Path.Combine(dir, "items.tsv"), items.ToString());

            var recipes = new System.Text.StringBuilder(Row("item", "amount", "station", "stationLevel", "enabled", "resources (name x amount +perLevel)"));
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (!r || !r.m_item) continue;
                var res = new System.Text.StringBuilder();
                foreach (var req in r.m_resources)
                    if (req.m_resItem) res.Append(req.m_resItem.name).Append(" x ").Append(req.m_amount).Append(" +").Append(req.m_amountPerLevel).Append(", ");
                recipes.Append(Row(r.m_item.name, r.m_amount, r.m_craftingStation ? r.m_craftingStation.name : "", r.m_minStationLevel, r.m_enabled, res));
            }
            File.WriteAllText(Path.Combine(dir, "recipes.tsv"), recipes.ToString());

            var creatures = new System.Text.StringBuilder(Row("prefab", "name", "health", "boss", "faction", "ai", "tameable", "flying", "drops"));
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                var c = go ? go.GetComponent<Character>() : null;
                if (!c || c is Player) continue;
                string ai = go.GetComponent<MonsterAI>() ? "Monster" : go.GetComponent<AnimalAI>() ? "Animal" : go.GetComponent<BaseAI>() ? "Base" : "";
                var drops = new System.Text.StringBuilder();
                var cd = go.GetComponent<CharacterDrop>();
                if (cd)
                    foreach (var d in cd.m_drops)
                        if (d.m_prefab) drops.Append(d.m_prefab.name).Append(' ').Append(d.m_amountMin).Append('-').Append(d.m_amountMax).Append(" @").Append(d.m_chance).Append(", ");
                creatures.Append(Row(go.name, Clean(Localization.instance.Localize(c.m_name)), c.m_health, c.IsBoss(), c.m_faction, ai,
                    (bool)go.GetComponent<Tameable>(), c.m_flying, drops));
            }
            File.WriteAllText(Path.Combine(dir, "creatures.tsv"), creatures.ToString());
            Plugin.Message($"ValCraft: saved item, recipe and creature tables to {dir}");
        }

        // Icons live in sprite atlases that can't be read directly: copy through a render texture.
        static byte[] Png(Sprite sprite)
        {
            var tex = sprite.texture;
            var rect = sprite.textureRect;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var outTex = new Texture2D(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height), TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
            outTex.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = outTex.EncodeToPNG();
            UnityEngine.Object.Destroy(outTex);
            return png;
        }
    }
}
