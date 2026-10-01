using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Valheim's water surface over the block columns around the player, so Minecraft swims, floats
    // and drowns in Valheim's sea (waves included).
    public static class Water
    {
        static readonly float[] _surface = new float[Proto.WaterGridSize * Proto.WaterGridSize];

        public static void Write(Vector3d centreMc, uint worldId)
        {
            const int size = Proto.WaterGridSize;
            int ox = Mathf.FloorToInt((float)centreMc.x) - size / 2;
            int oz = Mathf.FloorToInt((float)centreMc.z) - size / 2;
            float probeY = (float)(centreMc.y + Coords.YOffset);
            for (int dz = 0; dz < size; dz++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    var p = Coords.ToValheim(ox + dx + 0.5, 0, oz + dz + 0.5);
                    // Sample at the player's height and just under sea level: water volumes are boxes
                    // that don't always reach far above their surface.
                    p.y = probeY;
                    float h = Floating.GetLiquidLevel(p, 1f, LiquidType.Water);
                    p.y = 29f;
                    h = Mathf.Max(h, Floating.GetLiquidLevel(p, 1f, LiquidType.Water));
                    _surface[dz * size + dx] = h > -5000f ? (float)(h - Coords.YOffset) : Proto.NoWater;
                }
            }
            Shm.WriteWaterGrid(ox, oz, worldId, _surface);
        }
    }
}
