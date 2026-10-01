using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using ValCraft.Link;

namespace ValCraft.Render
{
    // Minecraft's small world things, from the world-entity table (rewritten every Minecraft frame):
    // block-breaking cracks, dropped items (spinning mini blocks or flat item sprites), arrows and
    // tridents, and the outline around the targeted block. One mesh rebuilt every frame, in Minecraft
    // space under WorldRender's root. Faces are wound counter-clockwise seen from outside, as
    // Minecraft's own quads are (the root's mirror turns that into Unity's front faces).
    public static unsafe class Entities
    {
        const int WeArrow = 1, WeItem = 2, WeTrident = 3, WeBlock = 4, WeCrack = 5, WeShadow = 6;
        const int EntityBytes = 96;

        static GameObject _go;
        static Mesh _mesh;
        static MeshRenderer _renderer;
        static Material _outlineMat;
        static readonly List<Vector3> _pos = new List<Vector3>();
        static readonly List<Vector3> _nrm = new List<Vector3>();
        static readonly List<Vector2> _uv = new List<Vector2>();
        static readonly Dictionary<long, List<int>> _groups = new Dictionary<long, List<int>>();
        static readonly List<long> _keys = new List<long>();
        static readonly List<Material> _mats = new List<Material>();
        const long OutlineKey = -1;

        static readonly byte[] _table = new byte[0x40 + EntityBytes * 160];

        public static void Frame(Transform root)
        {
            if (!root || !BlockMaterials.Ready) return;
            if (!_go)
            {
                _go = new GameObject("minecraft entities");
                _go.transform.SetParent(root, false);
                _mesh = new Mesh { name = "minecraft entities" };
                _mesh.MarkDynamic();
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.On;
                _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;  // rebuilt every frame (see SceneRender)
            }
            _pos.Clear(); _nrm.Clear(); _uv.Clear();
            foreach (var g in _groups.Values) g.Clear();

            if (Puppet.McInWorld && Shm.Valid && Snapshot(out int count, out bool hasSel, out Vector3 selLo, out Vector3 selHi))
            {
                fixed (byte* t = _table)
                {
                    for (int i = 0; i < count; i++) AddEntity(t + 0x40 + i * EntityBytes);
                }
                if (hasSel && Puppet.MinecraftOwnsPlayer && !Puppet.McScreenOpen) AddOutline(selLo, selHi);
            }

            _mesh.Clear();
            if (_pos.Count == 0) { _renderer.enabled = false; return; }
            _renderer.enabled = true;
            _mesh.indexFormat = _pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(_pos);
            _mesh.SetNormals(_nrm);
            _mesh.SetUVs(0, _uv);
            _keys.Clear();
            foreach (var kv in _groups) if (kv.Value.Count > 0) _keys.Add(kv.Key);
            _mesh.subMeshCount = _keys.Count;
            _mats.Clear();
            for (int i = 0; i < _keys.Count; i++)
            {
                _mesh.SetTriangles(_groups[_keys[i]], i, false);
                _mats.Add(_keys[i] == OutlineKey ? OutlineMaterial() : BlockMaterials.Get(BlockMaterials.Layer.Cutout, (uint)_keys[i]));
            }
            _mesh.RecalculateBounds();
            _renderer.sharedMaterials = _mats.ToArray();
        }

        // Seqlock copy of the table into _table.
        static bool Snapshot(out int count, out bool hasSel, out Vector3 lo, out Vector3 hi)
        {
            count = 0; hasSel = false; lo = hi = default;
            byte* b = Shm.Base + Proto.OffWorldEntities;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                uint s1 = Volatile.Read(ref *(uint*)b);
                if ((s1 & 1) != 0) { Thread.SpinWait(4); continue; }
                count = (int)Mathf.Min(*(uint*)(b + 4), 160);
                int bytes = 0x40 + count * EntityBytes;
                fixed (byte* t = _table) System.Buffer.MemoryCopy(b, t, _table.Length, bytes);
                Thread.MemoryBarrier();
                if (Volatile.Read(ref *(uint*)b) != s1) continue;
                fixed (byte* t = _table)
                {
                    hasSel = *(uint*)(t + 8) != 0;
                    float* f = (float*)(t + 12);
                    lo = new Vector3(f[0], f[1], f[2]);
                    hi = new Vector3(f[3], f[4], f[5]);
                }
                return true;
            }
            return false;
        }

