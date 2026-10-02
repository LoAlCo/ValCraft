using System.Collections.Generic;
using UnityEngine;

namespace ValCraft
{
    // Minecraft explosions (TNT, creepers) blow craters in Valheim's ground. Valheim only replays its
    // own terrain tools (a change is saved as "this tool's settings, here"), so a crater is many of the
    // pickaxe's digs: spread over a circle, repeated deeper towards the middle for a bowl, each placed
    // on the ground as it is by then. They're spread over a few frames so a chain of TNT doesn't stall.
    // Not inside wards or no-build areas (Valheim's rules), and not in block terrain mode (F8), where
    // the ground is Minecraft's blocks and Minecraft's own explosion takes them.
    public static class Craters
    {
        const int OpsPerFrame = 12;
        const int MaxOpsPerCrater = 160;

        struct Dig { public float x, z, floor; public int left; }

        static readonly Queue<Dig> _queue = new Queue<Dig>();
        static GameObject _prefab;
        static float _opRadius = 1f, _opDepth = 0.5f;
        static bool _logged;

        public static void Add(Vector3 centre, float radius, float scale, GameObject digPrefab)
        {
            if (scale <= 0f || BlockTerrain.On || !digPrefab) return;
            if (_prefab != digPrefab) Setup(digPrefab);
            if (!Heightmap.GetHeight(centre, out float ground)) return;  // no Valheim ground here (a dungeon)
            if (centre.y - ground > radius) return;                      // went off in the air: no crater
            float r = radius * 0.9f * scale, depth = radius * 0.5f * scale;
            float spacing = Mathf.Max(0.75f, _opRadius * 0.8f);
            int ops = 0;
            for (float dx = -r; dx <= r && ops < MaxOpsPerCrater; dx += spacing)
            {
                for (float dz = -r; dz <= r && ops < MaxOpsPerCrater; dz += spacing)
                {
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > r) continue;
                    var p = new Vector3(centre.x + dx, 0f, centre.z + dz);
                    if (!Heightmap.GetHeight(p, out float h)) continue;
                    p.y = h;
                    if (!PrivateArea.CheckAccess(p, 0f, false, false) || Location.IsInsideNoBuildLocation(p)) continue;
                    float here = depth * (1f - (d / r) * (d / r));  // a bowl
                    int count = Mathf.Clamp(Mathf.CeilToInt(here / _opDepth), 1, 8);
                    _queue.Enqueue(new Dig { x = p.x, z = p.z, floor = h - here, left = count });
                    ops += count;
                }
            }
        }

        public static void Frame()
        {
            for (int n = 0; n < OpsPerFrame && _queue.Count > 0; n++)
            {
                var dig = _queue.Dequeue();
                var p = new Vector3(dig.x, 0f, dig.z);
                if (!_prefab || !Heightmap.GetHeight(p, out float h) || h <= dig.floor + 0.05f) continue;
                p.y = h;
                Object.Instantiate(_prefab, p, Quaternion.identity);  // the dig applies itself and goes away
                if (--dig.left > 0) _queue.Enqueue(dig);              // again later, from the lowered ground
            }
        }

        // How wide and deep one of the pickaxe's digs is, from its own settings.
        static void Setup(GameObject prefab)
        {
            _prefab = prefab;
            var op = prefab.GetComponentInChildren<TerrainOp>();
            if (op != null)
            {
                var s = op.m_settings;
                _opRadius = Mathf.Max(0.5f, s.GetRadius());
                if (s.m_raise && s.m_raiseDelta < 0f) _opDepth = Mathf.Max(0.1f, -s.m_raiseDelta);
            }
            if (!_logged)
            {
                _logged = true;
                Plugin.Log($"craters: built from {prefab.name} digs (radius {_opRadius:F2} m, depth {_opDepth:F2} m each)");
            }
        }
    }
}
