using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using ValCraft.Link;
using ValCraft.Render;

namespace ValCraft
{
    // Minecraft's blocks inside Valheim's scene. The Fabric mod meshes every section with Minecraft's
    // own block renderer (models, tint, fluids) and ships the meshes and its atlas over the render
    // ring; here they become ordinary Unity meshes, so Valheim's sun, shadows, fog and weather light
    // them and Valheim geometry hides them. Solid blocks get colliders so Valheim's creatures bump
    // into builds, and light-emitting blocks (torches, lava, glowstone) get Unity point lights.
    //
    // Everything hangs under one root that maps Minecraft space into Valheim (Z mirrored, interior
    // offsets undone), so vertices, normals and winding stay exactly as Minecraft sent them.
    public static unsafe class WorldRender
    {
        const int MaxLights = 12;
        static GameObject _root;
        static Texture2D _atlas;
        static bool _atlasDirty;
        static readonly Dictionary<long, Section> _sections = new Dictionary<long, Section>();
        static readonly Dictionary<long, List<Emitter>> _emitters = new Dictionary<long, List<Emitter>>();
        static readonly Dictionary<uint, int> _counts = new Dictionary<uint, int>();
        static float _logTimer = 10f;
        static int _pieceLayer = -1;
        static readonly List<Light> _lights = new List<Light>();
        static readonly List<Section> _pendingMaterials = new List<Section>();

        class Section
        {
            public GameObject go;
            public Mesh mesh;
            public MeshRenderer renderer;
            public long[] submeshKeys;  // layer << 32 | colour, per submesh (materials assigned when ready)
            public GameObject solids;
        }

        struct Emitter { public Vector3 pos; public int level; public Color color; public int kind; }

        public static Transform Root => _root ? _root.transform : null;
        public static int AtlasWidth => _atlas ? _atlas.width : 0;

        static long Key(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        public static void OnMinecraftConnected()
        {
            _counts.Clear();
        }

        static void EnsureRoot()
        {
            if (_root) return;
            _root = new GameObject("ValCraft Minecraft world");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            Collision.OwnRoot = _root.transform;
            _pieceLayer = LayerMask.NameToLayer("piece");
        }

        public static void Frame(float dt)
        {
            EnsureRoot();
            // Minecraft space follows the interior offsets; everything under the root moves with them.
            _root.transform.position = Coords.McRootPosition;
            _root.transform.localScale = Coords.McRootScale;
            bool show = Player.m_localPlayer && Puppet.McConnected;
            if (_root.activeSelf != show) _root.SetActive(show);

            Shm.DrainRender(OnMessage, 48ul << 20);
            if (_atlasDirty && _atlas) { _atlas.Apply(false, false); _atlasDirty = false; }
            if (_pendingMaterials.Count > 0 && Player.m_localPlayer && BlockMaterials.FindShader() && _atlas)
            {
                foreach (var s in _pendingMaterials) AssignMaterials(s);
                _pendingMaterials.Clear();
            }
            UpdateLights();
            Entities.Frame(_root.transform);
            SceneRender.Frame();

            _logTimer -= dt;
            if (_logTimer <= 0f && Plugin.Diagnostics.Value)
            {
                _logTimer = 30f;
                var parts = new List<string>();
                foreach (var kv in _counts) parts.Add(kv.Key + ":" + kv.Value);
                Plugin.Log($"render: {_sections.Count} sections, {_emitters.Count} lit sections; messages (type:count) " + string.Join(" ", parts));
            }
        }

        static void OnMessage(uint type, byte* p, uint bytes)
        {
            _counts.TryGetValue(type, out int n);
            _counts[type] = n + 1;
            try
            {
                switch (type)
                {
                    case Proto.RenClearAll: ClearAll(); break;
                    case Proto.RenAtlas: OnAtlas(p, bytes); break;
                    case Proto.RenAtlasRegion: OnAtlasRegion(p, bytes); break;
                    case Proto.RenSection: OnSection(p, bytes); break;
                    case Proto.RenSolids: OnSolids(p, bytes); break;
                    case Proto.RenLights: OnLights(p, bytes); break;
                    case Proto.RenTexture: SceneRender.OnTexture(p, bytes); break;
                    case Proto.RenScene: SceneRender.OnScene(_root.transform, p, bytes); break;
                    case Proto.RenAvatar: SceneRender.OnAvatar(_root.transform, p, bytes); break;
                    default: break;  // ragdoll: later
                }
            }
            catch (Exception e)
            {
                Plugin.Error($"render message {type}: {e}");
            }
        }

        static void ClearAll()
        {
            foreach (var s in _sections.Values) if (s.go) UnityEngine.Object.Destroy(s.go);
            _sections.Clear();
            _emitters.Clear();
            _pendingMaterials.Clear();
            SceneRender.Clear();
            Plugin.Log("render: cleared (Minecraft resends its world)");
        }

        static void OnAtlas(byte* p, uint bytes)
        {
            int w = *(int*)p, h = *(int*)(p + 4);
            if (w <= 0 || h <= 0 || bytes < 8 + (ulong)w * (ulong)h * 4) return;
            if (!_atlas || _atlas.width != w || _atlas.height != h)
            {
                if (_atlas) UnityEngine.Object.Destroy(_atlas);
                _atlas = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    name = "ValCraft atlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0
                };
            }
            // Minecraft's rows come top first with v = 0 at the top; loaded as-is, row r is Unity's row r
            // and Minecraft's UVs map straight on.
            _atlas.LoadRawTextureData((IntPtr)(p + 8), w * h * 4);
            _atlas.Apply(false, false);
            BlockMaterials.SetAtlas(_atlas);
            Plugin.Log($"render: atlas {w}x{h}");
        }

