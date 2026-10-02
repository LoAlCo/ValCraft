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

        static ConfigEntry<bool> _export;
        static ConfigEntry<string> _list;

        public static void Init(ConfigFile config)
        {
            _export = config.Bind("Debug", "ExportIcons", false,
                "Save Valheim's icons for the items in ExportIconsList as PNGs in BepInEx/ValCraft icons (for making ValCraft's Minecraft versions of them). Switches itself off when done.");
            _list = config.Bind("Debug", "ExportIconsList", DefaultList, "Item prefab names to export, comma-separated; * for every item.");
        }

        public static void Frame()
        {
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
