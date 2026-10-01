using UnityEngine;

namespace ValCraft
{
    // Valheim (Unity: metres, Y up, +Z north, left-handed) <-> Minecraft (blocks, Y up, +Z south).
    // 1 block = 1 metre, so only Z flips.
    //
    // Valheim's dungeons, crypts and caves sit about 5000 m straight above their entrances: far above
    // Minecraft's build height, and on top of whatever the player built at the entrance. While the
    // player is in one, Minecraft space is shifted: down by InteriorShiftY and out by InteriorShiftX,
    // so interiors get Minecraft space of their own (a different "world" too, see Puppet.WorldIdFor).
    public static class Coords
    {
        public const float InteriorAltitude = 3000f;  // Valheim y above this: a dungeon/interior
        public const double InteriorShiftY = 5000.0;
        public const double InteriorShiftX = 100000.0;

        public static double YOffset;  // subtracted from Valheim y
        public static double XOffset;  // added to Valheim x

        public static void UpdateFor(Vector3 valheimPos)
        {
            bool interior = valheimPos.y > InteriorAltitude;
            YOffset = interior ? InteriorShiftY : 0.0;
            XOffset = interior ? InteriorShiftX : 0.0;
        }

        public static bool Interior => YOffset != 0.0;

        public static Vector3d ToMc(Vector3 p) => new Vector3d(p.x + XOffset, p.y - YOffset, -p.z);
        public static Vector3 ToValheim(double x, double y, double z) => new Vector3((float)(x - XOffset), (float)(y + YOffset), (float)-z);
        public static Vector3 ToValheim(Vector3d p) => ToValheim(p.x, p.y, p.z);

        // Bulk geometry (collision) in Minecraft space, as floats.
        public static Vector3 ToMcF(Vector3 p) => new Vector3((float)(p.x + XOffset), (float)(p.y - YOffset), -p.z);

        public static Vector3 DirToMc(Vector3 d) => new Vector3(d.x, d.y, -d.z);

        // The transform that puts Minecraft-space geometry into Valheim: mirror Z, then undo the offsets.
        public static Vector3 McRootPosition => new Vector3((float)-XOffset, (float)YOffset, 0f);
        public static readonly Vector3 McRootScale = new Vector3(1f, 1f, -1f);

        // Unity yaw (degrees about +Y, 0 = +Z north, clockwise from above) <-> MC yaw (0 = +Z south).
        public static float McYawToUnity(float yaw) => yaw + 180f;
        public static float UnityYawToMc(float yaw) => Mathf.Repeat(yaw + 180f, 360f);
    }

    public struct Vector3d
    {
        public double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double DistanceTo(Vector3d o)
        {
            double dx = x - o.x, dy = y - o.y, dz = z - o.z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public override string ToString() => $"({x:F2} {y:F2} {z:F2})";
    }
}
