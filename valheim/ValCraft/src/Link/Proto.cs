using System.Runtime.InteropServices;

namespace ValCraft.Link
{
    // Mirror of protocol/valcraft_protocol.h (the Java side mirrors it in fabric/.../link/Proto.java).
    // Coordinates in the protocol are Minecraft space (blocks, Y up, Z south); see Coords.
    public static class Proto
    {
        public const uint Magic = 0x434C4156;  // "VALC"
        public const uint Version = 10;
        public const string MappingName = "Local\\ValCraft_v1";

        public const long OffHeader = 0x0;
        public const long OffValState = 0x100;
        public const long OffMcState = 0x200;
        public const long OffOverlayCtl = 0x300;
        public const long OffOverlaySlotHdr = 0x340;
        public const long OffWaterGrid = 0x400;
        public const long OffInputRing = 0x1000;
        public const long OffCollisionRing = 0x20000;
        public const long CollisionRingBytes = 32L << 20;
        public const long OffOverlayPixels = OffCollisionRing + CollisionRingBytes;
        public const int MaxOverlayW = 3840;
        public const int MaxOverlayH = 2160;
        public const long OverlaySlotBytes = (long)MaxOverlayW * MaxOverlayH * 4;
        public const int OverlaySlots = 3;
        public const long OffActorTable = 0x12000;
        public const long OffEventRing = 0x17000;
        public const long OffWorldEntities = 0x1C000;
        public const long OffRenderRing = OffOverlayPixels + OverlaySlotBytes * OverlaySlots;
        public const long RenderRingBytes = 64L << 20;
        public const long MappingBytes = OffRenderRing + RenderRingBytes;

        // ValState.flags
        public const uint ValInGame = 1u << 0;
        public const uint ValMenuOpen = 1u << 1;
        public const uint ValLoading = 1u << 2;
        public const uint ValBlockTerrain = 1u << 3;  // ValCraft: block terrain on (Minecraft opens the -blocks save)

        // McState.flags
        public const uint McInWorld = 1u << 0;
        public const uint McScreenOpen = 1u << 1;
        public const uint McOnGround = 1u << 2;
        public const uint McSneaking = 1u << 3;
        public const uint McSprinting = 1u << 4;
        public const uint McDead = 1u << 5;
        public const uint McSwimming = 1u << 6;
        public const uint McFlying = 1u << 7;

        // overlay triple buffer
        public const uint OverlayDirty = 1u << 2;

        // water grid
        public const int WaterGridSize = 16;
        public const float NoWater = -1.0e30f;

        // input ring (Valheim produces, MC consumes)
        public const uint InputRingEntries = 4096;
        public const long InputRingHeadOff = 0x00;
        public const long InputRingTailOff = 0x40;
        public const long InputRingDataOff = 0x80;

        public const ushort InKey = 1;          // code = SDL scancode, a = 1 press / 0 release
        public const ushort InMouseButton = 2;  // code = SDL button (1 L, 2 M, 3 R, 4 X1, 5 X2)
        public const ushort InScroll = 3;       // a = notches * 120
        public const ushort InCursor = 4;       // a, b = cursor in overlay pixels (top-left origin)
        public const ushort InText = 5;         // a = code point
        public const ushort InReleaseAll = 6;
        public const ushort InHurt = 7;         // code = HurtKind, a = damage * 100, b = attacker id, c = flags
        public const ushort InOpenMenu = 8;
        public const ushort InGive = 9;        // ValCraft: give the player a Minecraft item: a = count, b = id length; InGiveData follows
        public const ushort InGiveData = 10;   // 12 bytes of the UTF-8 item id in a, b, c

        // actor table
        public const int MaxActors = 256;

        // event ring (MC produces, Valheim consumes)
        public const uint EventRingEntries = 512;
        public const long EventRingHeadOff = 0x00;
        public const long EventRingTailOff = 0x40;
        public const long EventRingDataOff = 0x80;

        // collision ring (Valheim produces, MC consumes)
        public const long ColRingHeadOff = 0x00;
        public const long ColRingTailOff = 0x40;
        public const long ColRingDataOff = 0x80;
        public const long ColRingDataBytes = CollisionRingBytes - ColRingDataOff;
        public const uint ColPad = 0;
        public const uint ColClear = 1;
        public const uint ColRegion = 2;
        public const uint ColTris = 3;
        public const uint ColTerrain = 4;  // ValCraft block terrain: a chunk's column heights and biomes (see BlockTerrain)
        public const uint TriStairHelper = 1u << 0;

        // render ring (MC produces, Valheim consumes)
        public const long RenRingHeadOff = 0x00;
        public const long RenRingTailOff = 0x40;
        public const long RenRingDataOff = 0x80;
        public const long RenRingDataBytes = RenderRingBytes - RenRingDataOff;
        public const uint RenPad = 0;
        public const uint RenAtlas = 1;
        public const uint RenSection = 2;
        public const uint RenClearAll = 3;
        public const uint RenTexture = 4;
        public const uint RenAvatar = 5;
        public const uint RenScene = 6;
        public const uint RenAtlasRegion = 7;
        public const uint RenLights = 8;
        public const uint RenRagdoll = 9;
        public const uint RenSolids = 10;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Header
    {
        public uint magic;
        public uint version;
        public uint hostPid;
        public uint mcPid;
        public ulong hostHeartbeatMs;
        public ulong mcHeartbeatMs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ValState
    {
        public uint seq;
        public uint flags;
        public uint worldId;
        public uint collisionEpoch;
        public double posX, posY, posZ;  // Valheim player's feet, MC coords
        public float yaw, pitch;         // MC degrees
        public uint teleportSeq;
        public uint viewportW, viewportH;
        public float gameHour;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct McState
    {
        public uint seq;
        public uint flags;
        public double x, y, z;
        public float yaw, pitch;
        public float eyeHeight;
        public float sensitivity;
        public uint teleportAck;
        public uint guiScale;
        public ulong frameCounter;
        public float fovDeg;
        public float bobPhase;
        public float bobAmount;
        public uint pad4C;
        public double eyeX, eyeY, eyeZ;
        public long tickQpc;
        public double prevX, prevY, prevZ;
        public double curX, curY, curZ;
        public float tickEyeO, tickEye;
        public float walkDistO, walkDist;
        public float bobO, bob;
        public float tickMs;
        public uint tickPad;
        public uint cameraMode;
        public float cameraDistance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OverlaySlotHdr
    {
        public uint width;
        public uint height;
        public uint flags;  // bit0: rows bottom-up
        public uint pad;
        public ulong frameId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct McEvent
    {
        public uint type;
        public uint formId;
        public float a, b, c, d;
        public uint flags;
        public uint weapon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ColRegion
    {
        public int minX, minY, minZ;
        public int maxX, maxY, maxZ;
        public uint epoch;
        public uint count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ColBlock
    {
        public int x, y, z;
        public uint pad;
        public fixed ulong bits[8];  // bits[y] bit (z * 8 + x): sub-voxel (x, y, z), each 1/8 block
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ColTri
    {
        public fixed float v[9];
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ActorRecord
    {
        public uint formId;
        public uint flags;
        public float x, y, z;
        public float yaw;
        public float width;
        public float height;
        public float healthFrac;
        public ushort level;
        public ushort pad;
        public unsafe fixed byte name[24];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RenVertex
    {
        public float x, y, z;
        public float u, v;
        public uint color;
        public uint light;
        public uint flags;
    }
}