        static void AddEntity(byte* e)
        {
            int kind = *(int*)e;
            float* f = (float*)(e + 8);
            var p = new Vector3(f[0], f[1], f[2]);
            float yaw = f[3], pitch = f[4], scale = f[5];
            var ext = new Vector3(f[6], f[7], f[8]);
            float* uv = (float*)(e + 44);
            uint tint = *(uint*)(e + 92);
            switch (kind)
            {
                case WeCrack:
                    Box(p, p + ext, Rect(uv, 0), Rect(uv, 0), Rect(uv, 0), 0xFFFFFFFFu, Quaternion.identity, p);
                    break;
                case WeBlock:
                    {
                        float h = scale * 0.5f;
                        var rot = Quaternion.Euler(0f, -yaw, 0f);
                        uint top = tint != 0 ? BlockMaterials.Quantise(new Color32((byte)tint, (byte)(tint >> 8), (byte)(tint >> 16), 255)) : 0xFFFFFFFFu;
                        Box(p - Vector3.one * h, p + Vector3.one * h, Rect(uv, 0), Rect(uv, 1), Rect(uv, 2), top, rot, p);
                        break;
                    }
                case WeItem:
                    {
                        // A flat sprite turning about the vertical, seen from both sides.
                        var rot = Quaternion.Euler(0f, -yaw, 0f);
                        float h = scale * 0.5f;
                        var r = rot * Vector3.right * h;
                        var u = Vector3.up * h;
                        var n = rot * Vector3.forward;
                        Quad(p - r - u, p + r - u, p + r + u, p - r + u, n, Rect(uv, 0), 0xFFFFFFFFu);
                        Quad(p + r - u, p - r - u, p - r + u, p + r + u, -n, Rect(uv, 0), 0xFFFFFFFFu, flipU: true);
                        break;
                    }
                case WeArrow:
                case WeTrident:
                    {
                        // Two crossed quads along the flight direction. Projectiles keep their angles
                        // the other way round from a player's look: they face (sin yaw, sin pitch, cos yaw).
                        float yr = yaw * Mathf.Deg2Rad, pr = pitch * Mathf.Deg2Rad;
                        var dir = new Vector3(Mathf.Sin(yr) * Mathf.Cos(pr), Mathf.Sin(pr), Mathf.Cos(yr) * Mathf.Cos(pr));
                        float len = kind == WeTrident ? 0.9f : 0.5f, w = 0.08f;
                        var side = Vector3.Cross(dir, Vector3.up);
                        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                        side.Normalize();
                        var up = Vector3.Cross(side, dir).normalized;
                        var a = p - dir * len * 0.5f;
                        var b = p + dir * len * 0.5f;
                        foreach (var s in new[] { side, up })
                        {
                            var n = Vector3.Cross(dir, s);
                            Quad(a - s * w, b - s * w, b + s * w, a + s * w, n, Rect(uv, 0), 0xFFFFFFFFu);
                            Quad(b - s * w, a - s * w, a + s * w, b + s * w, -n, Rect(uv, 0), 0xFFFFFFFFu, flipU: true);
                        }
                        break;
                    }
                case WeShadow:
                default:
                    break;  // Valheim draws real shadows
            }
        }

        static Vector4 Rect(float* uv, int i) => new Vector4(uv[i * 4], uv[i * 4 + 1], uv[i * 4 + 2], uv[i * 4 + 3]);

        // Axis box lo..hi rotated by rot about pivot: sides use `side`, top `top`, bottom `bottom`
        // (atlas rects {u0, v0, u1, v1}, v0 the texture's top).
        static void Box(Vector3 lo, Vector3 hi, Vector4 side, Vector4 top, Vector4 bottom, uint topColour, Quaternion rot, Vector3 pivot, uint colour = 0xFFFFFFFFu)
        {
            var c = (lo + hi) * 0.5f;
            var h = (hi - lo) * 0.5f;
            void Face(Vector3 n, Vector3 up, float hr, float hu, float hn, Vector4 rect, uint colour)
            {
                var right = Vector3.Cross(up, n);
                var fc = c + n * hn;
                var r = right * hr;
                var u = up * hu;
                Vector3 T(Vector3 v) => pivot + rot * (v - pivot);
                Quad(T(fc - r - u), T(fc + r - u), T(fc + r + u), T(fc - r + u), rot * n, rect, colour);
            }
            Face(new Vector3(0, 0, -1), Vector3.up, h.x, h.y, h.z, side, colour);  // north
            Face(new Vector3(0, 0, 1), Vector3.up, h.x, h.y, h.z, side, colour);   // south
            Face(new Vector3(-1, 0, 0), Vector3.up, h.z, h.y, h.x, side, colour);  // west
            Face(new Vector3(1, 0, 0), Vector3.up, h.z, h.y, h.x, side, colour);   // east
            Face(Vector3.up, new Vector3(0, 0, -1), h.x, h.z, h.y, top, topColour);
            Face(Vector3.down, new Vector3(0, 0, 1), h.x, h.z, h.y, bottom, colour);
        }

