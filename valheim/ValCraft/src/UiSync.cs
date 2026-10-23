using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // ValCraft's Valheim-style UI in Minecraft ([UI] Style): Minecraft draws a health bar and a food
    // bar in place of its hearts and hunger, and tabs in its inventory for Valheim's skills, trophies,
    // compendium and the Valheim-only items. This sends Minecraft what those tabs show
    // (Proto.ColValheimInfo, through the collision ring): a JSON block, then the icons it names as
    // 16x16 Minecraft-style pixel art made from Valheim's own icons.
    public static class UiSync
    {
        public static ConfigEntry<string> Style;
        public static bool ValheimStyle => Style == null || Style.Value != "Minecraft";

        public static void Init(ConfigFile config)
        {
            Style = config.Bind("Valheim Features", "UIStyle", "Valheim", new ConfigDescription(
                "Valheim: a health bar and a food bar in place of Minecraft's hearts and hunger, and tabs in Minecraft's inventory for Valheim's skills, trophies, compendium and Valheim-only items. Minecraft: Minecraft's own hearts and hunger, no tabs.",
                new AcceptableValueList<string>("Valheim", "Minecraft")));
        }

        static float _nextCheck, _nextFull;
        static string _lastJson;
        static readonly HashSet<string> _sentIcons = new HashSet<string>();

        // Main thread, every frame.
        public static void Frame()
        {
            if (!ValheimStyle || !Puppet.Puppeting || !Shm.Valid || !Player.m_localPlayer || !ObjectDB.instance) return;
            float now = Time.unscaledTime;
            if (now < _nextCheck) return;
            _nextCheck = now + 1f;
            var icons = new Dictionary<string, Sprite>();
            string json;
            try { json = Json(Player.m_localPlayer, icons); }
            catch (System.Exception e) { Plugin.Warn("ui sync: " + e.Message); _nextCheck = now + 10f; return; }
            // Everything again now and then: a restarted Minecraft has none of it.
            bool full = now >= _nextFull;
            if (!full && json == _lastJson) return;
            if (full)
            {
                _nextFull = now + 20f;
                _sentIcons.Clear();
            }
            _lastJson = json;
            var send = new List<KeyValuePair<string, byte[]>>();
            foreach (var kv in icons)
            {
                if (_sentIcons.Contains(kv.Key)) continue;
                var px = Icons.Pixel(kv.Value);
                if (px == null) continue;
                send.Add(new KeyValuePair<string, byte[]>(kv.Key, px));
                _sentIcons.Add(kv.Key);
            }
            Icons.ReleaseAtlases();
            Collision.EnqueueRaw(Proto.ColValheimInfo, Pack(json, send));
        }

        static byte[] Pack(string json, List<KeyValuePair<string, byte[]>> icons)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var ms = new System.IO.MemoryStream(body.Length + icons.Count * 1100 + 16);
            var w = new System.IO.BinaryWriter(ms);
            w.Write(body.Length);
            w.Write(body);
            w.Write(icons.Count);
            foreach (var kv in icons)
            {
                var key = Encoding.UTF8.GetBytes(kv.Key);
                w.Write((ushort)key.Length);
                w.Write(key);
                w.Write(kv.Value);  // 16 x 16 RGBA, top row first
            }
            w.Flush();
            return ms.ToArray();
        }

        // ---- what the tabs show -----------------------------------------------------------------

        static string L(string s) => Localization.instance != null ? Localization.instance.Localize(s ?? "") : s ?? "";

        // Valheim's rich text, for Minecraft's plain text: colours become nothing, the rest stays.
        static readonly System.Text.RegularExpressions.Regex Tags = new System.Text.RegularExpressions.Regex("<[^>]*>");
        static string Plain(string s) => Tags.Replace((s ?? "").Replace("\u0016", ""), "").Replace("\r", "").Trim();

        static string Json(Player player, Dictionary<string, Sprite> icons)
        {
            var j = new JsonOut();
            j.Open('{');

            // Skills, as Valheim's skills tab lists them
            var skills = player.GetSkills();
            j.Key("skillTotal").Num(skills.GetTotalSkill()).Key("skillCap").Num(skills.GetTotalSkillCap());
            j.Key("skills").Open('[');
            foreach (var s in skills.GetSkillList())
            {
                string key = "skill:" + s.m_info.m_skill;
                if (s.m_info.m_icon) icons[key] = s.m_info.m_icon;
                j.Open('{').Key("icon").Str(key)
                    .Key("name").Str(L("$skill_" + s.m_info.m_skill.ToString().ToLower()))
                    .Key("desc").Str(Plain(L(s.m_info.m_description)))
                    .Key("level").Num(Mathf.Floor(s.m_level))
                    .Key("bonus").Num(skills.GetSkillLevel(s.m_info.m_skill) - Mathf.Floor(s.m_level))
                    .Key("progress").Num(s.GetLevelPercentage())
                    .Close('}');
            }
            j.Close(']');

            // Trophies
            j.Key("trophies").Open('[');
            foreach (var name in player.GetTrophies())
            {
                var prefab = ObjectDB.instance.GetItemPrefab(name);
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (!drop) continue;
                var shared = drop.m_itemData.m_shared;
                string key = "item:" + name;
                var icon = drop.m_itemData.GetIcon();
                if (icon) icons[key] = icon;
                string title = L(shared.m_name);
                if (title.EndsWith(" trophy")) title = title.Substring(0, title.Length - 7);
                j.Open('{').Key("icon").Str(key).Key("name").Str(title).Key("lore").Str(Plain(L(shared.m_name + "_lore"))).Close('}');
            }
            j.Close(']');

            // Compendium: active effects first, then everything read (runestones, Hugin's tips, ...)
            j.Key("texts").Open('[');
            var effects = new List<StatusEffect>();
            player.GetSEMan().GetHUDStatusEffects(effects);
            var sb = new StringBuilder();
            foreach (var se in effects)
                sb.Append(Plain(L(se.m_name))).Append('\n').Append(Plain(L(se.GetTooltipString()))).Append("\n\n");
            player.GetGuardianPowerHUD(out var power, out _);
            if (power) sb.Append(Plain(L("$inventory_selectedgp"))).Append(": ").Append(Plain(L(power.m_name))).Append('\n').Append(Plain(L(power.GetTooltipString())));
            j.Open('{').Key("title").Str(Plain(L("$inventory_activeeffects"))).Key("text").Str(sb.ToString().Trim()).Close('}');
            foreach (var kv in player.GetKnownTexts())
                j.Open('{').Key("title").Str(Plain(L(kv.Key))).Key("text").Str(Plain(L(kv.Value))).Close('}');
            j.Close(']');

            // The foods eaten (their timers come with every frame, in ValState), for the food bar's icons
            j.Key("foods").Open('[');
            foreach (var f in player.GetFoods())
            {
                if (f.m_item == null) continue;
                string key = "item:" + f.m_name;
                var icon = f.m_item.GetIcon();
                if (icon) icons[key] = icon;
                j.Open('{').Key("hash").Int(Combat.JavaHash(f.m_name ?? "")).Key("icon").Str(key).Key("name").Str(Plain(L(f.m_item.m_shared.m_name)))
                    .Key("health").Num(f.m_item.m_shared.m_food).Key("regen").Num(f.m_item.m_shared.m_foodRegen).Close('}');
            }
            j.Close(']');

            // Valheim's own effects, for the compendium's icons
            j.Key("effects").Open('[');
            foreach (var se in effects)
            {
                if (!se.m_icon) continue;
                string key = "se:" + se.name;
                icons[key] = se.m_icon;
                j.Open('{').Key("icon").Str(key).Key("name").Str(Plain(L(se.m_name))).Close('}');
            }
            j.Close(']');

            // What stays in the Valheim inventory (no Minecraft counterpart), where it sits there
            var inv = player.GetInventory();
            j.Key("packW").Num(inv.GetWidth()).Key("packH").Num(inv.GetHeight())
                .Key("weight").Num(Mathf.Round(inv.GetTotalWeight())).Key("maxWeight").Num(Mathf.Round(player.GetMaxCarryWeight()));
            j.Key("items").Open('[');
            foreach (var item in inv.GetAllItems())
            {
                var shared = item.m_shared;
                string key = "item:" + (item.m_dropPrefab ? item.m_dropPrefab.name : shared.m_name);
                var icon = item.GetIcon();
                if (icon) icons[key] = icon;
                j.Open('{').Key("icon").Str(key).Key("name").Str(Plain(L(shared.m_name)))
                    .Key("desc").Str(Plain(L(shared.m_description)))
                    .Key("x").Num(item.m_gridPos.x).Key("y").Num(item.m_gridPos.y)
                    .Key("count").Num(item.m_stack).Key("quality").Num(item.m_quality)
                    .Key("weight").Num(Mathf.Round(item.GetWeight() * 10f) / 10f)
                    .Key("equipped").Bool(item.m_equipped)
                    .Key("eat").Bool(shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                    .Close('}');
            }
            j.Close(']');

            j.Close('}');
            return j.ToString();
        }

        // A small JSON writer (Valheim ships none we can count on).
        sealed class JsonOut
        {
            readonly StringBuilder _sb = new StringBuilder(4096);
            bool _comma;

            void Sep()
            {
                if (_comma) _sb.Append(',');
                _comma = true;
            }

            public JsonOut Open(char c)
            {
                Sep();
                _sb.Append(c);
                _comma = false;
                return this;
            }

            public JsonOut Close(char c)
            {
                _sb.Append(c);
                _comma = true;
                return this;
            }

            public JsonOut Key(string k)
            {
                Sep();
                Quote(k);
                _sb.Append(':');
                _comma = false;
                return this;
            }

            public JsonOut Str(string s)
            {
                Sep();
                Quote(s ?? "");
                return this;
            }

            public JsonOut Num(float v)
            {
                Sep();
                _sb.Append(float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                return this;
            }

            public JsonOut Int(int v)
            {
                Sep();
                _sb.Append(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return this;
            }

            public JsonOut Bool(bool v)
            {
                Sep();
                _sb.Append(v ? "true" : "false");
                return this;
            }

            void Quote(string s)
            {
                _sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else _sb.Append(c);
                            break;
                    }
                }
                _sb.Append('"');
            }

            public override string ToString() => _sb.ToString();
        }

        // ---- Valheim's icons as Minecraft-style 16x16 pixel art (as tools/pixelize_icons.py) ------

        static class Icons
        {
            const int Inner = 14, Colours = 7;
            const float Cut = 0.5f;
            static readonly Dictionary<Sprite, byte[]> _done = new Dictionary<Sprite, byte[]>();
            static readonly Dictionary<Texture, Color32[]> _atlases = new Dictionary<Texture, Color32[]>();

            // Icons live in sprite atlases that can't be read directly: each atlas is copied once
            // through a render texture, then let go after the batch.
            static Color32[] Atlas(Texture tex)
            {
                if (_atlases.TryGetValue(tex, out var px)) return px;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                px = copy.GetPixels32();
                Object.Destroy(copy);
                _atlases[tex] = px;
                return px;
            }

            public static void ReleaseAtlases() => _atlases.Clear();

            public static byte[] Pixel(Sprite sprite)
            {
                if (_done.TryGetValue(sprite, out var cached)) return cached;
                byte[] result = null;
                try { result = Make(sprite); }
                catch (System.Exception e) { Plugin.Warn("ui sync: icon " + sprite.name + ": " + e.Message); }
                _done[sprite] = result;
                return result;
            }

            static byte[] Make(Sprite sprite)
            {
                var tex = sprite.texture;
                if (!tex) return null;
                var atlas = Atlas(tex);
                var r = sprite.textureRect;
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height), ox = Mathf.RoundToInt(r.x), oy = Mathf.RoundToInt(r.y);
                if (w <= 0 || h <= 0) return null;
                // the icon, top row first
                var src = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int ay = oy + (h - 1 - y), ax = ox + x;
                        src[y * w + x] = ax >= 0 && ay >= 0 && ax < tex.width && ay < tex.height ? atlas[ay * tex.width + ax] : default;
                    }

                // crop to the item, as a square
                int limit = Mathf.Max(60, (int)(Cut * 230));
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int i = 0; i < src.Length; i++)
                    if (src[i].a > limit)
                    {
                        int x = i % w, y = i / w;
                        if (x < x0) x0 = x;
                        if (y < y0) y0 = y;
                        if (x > x1) x1 = x;
                        if (y > y1) y1 = y;
                    }
                if (x1 < 0) return null;
                float size = Mathf.Max(x1 + 1 - x0, y1 + 1 - y0);
                float bx = (x0 + x1 + 1) / 2f - size / 2f, by = (y0 + y1 + 1) / 2f - size / 2f, step = size / Inner;

                // shrink by area, colours weighted by alpha
                var small = new Vector4[Inner * Inner];
                for (int ty = 0; ty < Inner; ty++)
                    for (int tx = 0; tx < Inner; tx++)
                    {
                        float sx0 = bx + tx * step, sy0 = by + ty * step, sx1 = sx0 + step, sy1 = sy0 + step;
                        float cr = 0, cg = 0, cb = 0, ca = 0, area = 0;
                        for (int y = (int)Mathf.Floor(sy0); y < sy1; y++)
                        {
                            float oyw = Mathf.Min(sy1, y + 1) - Mathf.Max(sy0, y);
                            if (oyw <= 0) continue;
                            for (int x = (int)Mathf.Floor(sx0); x < sx1; x++)
                            {
                                float oxw = Mathf.Min(sx1, x + 1) - Mathf.Max(sx0, x);
                                if (oxw <= 0) continue;
                                float wgt = oxw * oyw;
                                area += wgt;
                                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                                var p = src[y * w + x];
                                float al = p.a / 255f * wgt;
                                cr += p.r * al; cg += p.g * al; cb += p.b * al; ca += al;
                            }
                        }
                        small[ty * Inner + tx] = ca > 0 ? new Vector4(cr / ca, cg / ca, cb / ca, ca / area) : Vector4.zero;
                    }

                // solid where at least half covered; a few colours; a dark outline like Minecraft's items
                var solid = new Dictionary<int, Vector3>();
                for (int i = 0; i < small.Length; i++)
                    if (small[i].w >= Cut) solid[(i / Inner + 1) * 16 + (i % Inner + 1)] = new Vector3(small[i].x, small[i].y, small[i].z);
                if (solid.Count == 0) return null;
                var palette = Quantize(new List<Vector3>(solid.Values), Colours);
                var darkest = palette[0];
                foreach (var c in palette) if (c.x + c.y + c.z < darkest.x + darkest.y + darkest.z) darkest = c;
                var outPx = new byte[16 * 16 * 4];
                foreach (var kv in solid) Put(outPx, kv.Key, Punch(Nearest(kv.Value, palette)));
                var edge = darkest * 0.35f;
                foreach (var pos in solid.Keys)
                {
                    int x = pos % 16, y = pos / 16;
                    foreach (var n in new[] { pos + 1, pos - 1, pos + 16, pos - 16 })
                    {
                        int nx = n % 16, ny = n / 16;
                        if (n < 0 || n >= 256 || System.Math.Abs(nx - x) + System.Math.Abs(ny - y) != 1 || solid.ContainsKey(n)) continue;
                        Put(outPx, n, edge);
                    }
                }
                return outPx;
            }

            static void Put(byte[] px, int pos, Vector3 c)
            {
                px[pos * 4] = (byte)Mathf.Clamp(c.x, 0, 255);
                px[pos * 4 + 1] = (byte)Mathf.Clamp(c.y, 0, 255);
                px[pos * 4 + 2] = (byte)Mathf.Clamp(c.z, 0, 255);
                px[pos * 4 + 3] = 255;
            }

            // a touch more contrast and saturation than the soft render, like Minecraft's flat shading
            static Vector3 Punch(Vector3 c)
            {
                float grey = (c.x + c.y + c.z) / 3f;
                float F(float v) => Mathf.Clamp((grey + (v - grey) * 1.15f - 128f) * 1.08f + 128f, 0, 255);
                return new Vector3(F(c.x), F(c.y), F(c.z));
            }

            static Vector3 Nearest(Vector3 c, List<Vector3> palette)
            {
                var best = palette[0];
                float bestD = float.MaxValue;
                foreach (var p in palette)
                {
                    float d = (c - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }

            // median cut
            static List<Vector3> Quantize(List<Vector3> colours, int n)
            {
                var boxes = new List<List<Vector3>> { colours };
                while (boxes.Count < n)
                {
                    int pick = -1;
                    float widest = -1;
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        if (boxes[i].Count < 2) continue;
                        float range = Range(boxes[i], out _);
                        if (range > widest) { widest = range; pick = i; }
                    }
                    if (pick < 0) break;
                    var box = boxes[pick];
                    boxes.RemoveAt(pick);
                    Range(box, out int ch);
                    box.Sort((a, b) => a[ch].CompareTo(b[ch]));
                    int mid = box.Count / 2;
                    boxes.Add(box.GetRange(0, mid));
                    boxes.Add(box.GetRange(mid, box.Count - mid));
                }
                var palette = new List<Vector3>();
                foreach (var b in boxes)
                {
                    if (b.Count == 0) continue;
                    var sum = Vector3.zero;
                    foreach (var c in b) sum += c;
                    palette.Add(sum / b.Count);
                }
                return palette;
            }

            static float Range(List<Vector3> box, out int channel)
            {
                channel = 0;
                float best = -1;
                for (int i = 0; i < 3; i++)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var c in box) { lo = Mathf.Min(lo, c[i]); hi = Mathf.Max(hi, c[i]); }
                    if (hi - lo > best) { best = hi - lo; channel = i; }
                }
                return best;
            }
        }
    }
}
