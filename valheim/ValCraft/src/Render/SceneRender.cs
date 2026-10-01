using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ValCraft.Link;

namespace ValCraft.Render
{
    // Everything else Minecraft draws in its world, captured from Minecraft's own renderers every
    // frame (AvatarExporter on the Fabric side) and drawn in Valheim's scene:
    //  - the scene: lit TNT, falling blocks, minecarts, boats, block entities (chests, beds, signs,
    //    banners, moving pistons) and every particle (explosions, smoke, block debris, crits);
    //  - the avatar: the player's own Minecraft body, shown in Minecraft's third person (F5).
    // Entity textures (skins, the TNT's, chests') come once as RenTexture; texture 0 is the atlas.
    public static unsafe class SceneRender
    {
        static readonly Dictionary<int, Texture2D> _textures = new Dictionary<int, Texture2D>();
        static Part _scene, _avatar, _viewModel;
        // The hands come in Minecraft's view space (metres in front of the eye); shrunk towards the eye
        // they look the same but stay inside the player's collision, so walls never cut into them.
        const float ViewModelScale = 0.25f;
        public static bool AvatarVisible => _avatar != null && _avatar.renderer && _avatar.renderer.enabled;

        class Part
        {
            public ulong hash;  // of the last payload built: identical frames are skipped
            public GameObject go;
            public Mesh mesh;
            public MeshRenderer renderer;
        }

        public static void Clear()
        {
            foreach (var t in _textures.Values) if (t) UnityEngine.Object.Destroy(t);
            _textures.Clear();
            if (_scene != null && _scene.renderer) _scene.renderer.enabled = false;
            if (_avatar != null && _avatar.renderer) _avatar.renderer.enabled = false;
            if (_viewModel != null && _viewModel.renderer) _viewModel.renderer.enabled = false;
        }

        public static void OnTexture(byte* p, uint bytes)
        {
            int id = *(int*)p, w = *(int*)(p + 4), h = *(int*)(p + 8);
            if (w <= 0 || h <= 0 || bytes < 16 + (ulong)w * (ulong)h * 4) return;
            if (!_textures.TryGetValue(id, out var tex) || !tex || tex.width != w || tex.height != h)
            {
                if (tex) UnityEngine.Object.Destroy(tex);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = "ValCraft texture " + id, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _textures[id] = tex;
            }
            // Rows top first with v = 0 at the top: loaded as-is, the UVs map straight on (see OnAtlas).
            tex.LoadRawTextureData((IntPtr)(p + 16), w * h * 4);
            tex.Apply(false, false);
        }

        public static void OnScene(Transform root, byte* p, uint bytes)
        {
            double ox = *(double*)p, oy = *(double*)(p + 8), oz = *(double*)(p + 16);
            if (Plugin.Diagnostics.Value) Probe(ox, oy, oz, p + 24);
            _scene ??= NewPart(root, "minecraft scene");
            Build(_scene, p + 24, bytes - 24);
            _scene.go.transform.localPosition = new Vector3((float)ox, (float)oy, (float)oz);
        }

        public static void OnAvatar(Transform root, byte* p, uint bytes)
        {
            _avatar ??= NewPart(root, "minecraft player");
            Build(_avatar, p, bytes);
        }

        // The hands are drawn deferred like the world, with Valheim's ambient occlusion taken back off
        // them (HandOcclusion; so close to the eye it turned them black). Where it can't be (the
        // occlusion not in the G-buffer), they're drawn after the opaque pass instead: no ambient
        // occlusion, but no shadows either.
        public static MeshRenderer ViewModelRenderer => _viewModel != null && _viewModel.renderer && _viewModel.renderer.enabled ? _viewModel.renderer : null;
        static readonly Dictionary<Material, Material> _late = new Dictionary<Material, Material>();
        static readonly HashSet<Material> _lateSet = new HashSet<Material>();

        public static void RefreshViewModel()
        {
            if (_viewModel != null) _viewModel.hash = 0;  // rebuilt with the right materials
        }

