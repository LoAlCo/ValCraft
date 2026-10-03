using UnityEngine;

namespace ValCraft
{
    // Valheim's ground height with the last terrain tile remembered: Heightmap.GetHeight scans every
    // loaded tile on each call, and block terrain asks hundreds of times per chunk.
    public static class Ground
    {
        static Heightmap _last;

        public static bool Height(Vector3 v, out float h)
        {
            if (_last && _last.IsPointInside(v) && _last.GetWorldHeight(v, out h)) return true;
            _last = Heightmap.FindHeightmap(v);
            if (_last && _last.GetWorldHeight(v, out h)) return true;
            h = 0f;
            return false;
        }

        // Valheim's ground as the world made it, before any digging or raising. Valheim keeps its
        // ground within 8 m of this (DigLimit): no pickaxe gets it lower.
        public const float DigLimit = 8f;

        static readonly System.Reflection.MethodInfo _baseHeight = HarmonyLib.AccessTools.Method(typeof(Heightmap), "GetWorldBaseHeight");
        static readonly object[] _args = new object[2];

        public static bool BaseHeight(Vector3 v, out float h)
        {
            h = 0f;
            var hm = _last && _last.IsPointInside(v) ? _last : Heightmap.FindHeightmap(v);
            if (!hm || _baseHeight == null) return false;
            _args[0] = v;
            _args[1] = 0f;
            if (!(bool)_baseHeight.Invoke(hm, _args)) return false;
            h = (float)_args[1];
            return true;
        }
    }
}