        static void OnAtlasRegion(byte* p, uint bytes)
        {
            if (!_atlas) return;
            int x = *(int*)p, y = *(int*)(p + 4), w = *(int*)(p + 8), h = *(int*)(p + 12);
            if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > _atlas.width || y + h > _atlas.height) return;
            var data = _atlas.GetPixelData<byte>(0);
            byte* src = p + 16;
            int stride = _atlas.width * 4;
            unsafe
            {
                byte* dst = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(data);
                for (int row = 0; row < h; row++)
                    Buffer.MemoryCopy(src + row * w * 4, dst + (y + row) * stride + x * 4, w * 4, w * 4);
            }
            _atlasDirty = true;
        }

        // ---- section meshes -----------------------------------------------------------------

        static readonly Vector3[] Normals =
        {
            Vector3.up,                 // 0: no normal (plants): lit as if facing up, like Minecraft
            new Vector3(0, -1, 0), new Vector3(0, 1, 0),   // DOWN, UP
            new Vector3(0, 0, -1), new Vector3(0, 0, 1),   // NORTH, SOUTH
            new Vector3(-1, 0, 0), new Vector3(1, 0, 0),   // WEST, EAST
        };

        static readonly Dictionary<long, List<int>> _groups = new Dictionary<long, List<int>>();
        static readonly List<Vector3> _pos = new List<Vector3>();
        static readonly List<Vector3> _nrm = new List<Vector3>();
        static readonly List<Vector2> _uv = new List<Vector2>();
        static readonly List<Color32> _col = new List<Color32>();

