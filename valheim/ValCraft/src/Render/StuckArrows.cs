using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValCraft.Render
{
    // Minecraft arrows that stick in Valheim creatures stay in them, Minecraft style: pinned to the
    // nearest bone of the creature's skeleton at the spot they hit, pointing the way they flew, so
    // they move with its animation. Local only (other players don't see Minecraft arrows).
    public static class StuckArrows
    {
        const int MaxPerCreature = 12;
        const float Lifetime = 60f;
        const float Length = 0.5f, Width = 0.08f, Embed = 0.18f;  // metres; Embed: how deep the tip goes in

        static Vector4 _plainRect;   // the plain arrow's side view in the atlas {u0, v0, u1, v1}, from flying arrows
        static bool _haveRect;
        static readonly Dictionary<int, Mesh> _meshes = new Dictionary<int, Mesh>();
        static readonly Dictionary<Character, List<GameObject>> _stuck = new Dictionary<Character, List<GameObject>>();

        // Entities sees every flying arrow's atlas rect; the plain one is the base for all three kinds.
        public static void NoteArrowRect(Vector4 rect, int atlasWidth)
        {
            if (_haveRect || atlasWidth <= 0) return;
            // The three arrow textures sit side by side from x = 160 (after the 10 crack stages): back
            // any tipped/spectral one off to the plain one.
            int variant = Mathf.Clamp(Mathf.RoundToInt((rect.x * atlasWidth - 160f) / 32f), 0, 2);
            float back = variant * 32f / atlasWidth;
            rect.x -= back;
            rect.z -= back;
            _plainRect = rect;
            _haveRect = true;
            _atlasWidth = atlasWidth;
        }
        static int _atlasWidth;

        // yaw/pitch: Minecraft's projectile angles, facing (sin yaw cos pitch, sin pitch, cos yaw cos pitch).
        public static void Stick(Character target, Vector3d hitMc, float yawDeg, float pitchDeg, int variant)
        {
            if (!target || !_haveRect || !BlockMaterials.Ready) return;
            float yr = yawDeg * Mathf.Deg2Rad, pr = pitchDeg * Mathf.Deg2Rad;
            var dirMc = new Vector3(Mathf.Sin(yr) * Mathf.Cos(pr), Mathf.Sin(pr), Mathf.Cos(yr) * Mathf.Cos(pr));
            var dir = new Vector3(dirMc.x, dirMc.y, -dirMc.z).normalized;  // Minecraft -> Valheim (Z flips)
            var hit = Coords.ToValheim(hitMc);

            var bone = NearestBone(target, hit);
            var go = new GameObject("minecraft arrow");
            go.transform.SetParent(bone, true);
            go.transform.position = hit + dir * (Embed - Length * 0.5f);
            go.transform.rotation = Quaternion.LookRotation(dir);
            // Bones can be scaled; keep the arrow its true size.
            var s = bone.lossyScale;
            go.transform.localScale = new Vector3(SafeInv(s.x), SafeInv(s.y), SafeInv(s.z));
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(Mathf.Clamp(variant, 0, 2));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = BlockMaterials.Get(BlockMaterials.Layer.Cutout, 0xFFFFFFFFu);
            r.shadowCastingMode = ShadowCastingMode.On;
            Object.Destroy(go, Lifetime * Random.Range(0.8f, 1.2f));

            if (!_stuck.TryGetValue(target, out var list)) _stuck[target] = list = new List<GameObject>();
            list.RemoveAll(a => !a);
            list.Add(go);
            while (list.Count > MaxPerCreature) { if (list[0]) Object.Destroy(list[0]); list.RemoveAt(0); }
            if (_stuck.Count > 64)
            {
                var gone = new List<Character>();
                foreach (var kv in _stuck) if (!kv.Key) gone.Add(kv.Key);
                foreach (var c in gone) _stuck.Remove(c);
            }
        }

        static float SafeInv(float v) => Mathf.Abs(v) < 1e-4f ? 1f : 1f / v;

        static Transform NearestBone(Character c, Vector3 at)
        {
            Transform best = c.transform;
            float bestD = float.MaxValue;
            var visual = c.GetVisual();
            if (!visual) return best;
            foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.bones == null) continue;
                foreach (var b in smr.bones)
                {
                    if (!b) continue;
                    float d = (b.position - at).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = b; }
                }
            }
            return best;
        }

        // Two crossed quads along +Z, both sides; the arrow's side view runs fletching (u0) to tip (u1).
        static Mesh MeshFor(int variant)
        {
            if (_meshes.TryGetValue(variant, out var m) && m) return m;
            float shift = _atlasWidth > 0 ? variant * 32f / _atlasWidth : 0f;  // the three arrow textures sit 32 px apart
            float u0 = _plainRect.x + shift, u1 = _plainRect.z + shift, v0 = _plainRect.y, v1 = _plainRect.w;
            var pos = new List<Vector3>();
            var uv = new List<Vector2>();
            var nrm = new List<Vector3>();
            var tri = new List<int>();
            float h = Length * 0.5f;
            void Quad(Vector3 side, Vector3 n)
            {
                foreach (var face in new[] { 1f, -1f })
                {
                    int i = pos.Count;
                    pos.Add(new Vector3(0, 0, -h) - side * Width); uv.Add(new Vector2(u0, v1));
                    pos.Add(new Vector3(0, 0, h) - side * Width); uv.Add(new Vector2(u1, v1));
                    pos.Add(new Vector3(0, 0, h) + side * Width); uv.Add(new Vector2(u1, v0));
                    pos.Add(new Vector3(0, 0, -h) + side * Width); uv.Add(new Vector2(u0, v0));
                    for (int k = 0; k < 4; k++) nrm.Add(n * face);
                    if (face > 0) { tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3); }
                    else { tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); tri.Add(i); tri.Add(i + 3); tri.Add(i + 2); }
                }
            }
            Quad(Vector3.up, Vector3.right);
            Quad(Vector3.right, Vector3.up);
            m = new Mesh { name = "minecraft arrow " + variant };
            m.SetVertices(pos);
            m.SetUVs(0, uv);
            m.SetNormals(nrm);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            _meshes[variant] = m;
            return m;
        }
    }
}