        static void ApplyViewModelMode()
        {
            if (_viewModel == null || !_viewModel.renderer) return;
            bool late = !HandOcclusion.Usable;
            _viewModel.renderer.receiveShadows = !late;
            if (!late) return;
            var mats = _viewModel.renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i] || _lateSet.Contains(mats[i])) continue;  // already swapped (an unchanged frame)
                if (!_late.TryGetValue(mats[i], out var m) || !m)
                {
                    m = new Material(mats[i]) { name = mats[i].name + "_late" };
                    m.renderQueue = Math.Max(mats[i].renderQueue, 2501);
                    _late[mats[i]] = m;
                    _lateSet.Add(m);
                }
                if (m.mainTexture != mats[i].mainTexture) m.mainTexture = mats[i].mainTexture;  // a new atlas
                mats[i] = m;
            }
            _viewModel.renderer.sharedMaterials = mats;
        }

        // Same payload as the avatar, positions in Minecraft's view space (x right, y up, looking -z).
        public static void OnViewModel(byte* p, uint bytes)
        {
            var cam = GameCamera.instance;
            if (!cam) return;
            if (_viewModel == null || !_viewModel.go)
            {
                _viewModel = NewPart(cam.transform, "minecraft hands");
                _viewModel.go.transform.localPosition = Vector3.zero;
                _viewModel.go.transform.localRotation = Quaternion.identity;
                _viewModel.go.transform.localScale = new Vector3(ViewModelScale, ViewModelScale, -ViewModelScale);
                // Lit and shaded by the world, but casting no shadow of its own (it would land on the
                // view from inside the head).
                _viewModel.renderer.shadowCastingMode = ShadowCastingMode.Off;
                _viewModel.renderer.receiveShadows = true;
                // Moves with the camera: no motion vectors of its own, so motion blur leaves it sharp.
                _viewModel.renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                if (!cam.GetComponent<HandOcclusion>()) cam.gameObject.AddComponent<HandOcclusion>();
            }
            Build(_viewModel, p, bytes);
            ApplyViewModelMode();
            if (!Puppet.Puppeting || Puppet.Mc.cameraMode != 0) _viewModel.renderer.enabled = false;
        }

        // Every frame: the body stands at the feet the camera follows (Puppet's interpolation).
        public static void Frame()
        {
            if (_viewModel != null && _viewModel.go && (!Puppet.Puppeting || Puppet.Mc.cameraMode != 0)) _viewModel.renderer.enabled = false;
            if (_avatar == null || !_avatar.go) return;
            if (!Puppet.Puppeting || Puppet.Mc.cameraMode == 0) { _avatar.renderer.enabled = false; return; }
            var f = Coords.ToMc(Puppet.FeetPos);
            // Under the root, local positions are Minecraft coordinates with the interior offsets
            // taken off (the root adds them back).
            _avatar.go.transform.localPosition = new Vector3((float)f.x, (float)f.y, (float)f.z);
        }

        // Diagnostics: where textured (entity / block-entity) geometry lands in absolute Minecraft
        // coordinates. A static chest should give the same box every message.
        static int _probeCount;
        static Vector3 _probeMinLo = Vector3.positiveInfinity, _probeMaxLo = Vector3.negativeInfinity;
        static readonly List<string> _probeOrigins = new List<string>();

        static void Probe(double ox, double oy, double oz, byte* p)
        {
            int batchCount = *(int*)p;
            int* batches = (int*)(p + 8);
            var v = (RenVertex*)(p + 8 + batchCount * 16);
            var lo = Vector3.positiveInfinity;
            for (int b = 0; b < batchCount; b++)
            {
                if (batches[b * 4] == 0) continue;  // atlas batches (particles, moving blocks)
                for (int i = batches[b * 4 + 1]; i < batches[b * 4 + 1] + batches[b * 4 + 2]; i++)
                    lo = Vector3.Min(lo, new Vector3((float)(ox + v[i].x), (float)(oy + v[i].y), (float)(oz + v[i].z)));
            }
            if (float.IsInfinity(lo.x)) return;
            _probeMinLo = Vector3.Min(_probeMinLo, lo);
            _probeMaxLo = Vector3.Max(_probeMaxLo, lo);
            if (_probeOrigins.Count < 6 && (_probeOrigins.Count == 0 || !_probeOrigins[_probeOrigins.Count - 1].StartsWith($"{ox} {oy} {oz}")))
                _probeOrigins.Add($"{ox} {oy} {oz} -> lo {lo.x:F3} {lo.y:F3} {lo.z:F3}");
            if (++_probeCount >= 120)
            {
                var spread = _probeMaxLo - _probeMinLo;
                Plugin.Log($"scene probe: textured geometry min corner moved {spread.x:F3} {spread.y:F3} {spread.z:F3} over {_probeCount} messages; origins: {string.Join(" | ", _probeOrigins)}");
                _probeCount = 0;
                _probeMinLo = Vector3.positiveInfinity;
                _probeMaxLo = Vector3.negativeInfinity;
                _probeOrigins.Clear();
            }
        }

        // FNV-1a over the payload, 8 bytes at a time.
        static ulong Hash(byte* p, uint bytes)
        {
            ulong h = 14695981039346656037ul;
            ulong* q = (ulong*)p;
            uint words = bytes / 8;
            for (uint i = 0; i < words; i++) { h ^= q[i]; h *= 1099511628211ul; }
            for (uint i = words * 8; i < bytes; i++) { h ^= p[i]; h *= 1099511628211ul; }
            return h ^ bytes;
        }

        static Part NewPart(Transform root, string name)
        {
            var part = new Part { go = new GameObject(name) };
            part.go.transform.SetParent(root, false);
            part.mesh = new Mesh { name = name };
            part.mesh.MarkDynamic();
            part.go.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            part.renderer = part.go.AddComponent<MeshRenderer>();
            part.renderer.shadowCastingMode = ShadowCastingMode.On;
            // The mesh is rewritten every Minecraft frame; per-object motion vectors would read that as
            // movement and Valheim's motion blur smears it. Camera motion only, like static scenery.
            part.renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            return part;
        }

        static readonly List<Vector3> _pos = new List<Vector3>();
        static readonly List<Vector3> _nrm = new List<Vector3>();
        static readonly List<Vector2> _uv = new List<Vector2>();
        static readonly Dictionary<(int tex, int layer, uint colour, bool glow), List<int>> _groups = new Dictionary<(int, int, uint, bool), List<int>>();
        static readonly List<(int tex, int layer, uint colour, bool glow)> _keys = new List<(int, int, uint, bool)>();

        static readonly Vector3[] FaceNormals =
        {
            Vector3.up, new Vector3(0, -1, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1), new Vector3(0, 0, 1), new Vector3(-1, 0, 0), new Vector3(1, 0, 0),
        };

        // Payload: batchCount, vertexCount, RenBatch[batchCount] {texture, first, count, flags}, RenVertex[vertexCount].
        static void Build(Part part, byte* p, uint bytes)
        {
            int batchCount = *(int*)p, vertexCount = *(int*)(p + 4);
            ulong hash = Hash(p, bytes);
            if (hash == part.hash && part.renderer.enabled) return;
            part.hash = hash;
            part.mesh.Clear();
            if (batchCount <= 0 || vertexCount <= 0 || !BlockMaterials.Ready)
            {
                part.renderer.enabled = false;
                return;
            }
            if ((ulong)bytes < 8 + (ulong)batchCount * 16 + (ulong)vertexCount * 32) return;
            int* batches = (int*)(p + 8);
            var v = (RenVertex*)(p + 8 + batchCount * 16);

            _pos.Clear(); _nrm.Clear(); _uv.Clear();
            foreach (var g in _groups.Values) g.Clear();
            for (int b = 0; b < batchCount; b++)
            {
                int tex = batches[b * 4], first = batches[b * 4 + 1], count = batches[b * 4 + 2], bflags = batches[b * 4 + 3];
                if (tex != 0 && !_textures.ContainsKey(tex)) continue;
                int layer = (bflags & 1) != 0 ? 1 : 0;
                for (int t = first; t + 2 < first + count && t + 2 < vertexCount; t += 3)
                {
                    // One colour per triangle (the brightest corner), and glow when it's at full block light.
                    Color32 best = default;
                    int bestSum = -1;
                    bool glow = true;
                    for (int k = 0; k < 3; k++)
                    {
                        uint c = v[t + k].color;
                        var cc = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                        int sum = cc.r + cc.g + cc.b;
                        if (sum > bestSum) { bestSum = sum; best = cc; }
                        glow &= (v[t + k].light & 0xF) >= 15;
                    }
                    if (best.a < 8) continue;  // fully faded particle
                    var key = (tex, layer, BlockMaterials.Quantise(best), glow);
                    if (!_groups.TryGetValue(key, out var list)) _groups[key] = list = new List<int>();
                    var a = new Vector3(v[t].x, v[t].y, v[t].z);
                    var bb = new Vector3(v[t + 1].x, v[t + 1].y, v[t + 1].z);
                    var cv = new Vector3(v[t + 2].x, v[t + 2].y, v[t + 2].z);
                    uint nf = (v[t].flags >> 4) & 7;
                    Vector3 n = nf == 7 ? Vector3.Cross(bb - a, cv - a) : FaceNormals[nf];
                    if (nf == 7) n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                    for (int k = 0; k < 3; k++)
                    {
                        list.Add(_pos.Count);
                        _pos.Add(new Vector3(v[t + k].x, v[t + k].y, v[t + k].z));
                        _uv.Add(new Vector2(v[t + k].u, v[t + k].v));
                        _nrm.Add(n);
                    }
                }
            }
            if (_pos.Count == 0) { part.renderer.enabled = false; return; }
            part.mesh.indexFormat = _pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            part.mesh.SetVertices(_pos);
            part.mesh.SetNormals(_nrm);
            part.mesh.SetUVs(0, _uv);
            _keys.Clear();
            foreach (var kv in _groups) if (kv.Value.Count > 0) _keys.Add(kv.Key);
            part.mesh.subMeshCount = _keys.Count;
            var mats = new Material[_keys.Count];
            for (int i = 0; i < _keys.Count; i++)
            {
                part.mesh.SetTriangles(_groups[_keys[i]], i, false);
                var k = _keys[i];
                mats[i] = BlockMaterials.Get((BlockMaterials.Layer)k.layer, k.colour, k.tex == 0 ? null : _textures[k.tex], k.glow);
            }
            part.mesh.RecalculateBounds();
            part.renderer.sharedMaterials = mats;
            part.renderer.enabled = true;
        }
    }
}