        static void OnSection(byte* p, uint bytes)
        {
            int sx = *(int*)p, sy = *(int*)(p + 4), sz = *(int*)(p + 8);
            int count = *(int*)(p + 12);
            long key = Key(sx, sy, sz);
            if (count <= 0)
            {
                if (_sections.TryGetValue(key, out var gone))
                {
                    if (gone.mesh) UnityEngine.Object.Destroy(gone.mesh);
                    if (gone.renderer) gone.renderer.enabled = false;
                    gone.submeshKeys = new long[0];
                    if (!gone.solids) { UnityEngine.Object.Destroy(gone.go); _sections.Remove(key); }
                }
                return;
            }
            if ((ulong)bytes < 16 + (ulong)count * 32) return;
            var v = (RenVertex*)(p + 16);

            _pos.Clear(); _nrm.Clear(); _uv.Clear(); _col.Clear();
            foreach (var g in _groups.Values) g.Clear();
            for (int t = 0; t + 2 < count; t += 3)
            {
                // Per triangle: which layer, and which tint (the brightest corner's colour).
                uint flags = v[t].flags;
                uint layer = (flags & 2) != 0 ? 1u : 0u;
                Color32 best = default;
                int bestSum = -1;
                for (int k = 0; k < 3; k++)
                {
                    uint c = v[t + k].color;
                    var cc = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                    int sum = cc.r + cc.g + cc.b;
                    if (sum > bestSum) { bestSum = sum; best = cc; }
                }
                long group = ((long)layer << 32) | BlockMaterials.Quantise(best);
                if (!_groups.TryGetValue(group, out var list)) _groups[group] = list = new List<int>();
                for (int k = 0; k < 3; k++)
                {
                    var vx = v[t + k];
                    list.Add(_pos.Count);
                    _pos.Add(new Vector3(vx.x, vx.y, vx.z));
                    _uv.Add(new Vector2(vx.u, vx.v));
                    uint n = (vx.flags >> 4) & 7;
                    _nrm.Add(Normals[n < Normals.Length ? n : 0]);
                    uint c = vx.color;
                    _col.Add(new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)));
                }
            }

            if (!_sections.TryGetValue(key, out var s))
            {
                s = new Section { go = new GameObject($"section {sx} {sy} {sz}") };
                s.go.transform.SetParent(_root.transform, false);
                s.go.transform.localPosition = new Vector3(sx * 16, sy * 16, sz * 16);
                s.go.AddComponent<MeshFilter>();
                s.renderer = s.go.AddComponent<MeshRenderer>();
                s.renderer.shadowCastingMode = ShadowCastingMode.On;
                s.renderer.receiveShadows = true;
                s.renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                _sections[key] = s;
            }
            if (s.mesh) UnityEngine.Object.Destroy(s.mesh);
            s.mesh = new Mesh { name = s.go.name, indexFormat = _pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            s.mesh.SetVertices(_pos);
            s.mesh.SetNormals(_nrm);
            s.mesh.SetUVs(0, _uv);
            s.mesh.SetColors(_col);
            var keys = new List<long>();
            foreach (var kv in _groups) if (kv.Value.Count > 0) keys.Add(kv.Key);
            s.mesh.subMeshCount = keys.Count;
            for (int i = 0; i < keys.Count; i++) s.mesh.SetTriangles(_groups[keys[i]], i, false);
            s.mesh.RecalculateBounds();
            s.go.GetComponent<MeshFilter>().sharedMesh = s.mesh;
            s.submeshKeys = keys.ToArray();
            s.renderer.enabled = true;
            if (BlockMaterials.Ready && Player.m_localPlayer) AssignMaterials(s);
            else if (!_pendingMaterials.Contains(s)) _pendingMaterials.Add(s);
        }

        static void AssignMaterials(Section s)
        {
            if (!s.renderer || s.submeshKeys == null) return;
            var mats = new Material[s.submeshKeys.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                long k = s.submeshKeys[i];
                var layer = (k >> 32) != 0 ? BlockMaterials.Layer.Translucent : BlockMaterials.Layer.Cutout;
                mats[i] = BlockMaterials.Get(layer, (uint)(k & 0xFFFFFFFF));
            }
            s.renderer.sharedMaterials = mats;
        }

        // ---- NPC collision --------------------------------------------------------------------

        static void OnSolids(byte* p, uint bytes)
        {
            int sx = *(int*)p, sy = *(int*)(p + 4), sz = *(int*)(p + 8);
            int count = *(int*)(p + 12);
            long key = Key(sx, sy, sz);
            if (!_sections.TryGetValue(key, out var s))
            {
                if (count <= 0) return;
                s = new Section { go = new GameObject($"section {sx} {sy} {sz}") };
                s.go.transform.SetParent(_root.transform, false);
                s.go.transform.localPosition = new Vector3(sx * 16, sy * 16, sz * 16);
                s.go.AddComponent<MeshFilter>();
                s.renderer = s.go.AddComponent<MeshRenderer>();
                s.renderer.enabled = false;
                _sections[key] = s;
            }
            if (s.solids) UnityEngine.Object.Destroy(s.solids);
            s.solids = null;
            if (count <= 0 || bytes < 16 + 512) return;
            ulong* bits = (ulong*)(p + 16);
            bool Solid(int x, int y, int z) { int b = x + 16 * z + 256 * y; return (bits[b >> 6] & (1ul << (b & 63))) != 0; }
            s.solids = new GameObject("solids");
            s.solids.transform.SetParent(s.go.transform, false);
            if (_pieceLayer >= 0) s.solids.layer = _pieceLayer;
            // Runs along x merged into one box each.
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        if (!Solid(x, y, z)) continue;
                        int x1 = x;
                        while (x1 + 1 < 16 && Solid(x1 + 1, y, z)) x1++;
                        var box = s.solids.AddComponent<BoxCollider>();
                        box.center = new Vector3((x + x1 + 1) * 0.5f, y + 0.5f, z + 0.5f);
                        box.size = new Vector3(x1 - x + 1, 1f, 1f);
                        x = x1;
                    }
        }

        // ---- block lights -----------------------------------------------------------------------

        static void OnLights(byte* p, uint bytes)
        {
            int sx = *(int*)p, sy = *(int*)(p + 4), sz = *(int*)(p + 8);
            int count = *(int*)(p + 12);
            long key = Key(sx, sy, sz);
            if (count <= 0 || bytes < 16 + (ulong)count * 8) { _emitters.Remove(key); return; }
            var list = new List<Emitter>(count);
            byte* e = p + 16;
            for (int i = 0; i < count; i++, e += 8)
            {
                uint c = *(uint*)(e + 4);
                list.Add(new Emitter
                {
                    pos = new Vector3(sx * 16 + e[0] + 0.5f, sy * 16 + e[1] + 0.5f, sz * 16 + e[2] + 0.5f),
                    level = e[3],
                    color = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), 255),
                    kind = (int)((c >> 24) & 0xF),
                });
            }
            _emitters[key] = list;
        }

        static readonly List<(float d, Emitter e)> _near = new List<(float, Emitter)>();

        // The brightest emitters near the player get Unity point lights (Minecraft coordinates in, Valheim out).
        static void UpdateLights()
        {
            _near.Clear();
            if (Puppet.McInWorld && _root.activeSelf)
            {
                var me = new Vector3((float)Puppet.Mc.x, (float)Puppet.Mc.y, (float)Puppet.Mc.z);
                foreach (var list in _emitters.Values)
                    foreach (var e in list)
                    {
                        float d = (e.pos - me).sqrMagnitude;
                        if (d < 48f * 48f) _near.Add((d / Mathf.Max(1, e.level), e));
                    }
                _near.Sort((a, b) => a.d.CompareTo(b.d));
            }
            int n = Mathf.Min(MaxLights, _near.Count);
            while (_lights.Count < n)
            {
                var go = new GameObject("block light");
                go.transform.SetParent(_root.transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                _lights.Add(l);
            }
            float time = Time.time;
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (i >= n) { if (l.enabled) l.enabled = false; continue; }
                var e = _near[i].e;
                l.enabled = true;
                l.transform.localPosition = e.pos;
                l.color = e.color;
                l.range = e.level * 1.1f;
                float flicker = e.kind == 1 ? 0.9f + 0.1f * Mathf.PerlinNoise(time * 6f, i) : e.kind == 2 ? 0.9f + 0.1f * Mathf.Sin(time * 1.5f + i) : 1f;
                l.intensity = 1.1f * e.level / 15f * flicker;
            }
        }

        public static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            if (s) return s;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m && m.shader && m.shader.name == name) return m.shader;
            return null;
        }
    }
}
