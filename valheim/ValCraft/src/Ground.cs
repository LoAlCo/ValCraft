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
    }
}