        // Corners bottom-left, bottom-right, top-right, top-left seen from the front.
        static void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 n, Vector4 rect, uint colour, bool flipU = false)
        {
            if (!_groups.TryGetValue(colour, out var list)) _groups[colour] = list = new List<int>();
            int i = _pos.Count;
            _pos.Add(bl); _pos.Add(br); _pos.Add(tr); _pos.Add(tl);
            for (int k = 0; k < 4; k++) _nrm.Add(n);
            float u0 = flipU ? rect.z : rect.x, u1 = flipU ? rect.x : rect.z;
            _uv.Add(new Vector2(u0, rect.w)); _uv.Add(new Vector2(u1, rect.w));
            _uv.Add(new Vector2(u1, rect.y)); _uv.Add(new Vector2(u0, rect.y));
            list.Add(i); list.Add(i + 1); list.Add(i + 2);
            list.Add(i); list.Add(i + 2); list.Add(i + 3);
        }

        // Minecraft's selection box: thin dark edges just outside the block's shape.
        static void AddOutline(Vector3 lo, Vector3 hi)
        {
            const float g = 0.002f, w = 0.006f;
            lo -= Vector3.one * g;
            hi += Vector3.one * g;
            if (!_groups.TryGetValue(OutlineKey, out var list)) _groups[OutlineKey] = list = new List<int>();
            void Edge(Vector3 a, Vector3 b)
            {
                var min = Vector3.Min(a, b) - Vector3.one * w;
                var max = Vector3.Max(a, b) + Vector3.one * w;
                BoxInto(min, max, list);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? lo.x : hi.x, z = (i & 2) == 0 ? lo.z : hi.z;
                Edge(new Vector3(x, lo.y, z), new Vector3(x, hi.y, z));  // vertical edges
                float y = (i & 1) == 0 ? lo.y : hi.y;
                float zz = (i & 2) == 0 ? lo.z : hi.z;
                Edge(new Vector3(lo.x, y, zz), new Vector3(hi.x, y, zz));  // along x
                float xx = (i & 2) == 0 ? lo.x : hi.x;
                Edge(new Vector3(xx, y, lo.z), new Vector3(xx, y, hi.z));  // along z
            }
        }

        static void BoxInto(Vector3 lo, Vector3 hi, List<int> list)
        {
            var c = (lo + hi) * 0.5f;
            var h = (hi - lo) * 0.5f;
            void Face(Vector3 n, Vector3 up, float hr, float hu, float hn)
            {
                var right = Vector3.Cross(up, n);
                var fc = c + n * hn;
                var r = right * hr;
                var u = up * hu;
                int i = _pos.Count;
                _pos.Add(fc - r - u); _pos.Add(fc + r - u); _pos.Add(fc + r + u); _pos.Add(fc - r + u);
                for (int k = 0; k < 4; k++) { _nrm.Add(n); _uv.Add(Vector2.zero); }
                list.Add(i); list.Add(i + 1); list.Add(i + 2);
                list.Add(i); list.Add(i + 2); list.Add(i + 3);
            }
            Face(new Vector3(0, 0, -1), Vector3.up, h.x, h.y, h.z);
            Face(new Vector3(0, 0, 1), Vector3.up, h.x, h.y, h.z);
            Face(new Vector3(-1, 0, 0), Vector3.up, h.z, h.y, h.x);
            Face(new Vector3(1, 0, 0), Vector3.up, h.z, h.y, h.x);
            Face(Vector3.up, new Vector3(0, 0, -1), h.x, h.z, h.y);
            Face(Vector3.down, new Vector3(0, 0, 1), h.x, h.z, h.y);
        }

        static Material OutlineMaterial()
        {
            if (_outlineMat) return _outlineMat;
            _outlineMat = new Material(BlockMaterials.Get(BlockMaterials.Layer.Cutout, 0xFF000000u)) { name = "valcraft_outline", mainTexture = Texture2D.whiteTexture };
            _outlineMat.color = new Color(0.02f, 0.02f, 0.02f, 1f);
            return _outlineMat;
        }
    }
}
