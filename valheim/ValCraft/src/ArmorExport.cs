using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValCraft
{
    // [Debug] ExportArmor: puts each Valheim armor piece on the Viking in turn and samples it as worn,
    // for drawing ValCraft's Minecraft versions of the sets: coloured surface points (with which way
    // the surface faces) in the player's own space, and where the skeleton's joints are, in
    // BepInEx/ValCraft armor/<prefab>.txt. Chest and leg armor is partly painted onto the body itself
    // (its chest and legs textures): those parts of the body are sampled through the armor's texture.
    // The Viking's own gear goes back on afterwards. Switches itself off when done.
    public static class ArmorExport
    {
        static ConfigEntry<bool> _export;
        static ConfigEntry<string> _list;
        static readonly List<GameObject> _queue = new List<GameObject>();
        static int _step, _wait, _saved;
        static string _dir;
        const int Points = 60000;

        static readonly FieldInfo HelmetInstance = AccessTools("m_helmetItemInstance");
        static readonly FieldInfo ChestInstances = AccessTools("m_chestItemInstances");
        static readonly FieldInfo LegInstances = AccessTools("m_legItemInstances");
        static readonly FieldInfo ShoulderInstances = AccessTools("m_shoulderItemInstances");
        static FieldInfo AccessTools(string name) => HarmonyLib.AccessTools.Field(typeof(VisEquipment), name);

        public static void Init(ConfigFile config)
        {
            _export = config.Bind("Debug", "ExportArmor", false,
                "Sample every Valheim armor piece as worn (on the Viking) into BepInEx/ValCraft armor, for drawing ValCraft's Minecraft armor. Switches itself off when done.");
            _list = config.Bind("Debug", "ExportArmorList", "armor", "Armor prefabs to sample, comma-separated; armor for every helmet, chest, legs and cape.");
        }

        // Main thread, every frame.
        public static void Frame()
        {
            var player = Player.m_localPlayer;
            if (_export == null || !_export.Value || !player || !ObjectDB.instance) return;
            var vis = player.GetComponent<VisEquipment>();
            if (!vis) return;
            try
            {
                if (_step == 0) Start();
                if (_wait-- > 0) return;
                if (_step > 0 && _step <= _queue.Count) Sample(player, vis, _queue[_step - 1]);
                if (_step < _queue.Count)
                {
                    Dress(vis, _queue[_step]);
                    _step++;
                    _wait = 3;  // Valheim attaches the new piece in its LateUpdate
                    return;
                }
                Finish(player, vis);
            }
            catch (Exception e)
            {
                Plugin.Warn("armor export: " + e);
                Finish(player, vis);
            }
        }

        static void Start()
        {
            _dir = Path.Combine(Paths.BepInExRootPath, "ValCraft armor");
            Directory.CreateDirectory(_dir);
            _queue.Clear();
            _saved = 0;
            bool all = _list.Value.Trim().Equals("armor", StringComparison.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in _list.Value.Split(',')) if (n.Trim().Length > 0) wanted.Add(n.Trim());
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (!drop) continue;
                var t = drop.m_itemData.m_shared.m_itemType;
                bool armor = t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs ||
                             t == ItemDrop.ItemData.ItemType.Shoulder;
                string n = prefab.name;
                bool creature = n.StartsWith("FW_") || n.StartsWith("SP_") || n.StartsWith("Charred") || n.StartsWith("Dverger") && !n.StartsWith("DvergerCirclet") && n != "HelmetDverger" ||
                                n.StartsWith("Goblin") || n.StartsWith("Jotun") || n.StartsWith("StoneGolem") || n == "CapeTest";
                if (all ? (armor && !creature) : wanted.Contains(n)) _queue.Add(prefab);
            }
            _step = 0;
            _wait = 0;
            Plugin.Message($"ValCraft: sampling {_queue.Count} armor pieces as worn...");
            Undress(VisOf());
        }

        static VisEquipment VisOf() => Player.m_localPlayer ? Player.m_localPlayer.GetComponent<VisEquipment>() : null;

        static void Undress(VisEquipment vis)
        {
            if (!vis) return;
            vis.SetChestItem(0);
            vis.SetLegItem(0);
            vis.SetHelmetItem(0);
            vis.SetShoulderItem(0, 0, 0);
        }

        static void Dress(VisEquipment vis, GameObject prefab)
        {
            Undress(vis);
            int hash = prefab.name.GetStableHashCode();
            switch (prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Helmet: vis.SetHelmetItem(hash); break;
                case ItemDrop.ItemData.ItemType.Chest: vis.SetChestItem(hash); break;
                case ItemDrop.ItemData.ItemType.Legs: vis.SetLegItem(hash); break;
                default: vis.SetShoulderItem(hash, 0, 1); break;
            }
        }

        struct Tri { public Vector3 a, b, c, n; public Vector2 ua, ub, uc; public Texture2D tex; public Color tint; public float area; public bool masked; }

        static void Sample(Player player, VisEquipment vis, GameObject prefab)
        {
            var type = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType;
            var objects = new List<GameObject>();
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                    if (HelmetInstance.GetValue(vis) is GameObject h && h) objects.Add(h);
                    break;
                case ItemDrop.ItemData.ItemType.Chest:
                    if (ChestInstances.GetValue(vis) is List<GameObject> c) objects.AddRange(c);
                    break;
                case ItemDrop.ItemData.ItemType.Legs:
                    if (LegInstances.GetValue(vis) is List<GameObject> l) objects.AddRange(l);
                    break;
                default:
                    if (ShoulderInstances.GetValue(vis) is List<GameObject> s) objects.AddRange(s);
                    break;
            }
            var toLocal = player.transform.worldToLocalMatrix;
            var tris = new List<Tri>();
            foreach (var go in objects)
            {
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) AddRenderer(tris, r, toLocal, null);
            }
            // armor painted onto the body (the chest and legs textures of its material)
            if (vis.m_bodyModel && (type == ItemDrop.ItemData.ItemType.Chest || type == ItemDrop.ItemData.ItemType.Legs))
            {
                var mat = vis.m_bodyModel.material;
                string prop = type == ItemDrop.ItemData.ItemType.Chest ? "_ChestTex" : "_LegsTex";
                if (mat.HasProperty(prop) && mat.GetTexture(prop) is Texture tex && tex)
                    AddRenderer(tris, vis.m_bodyModel, toLocal, ModelExport.ReadableTexture(tex));
            }
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.Append("# ").Append(prefab.name).Append(" worn: ").Append(type).Append(". Player space (metres): x right, y up, z forward\n");
            // the skeleton, for fitting the points onto Minecraft's head, body, arms and legs
            if (vis.m_bodyModel)
                foreach (var b in vis.m_bodyModel.bones)
                    if (b) sb.Append("bone ").Append(b.name.Replace(' ', '_')).Append(' ').Append(V(toLocal.MultiplyPoint3x4(b.position), ci)).Append('\n');
            float total = 0f;
            foreach (var t in tris) total += t.area;
            var rng = new System.Random(1);
            int written = 0;
            foreach (var t in tris)
            {
                double want = total > 0 ? t.area / total * Points : 0;
                int n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    float r1 = (float)rng.NextDouble(), r2 = (float)rng.NextDouble();
                    if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                    var p = t.a + (t.b - t.a) * r1 + (t.c - t.a) * r2;
                    var uv = t.ua + (t.ub - t.ua) * r1 + (t.uc - t.ua) * r2;
                    var col = (t.tex ? t.tex.GetPixelBilinear(uv.x, uv.y) : Color.gray) * t.tint;
                    if (t.masked && col.a < 0.5f) continue;  // the body's own skin, not armor
                    sb.Append("p ").Append(V(p, ci)).Append(' ').Append(V(t.n, ci)).Append(' ')
                      .Append((int)(Mathf.Clamp01(col.r) * 255)).Append(' ').Append((int)(Mathf.Clamp01(col.g) * 255)).Append(' ')
                      .Append((int)(Mathf.Clamp01(col.b) * 255)).Append('\n');
                    written++;
                }
            }
            if (written == 0) { Plugin.Log($"armor export: {prefab.name}: nothing worn to sample"); return; }
            File.WriteAllText(Path.Combine(_dir, prefab.name + ".txt"), sb.ToString());
            _saved++;
        }

        static string V(Vector3 v, CultureInfo ci) =>
            v.x.ToString("0.0000", ci) + " " + v.y.ToString("0.0000", ci) + " " + v.z.ToString("0.0000", ci);

        static void AddRenderer(List<Tri> tris, Renderer r, Matrix4x4 toLocal, Texture2D maskTex)
        {
            Mesh mesh;
            Matrix4x4 m;
            if (r is SkinnedMeshRenderer smr)
            {
                mesh = new Mesh();
                smr.BakeMesh(mesh);
                m = toLocal * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
            }
            else if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                mesh = mf ? mf.sharedMesh : null;
                m = toLocal * r.transform.localToWorldMatrix;
            }
            else return;
            if (!mesh || !ModelExport.ReadMesh(mesh, out var verts, out var uvs, out var subs)) return;
            var mats = r.sharedMaterials;
            for (int s = 0; s < subs.Count; s++)
            {
                var mat = s < mats.Length ? mats[s] : null;
                if (maskTex == null && mat && mat.shader && mat.shader.name.ToLowerInvariant().Contains("shadow")) continue;
                var tex = maskTex ?? (mat && mat.mainTexture ? ModelExport.ReadableTexture(mat.mainTexture) : null);
                var tint = maskTex == null && mat && mat.HasProperty("_Color") ? mat.color : Color.white;
                var idx = subs[s];
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    var t = new Tri
                    {
                        a = m.MultiplyPoint3x4(verts[idx[i]]), b = m.MultiplyPoint3x4(verts[idx[i + 1]]), c = m.MultiplyPoint3x4(verts[idx[i + 2]]),
                        tex = tex, tint = tint, masked = maskTex != null,
                    };
                    if (uvs.Length == verts.Length) { t.ua = uvs[idx[i]]; t.ub = uvs[idx[i + 1]]; t.uc = uvs[idx[i + 2]]; }
                    var cross = Vector3.Cross(t.b - t.a, t.c - t.a);
                    t.area = cross.magnitude * 0.5f;
                    t.n = cross.sqrMagnitude > 0 ? cross.normalized : Vector3.up;
                    if (t.area > 0f) tris.Add(t);
                }
            }
        }

        static void Finish(Player player, VisEquipment vis)
        {
            _export.Value = false;
            _step = 0;
            _queue.Clear();
            // the Viking's own gear back on
            HarmonyLib.AccessTools.Method(typeof(Humanoid), "SetupEquipment")?.Invoke(player, null);
            Plugin.Message($"ValCraft: sampled {_saved} armor pieces to {_dir}");
        }
    }
}
