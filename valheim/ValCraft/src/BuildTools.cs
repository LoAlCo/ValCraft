using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Valheim's build tools from Minecraft. While a Minecraft hoe or the Build Hammer (a ValCraft
    // item) is in Minecraft's main hand, the Viking quietly holds a real Valheim Hoe or Hammer, so
    // Valheim's own build mode runs: the ghost piece, right-click for its menu, left-click to place,
    // middle-click to remove (hammer), Alt + mouse wheel to rotate. Costs come out of Minecraft's
    // inventory through the loot table in reverse (Wood = oak logs, Stone = cobblestone, ...);
    // anything the table doesn't map comes from the Valheim inventory as usual. Creative is free.
    // The tool is ValCraft's (tagged) and leaves the Valheim inventory again when it's put away.
    public static class BuildTools
    {
        const string Tag = "valcraft.tool";

        public static bool Active;
        public static bool Hammer => Active && _prefab == "Hammer";
        static string _prefab;
        static ItemDrop.ItemData _tool;
        static ItemDrop.ItemData _prevRight, _prevLeft;
        static Player _cleaned;
        static bool _inPlacement;

        // Minecraft's inventory (REN_INVENTORY) and what's been spent here but not yet taken there.
        static readonly Dictionary<string, int> _mcItems = new Dictionary<string, int>();
        static readonly Dictionary<string, int> _pending = new Dictionary<string, int>();

        static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
        static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
        static readonly AccessTools.FieldRef<Player, Dictionary<string, int>> KnownStations = AccessTools.FieldRefAccess<Player, Dictionary<string, int>>("m_knownStations");

        public static bool Creative => (Puppet.Mc.flags & Proto.McCreative) != 0;

        public static void Frame()
        {
            var player = Player.m_localPlayer;
            if (player && _cleaned != player)
            {
                _cleaned = player;
                RemoveLeftovers(player);  // from a crash or quit while one was held
            }
            string want = null;
            // Not in block terrain (F8): the ground is Minecraft's blocks there (its hoe tills them).
            if (player && Puppet.MinecraftOwnsPlayer && !Plugin.Paused && !player.IsDead() && !BlockTerrain.On && (Puppet.Mc.flags & Proto.McInWorld) != 0)
                want = (Puppet.Mc.flags & Proto.McHoldingHammer) != 0 ? "Hammer" : (Puppet.Mc.flags & Proto.McHoldingHoe) != 0 ? "Hoe" : null;
            if (Active && want != _prefab) Exit(player);
            if (!Active && want != null) Enter(player, want);
            if (!Active) return;
            if (RightItem(player) != _tool)
            {
                Exit(player);  // something else got equipped
                return;
            }
            // Valheim's own bars are hidden while Minecraft drives (Minecraft has hunger): no stamina
            // or durability limits on the tool.
            player.AddStamina(player.GetMaxStamina());
            _tool.m_durability = _tool.GetMaxDurability();
        }

        static void Enter(Player player, string prefabName)
        {
            var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!drop) return;
            var inv = player.GetInventory();
            var item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            item.m_durability = item.GetMaxDurability();
            item.m_customData[Tag] = "1";
            if (!inv.AddItem(item))
            {
                Plugin.Message("ValCraft: no room in the Valheim inventory for the " + prefabName.ToLowerInvariant());
                return;
            }
            _prevRight = RightItem(player);
            _prevLeft = LeftItem(player);
            // Active before equipping: equipping builds the piece menu, and AllUnlocked has to apply.
            _tool = item;
            _prefab = prefabName;
            Active = true;
            if (!player.EquipItem(item, false))
            {
                Active = false;
                _tool = null;
                _prefab = null;
                inv.RemoveItem(item);  // can't right now (mid-swing, swimming): try again next frame
                return;
            }
            UpdatePieces(player);
            if (prefabName == "Hammer" && !_toldExperimental)
            {
                _toldExperimental = true;
                Plugin.Message("ValCraft: Valheim building with the Build Hammer is experimental");
            }
        }

        static bool _toldExperimental;

        static readonly System.Reflection.MethodInfo UpdateAvailablePiecesList = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

        static void UpdatePieces(Player player)
        {
            try { UpdateAvailablePiecesList?.Invoke(player, null); }
            catch (Exception e) { Plugin.Warn("build menu refresh: " + e.Message); }
        }

        static void Exit(Player player)
        {
            Active = false;
            if (Hud.IsPieceSelectionVisible()) Hud.HidePieceSelection();
            if (_tool != null && player)
            {
                player.UnequipItem(_tool, false);
                var inv = player.GetInventory();
                inv.RemoveItem(_tool);
                if (_prevRight != null && inv.ContainsItem(_prevRight)) player.EquipItem(_prevRight, false);
                if (_prevLeft != null && inv.ContainsItem(_prevLeft)) player.EquipItem(_prevLeft, false);
            }
            _tool = _prevRight = _prevLeft = null;
            _prefab = null;
        }

        static void RemoveLeftovers(Player player)
        {
            var inv = player.GetInventory();
            foreach (var item in inv.GetAllItems().ToArray())
                if (item.m_customData != null && (item.m_customData.ContainsKey(Tag) || item.m_customData.ContainsKey("valcraft.hoe")))
                {
                    player.UnequipItem(item, false);
                    inv.RemoveItem(item);
                }
        }

        // ---- Minecraft's inventory --------------------------------------------------------------

        // REN_INVENTORY: int entries, then per entry: int count, int id length, UTF-8 id.
        public static unsafe void OnInventory(byte* p, uint bytes)
        {
            if (bytes < 4) return;
            int n = *(int*)p;
            uint at = 4;
            _mcItems.Clear();
            for (int i = 0; i < n && at + 8 <= bytes; i++)
            {
                int count = *(int*)(p + at), len = *(int*)(p + at + 4);
                at += 8;
                if (len < 0 || at + (uint)len > bytes) break;
                _mcItems[Encoding.UTF8.GetString(p + at, len)] = count;
                at += (uint)len;
            }
            _pending.Clear();  // Minecraft's inventory caught up with what was spent
        }

        static readonly HashSet<string> _unmapped = new HashSet<string>();

        // How much of a Minecraft item the player carries (less what's been spent and not yet taken).
        public static int McCount(string id) => McHave(id);

        // Something taken from Minecraft outside building (an offering): counted as spent until
        // Minecraft's inventory catches up.
        public static void Spent(string id, int count)
        {
            _pending.TryGetValue(id, out int spent);
            _pending[id] = spent + count;
        }

        static int McHave(string id)
        {
            _mcItems.TryGetValue(id, out int have);
            _pending.TryGetValue(id, out int spent);
            return Math.Max(0, have - spent);
        }

        // A Valheim cost in Minecraft items, or false when the loot table doesn't map that material.
        static bool McCost(Piece.Requirement r, int multiplier, out string id, out int count)
        {
            count = 0;
            if (!Loot.TryMap(r.m_resItem.gameObject.name, out id, out float per))
            {
                if (_unmapped.Add(r.m_resItem.gameObject.name))
                    Plugin.Log($"build: no Minecraft item for {r.m_resItem.gameObject.name} (add a line to ValCraft.loot.txt); it comes from the Valheim inventory");
                return false;
            }
            count = Mathf.CeilToInt(r.m_amount * multiplier * per - 1e-4f);
            return true;
        }

        static bool IsToolPiece(Player player, Piece piece)
        {
            return Active && player == Player.m_localPlayer && _tool != null && _tool.m_shared.m_buildPieces && piece &&
                   _tool.m_shared.m_buildPieces.m_pieces.Contains(piece.gameObject);
        }

        // Player.HaveRequirements(Piece, mode) for the tool's pieces, with mapped materials counted in
        // Minecraft's inventory (and known once Minecraft has some, so new pieces unlock as in Valheim).
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
        static class HaveRequirementsPatch
        {
            static bool Prefix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
            {
                if (!IsToolPiece(__instance, piece)) return true;
                if (mode == Player.RequirementMode.IsKnown) return true;  // Valheim's own (it teaches the Viking for good)
                __result = Have(__instance, piece, mode);
                return false;
            }

            static bool Have(Player player, Piece piece, Player.RequirementMode mode)
            {
                if (piece.m_craftingStation)
                {
                    if (mode == Player.RequirementMode.IsKnown || mode == Player.RequirementMode.CanAlmostBuild)
                    {
                        if (!KnownStations(player).ContainsKey(piece.m_craftingStation.m_name)) return false;
                    }
                    else if (!CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position) &&
                             !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
                        return false;
                }
                if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc)) return false;
                bool free = Creative || (mode != Player.RequirementMode.IsKnown && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()));
                var inv = player.GetInventory();
                foreach (var r in piece.m_resources)
                {
                    if (!r.m_resItem || r.m_amount <= 0) continue;
                    string name = r.m_resItem.m_itemData.m_shared.m_name;
                    bool mapped = McCost(r, 1, out string id, out int need);
                    switch (mode)
                    {
                        case Player.RequirementMode.IsKnown:
                            break;  // everything is unlocked for now (see AllUnlocked)
                        case Player.RequirementMode.CanAlmostBuild:
                            if (free) break;
                            if (mapped ? McHave(id) <= 0 : !inv.HaveItem(name)) return false;
                            break;
                        case Player.RequirementMode.CanBuild:
                            if (free) break;
                            if (mapped ? McHave(id) < need : inv.CountItems(name) < r.m_amount) return false;
                            break;
                    }
                }
                return true;
            }
        }

        // Placing a piece: mapped materials are taken from Minecraft, the rest from Valheim.
        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        static class ConsumeResourcesPatch
        {
            static bool Prefix(Player __instance, Piece.Requirement[] requirements, int multiplier)
            {
                if (!_inPlacement || !Active || __instance != Player.m_localPlayer || requirements == null) return true;
                if (Creative) return false;
                var inv = __instance.GetInventory();
                foreach (var r in requirements)
                {
                    if (!r.m_resItem || r.m_amount <= 0) continue;
                    if (McCost(r, multiplier, out string id, out int count))
                    {
                        if (count <= 0) continue;
                        _pending.TryGetValue(id, out int spent);
                        _pending[id] = spent + count;
                        Loot.Take(id, count);
                    }
                    else inv.RemoveItem(r.m_resItem.m_itemData.m_shared.m_name, r.m_amount * multiplier);
                }
                return false;
            }
        }

        // For now every piece of the tool is in its menu, whatever materials have been found: the
        // Viking never picks materials up (they go to Minecraft), so Valheim's unlocking can't work.
        // Only while ValCraft's tool is out; the Viking's own known pieces stay as they were.
        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        static class AllUnlocked
        {
            static readonly HashSet<string> _all = new HashSet<string>();

            static void Prefix(PieceTable __instance, ref HashSet<string> knownRecipies, ref bool hideUnavailable)
            {
                if (!Active || _tool == null || __instance != _tool.m_shared.m_buildPieces) return;
                _all.Clear();
                foreach (var go in __instance.m_pieces)
                {
                    var piece = go ? go.GetComponent<Piece>() : null;
                    if (piece) _all.Add(piece.m_name);
                }
                knownRecipies = _all;
                hideUnavailable = false;
            }
        }

        // The menu's cost list in Minecraft items, with Minecraft's icons: "Oak Log 10" for Wood x10,
        // flashing red when Minecraft's inventory is short.
        [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
        static class CostsInMinecraftItems
        {
            static void Postfix(Hud __instance, Piece piece)
            {
                if (!Active || !piece || __instance.m_requirementItems == null) return;
                for (int j = 0; j < piece.m_resources.Length && j < __instance.m_requirementItems.Length; j++)
                {
                    var r = piece.m_resources[j];
                    if (!r.m_resItem || r.m_amount <= 0 || !McCost(r, 1, out string id, out int need)) continue;
                    var root = __instance.m_requirementItems[j].transform;
                    var name = root.Find("res_name")?.GetComponent<TMPro.TMP_Text>();
                    var amount = root.Find("res_amount")?.GetComponent<TMPro.TMP_Text>();
                    var tip = root.GetComponent<UITooltip>();
                    var icon = root.Find("res_icon")?.GetComponent<UnityEngine.UI.Image>();
                    var sprite = McIcon(id);
                    if (icon && sprite) { icon.sprite = sprite; icon.color = Color.white; }
                    string label = McName(id);
                    if (name) name.text = label;
                    if (tip) tip.m_text = label;
                    if (amount)
                    {
                        amount.text = need.ToString();
                        amount.color = !Creative && McHave(id) < need && Mathf.Sin(Time.time * 10f) > 0f ? Color.red : Color.white;
                    }
                }
            }
        }

        // ---- Minecraft item icons for the cost list ------------------------------------------------

        static readonly Dictionary<string, Vector4> _iconUv = new Dictionary<string, Vector4>();
        static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        // REN_ITEM_ICONS: int entries, then per entry: int id length, UTF-8 id, u0 v0 u1 v1 (atlas UV,
        // v down from the top as in Minecraft).
        public static unsafe void OnItemIcons(byte* p, uint bytes)
        {
            if (bytes < 4) return;
            foreach (var s in _icons.Values) if (s) { UnityEngine.Object.Destroy(s.texture); UnityEngine.Object.Destroy(s); }
            _icons.Clear();
            _iconUv.Clear();
            int n = *(int*)p;
            uint at = 4;
            for (int i = 0; i < n && at + 4 <= bytes; i++)
            {
                int len = *(int*)(p + at);
                at += 4;
                if (len < 0 || at + (uint)len + 16 > bytes) break;
                string id = Encoding.UTF8.GetString(p + at, len);
                at += (uint)len;
                float* f = (float*)(p + at);
                _iconUv[id] = new Vector4(f[0], f[1], f[2], f[3]);
                at += 16;
            }
        }

        // The item's icon as its own small sprite, cut out of the atlas Valheim already has.
        static Sprite McIcon(string id)
        {
            if (_icons.TryGetValue(id, out var cached)) return cached;
            Sprite sprite = null;
            var atlas = WorldRender.Atlas;
            if (atlas && _iconUv.TryGetValue(id, out var uv))
            {
                int x0 = Mathf.RoundToInt(uv.x * atlas.width), y0 = Mathf.RoundToInt(uv.y * atlas.height);
                int w = Mathf.RoundToInt((uv.z - uv.x) * atlas.width), h = Mathf.RoundToInt((uv.w - uv.y) * atlas.height);
                if (w > 0 && h > 0 && w <= 512 && h <= 512 && x0 >= 0 && y0 >= 0 && x0 + w <= atlas.width && y0 + h <= atlas.height)
                {
                    var src = atlas.GetPixelData<Color32>(0);
                    var px = new Color32[w * h];
                    // The atlas holds Minecraft's rows top first; a texture's rows go up from the bottom.
                    for (int row = 0; row < h; row++)
                        for (int col = 0; col < w; col++)
                            px[(h - 1 - row) * w + col] = src[(y0 + row) * atlas.width + x0 + col];
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "ValCraft icon " + id };
                    tex.SetPixels32(px);
                    tex.Apply(false, true);
                    sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
                }
            }
            _icons[id] = sprite;
            return sprite;
        }

        // "minecraft:oak_log" -> "Oak Log"
        public static string McName(string id)
        {
            int colon = id.IndexOf(':');
            var words = (colon >= 0 ? id.Substring(colon + 1) : id).Split('_');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        // ConsumeResources is also crafting's: only placements count as building.
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        static class PlacementScope
        {
            static void Prefix() => _inPlacement = true;
            static void Finalizer() => _inPlacement = false;
        }
    }
}
