using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValCraft
{
    // For ValCraft's 3D weapons resource pack: samples each listed item's 3D model as a cloud of
    // coloured points on its surface (BepInEx/ValCraft models/<prefab>.txt: x y z r g b, metres),
    // the model stood up straight: its long side along y (the end farther from where Valheim holds it
    // up), its width along x, its thin side along z: the measurements and colours the blocky models
    // in tools/weapons_3d.py are designed from. [Debug] ExportModels, which switches itself back off.
    public static class ModelExport
    {
        const int Points = 60000;

        static ConfigEntry<bool> _export;
        static ConfigEntry<string> _list;
        static readonly Dictionary<Texture, Texture2D> _readable = new Dictionary<Texture, Texture2D>();

        public static void Init(ConfigFile config)
        {
            _export = config.Bind("Debug", "ExportModels", false,
                "Sample the 3D models of the items in ExportModelsList as coloured point clouds in BepInEx/ValCraft models (for making ValCraft's 3D weapon models). Switches itself off when done.");
            _list = config.Bind("Debug", "ExportModelsList", "THSwordGold,THSwordSlayer,AxeIron,MaceGold,SpearBronze",
                "Item prefab names to sample, comma-separated; weapons for every weapon, tool and shield.");
        }

        public static void Frame()
        {
            if (_export == null || !_export.Value || !ObjectDB.instance || ObjectDB.instance.m_items.Count == 0 || !ZNetScene.instance) return;
            _export.Value = false;
            try { Export(); } catch (Exception e) { Plugin.Warn($"model export: {e}"); }
            foreach (var t in _readable.Values) if (t) UnityEngine.Object.Destroy(t);
            _readable.Clear();
        }

        static void Export()
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "ValCraft models");
            Directory.CreateDirectory(dir);
            bool all = _list.Value.Trim().Equals("weapons", StringComparison.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in _list.Value.Split(',')) if (n.Trim().Length > 0) wanted.Add(n.Trim());
            int saved = 0;
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (!drop) continue;
                var type = drop.m_itemData.m_shared.m_itemType;
                bool weapon = type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                              type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || type == ItemDrop.ItemData.ItemType.Bow ||
                              type == ItemDrop.ItemData.ItemType.Tool || type == ItemDrop.ItemData.ItemType.Shield;
                if (all ? !weapon : !wanted.Contains(prefab.name)) continue;
                GameObject go = null;
                try
                {
                    ZNetView.m_forceDisableInit = true;
                    go = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, -3000f, 0f), Quaternion.identity);
                    ZNetView.m_forceDisableInit = false;
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                    var text = Sample(go, prefab.name);
                    if (text == null) continue;
                    File.WriteAllText(Path.Combine(dir, prefab.name + ".txt"), text);
                    saved++;
                }
                catch (Exception e) { Plugin.Warn($"model export: {prefab.name}: {e.Message}"); }
                finally
                {
                    ZNetView.m_forceDisableInit = false;
                    if (go) UnityEngine.Object.DestroyImmediate(go);
                }
            }
            Plugin.Message($"ValCraft: sampled {saved} item models to {dir}");
        }

        struct Tri { public Vector3 a, b, c; public Vector2 ua, ub, uc; public Texture2D tex; public Color tint; public float area; }

        static string Sample(GameObject go, string name)
        {
            var tris = new List<Tri>();
            var root = go.transform;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh mesh = null;
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = new Mesh();
                    smr.BakeMesh(mesh);
                    m = root.worldToLocalMatrix * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf ? mf.sharedMesh : null;
                    m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                }
                else continue;
                if (!mesh) continue;
                Vector3[] verts;
                Vector2[] uvs;
                List<int[]> subs;
                if (mesh.isReadable)
                {
                    verts = mesh.vertices;
                    uvs = mesh.uv;
                    subs = new List<int[]>();
                    for (int s = 0; s < mesh.subMeshCount; s++) subs.Add(mesh.GetTriangles(s));
                }
                else if (!ReadFromGpu(mesh, out verts, out uvs, out subs))
                {
                    Plugin.Warn($"model export: {name}: couldn't read mesh {mesh.name}");
                    continue;
                }
                var mats = r.sharedMaterials;
                for (int s = 0; s < subs.Count; s++)
                {
                    var mat = s < mats.Length ? mats[s] : null;
                    var tex = mat && mat.mainTexture ? Readable(mat.mainTexture) : null;
                    var tint = mat && mat.HasProperty("_Color") ? mat.color : Color.white;
                    var idx = subs[s];
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        var t = new Tri
                        {
                            a = m.MultiplyPoint3x4(verts[idx[i]]), b = m.MultiplyPoint3x4(verts[idx[i + 1]]), c = m.MultiplyPoint3x4(verts[idx[i + 2]]),
                            tex = tex, tint = tint,
                        };
                        if (uvs.Length == verts.Length) { t.ua = uvs[idx[i]]; t.ub = uvs[idx[i + 1]]; t.uc = uvs[idx[i + 2]]; }
                        t.area = Vector3.Cross(t.b - t.a, t.c - t.a).magnitude * 0.5f;
                        if (t.area > 0f) tris.Add(t);
                    }
                }
            }
            if (tris.Count == 0) return null;

            // Stand it up: longest side along y (tip up: the end farther from the origin), then width
            // along x, thin side along z.
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            float total = 0f;
            foreach (var t in tris)
            {
                min = Vector3.Min(min, Vector3.Min(t.a, Vector3.Min(t.b, t.c)));
                max = Vector3.Max(max, Vector3.Max(t.a, Vector3.Max(t.b, t.c)));
                total += t.area;
            }
            var size = max - min;
            var centre = (min + max) * 0.5f;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var ext = new[] { size.x, size.y, size.z };
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (i, j) => ext[j].CompareTo(ext[i]));
            Vector3 along = axes[order[0]], across = axes[order[1]];
            if (Vector3.Dot(centre, along) < 0f) along = -along;
            var thin = Vector3.Cross(along, across);

            var rng = new System.Random(1);
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.Append("# ").Append(name).Append(" points: x y z r g b (x across, y along to the tip, z thickness)\n");
            foreach (var t in tris)
            {
                double want = t.area / total * Points;
                int n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    float r1 = (float)rng.NextDouble(), r2 = (float)rng.NextDouble();
                    if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                    var p = t.a + (t.b - t.a) * r1 + (t.c - t.a) * r2 - centre;
                    var uv = t.ua + (t.ub - t.ua) * r1 + (t.uc - t.ua) * r2;
                    var col = (t.tex ? t.tex.GetPixelBilinear(uv.x, uv.y) : Color.gray) * t.tint;
                    sb.Append(Vector3.Dot(p, across).ToString("0.0000", ci)).Append(' ')
                      .Append(Vector3.Dot(p, along).ToString("0.0000", ci)).Append(' ')
                      .Append(Vector3.Dot(p, thin).ToString("0.0000", ci)).Append(' ')
                      .Append((int)(Mathf.Clamp01(col.r) * 255)).Append(' ')
                      .Append((int)(Mathf.Clamp01(col.g) * 255)).Append(' ')
                      .Append((int)(Mathf.Clamp01(col.b) * 255)).Append('\n');
                }
            }
            return sb.ToString();
        }

        // Most of Valheim's meshes only live on the graphics card: read their vertex and index
        // buffers back from it.
        static bool ReadFromGpu(Mesh mesh, out Vector3[] verts, out Vector2[] uvs, out List<int[]> subs)
        {
            verts = null;
            uvs = null;
            subs = null;
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position)) return false;
            int n = mesh.vertexCount;
            verts = new Vector3[n];
            uvs = new Vector2[n];
            var posStream = mesh.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position);
            var data = new Dictionary<int, (byte[] bytes, int stride)>();
            (byte[] bytes, int stride) Stream(int stream)
            {
                if (data.TryGetValue(stream, out var d)) return d;
                using (var vb = mesh.GetVertexBuffer(stream))
                {
                    var bytes = new byte[vb.count * vb.stride];
                    vb.GetData(bytes);
                    d = (bytes, vb.stride);
                }
                data[stream] = d;
                return d;
            }
            var ps = Stream(posStream);
            int posOff = mesh.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
            for (int i = 0; i < n; i++)
            {
                int o = i * ps.stride + posOff;
                verts[i] = new Vector3(BitConverter.ToSingle(ps.bytes, o), BitConverter.ToSingle(ps.bytes, o + 4), BitConverter.ToSingle(ps.bytes, o + 8));
            }
            if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
            {
                var us = Stream(mesh.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.TexCoord0));
                int uvOff = mesh.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.TexCoord0);
                bool half = mesh.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.TexCoord0) == UnityEngine.Rendering.VertexAttributeFormat.Float16;
                for (int i = 0; i < n; i++)
                {
                    int o = i * us.stride + uvOff;
                    uvs[i] = half ? new Vector2(Mathf.HalfToFloat(BitConverter.ToUInt16(us.bytes, o)), Mathf.HalfToFloat(BitConverter.ToUInt16(us.bytes, o + 2)))
                                  : new Vector2(BitConverter.ToSingle(us.bytes, o), BitConverter.ToSingle(us.bytes, o + 4));
                }
            }
            byte[] ib;
            int isz = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16 ? 2 : 4;
            using (var buf = mesh.GetIndexBuffer())
            {
                ib = new byte[buf.count * buf.stride];
                buf.GetData(ib);
            }
            subs = new List<int[]>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sm = mesh.GetSubMesh(s);
                if (sm.topology != MeshTopology.Triangles) { subs.Add(new int[0]); continue; }
                var tri = new int[sm.indexCount];
                for (int i = 0; i < sm.indexCount; i++)
                {
                    int o = (sm.indexStart + i) * isz;
                    tri[i] = (isz == 2 ? BitConverter.ToUInt16(ib, o) : BitConverter.ToInt32(ib, o)) + sm.baseVertex;
                }
                subs.Add(tri);
            }
            return true;
        }

        // Game textures can't be read directly: copy through a render texture.
        static Texture2D Readable(Texture tex)
        {
            if (_readable.TryGetValue(tex, out var cached)) return cached;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            _readable[tex] = copy;
            return copy;
        }
    }
}
