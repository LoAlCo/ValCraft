using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ValCraft.Link
{
    // Owner of the shared-memory mapping: Valheim creates it, Minecraft opens it.
    // All producer/consumer rings are single-producer single-consumer; x64 keeps stores in order, so
    // Volatile reads/writes on the head/tail words are all the fencing needed.
    public static unsafe class Shm
    {
        const uint PAGE_READWRITE = 0x04;
        const uint FILE_MAP_ALL_ACCESS = 0xF001F;
        const int ERROR_ALREADY_EXISTS = 183;
        const ulong McTimeoutMs = 3000;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr attributes, uint protect, uint maxHigh, uint maxLow, string name);

        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offHigh, uint offLow, UIntPtr bytes);

        [DllImport("kernel32")]
        public static extern ulong GetTickCount64();

        [DllImport("kernel32")]
        static extern uint GetCurrentProcessId();

        [DllImport("kernel32")]
        static extern bool QueryPerformanceCounter(out long value);

        [DllImport("kernel32")]
        static extern bool QueryPerformanceFrequency(out long value);

        static byte* _base;
        static uint _overlayFront = 2;

        public static bool Valid => _base != null;
        public static byte* Base => _base;

        public static long Qpc() { QueryPerformanceCounter(out var v); return v; }
        public static readonly long QpcFrequency = GetFreq();
        static long GetFreq() { QueryPerformanceFrequency(out var f); return f; }

        static T* At<T>(long off) where T : unmanaged => (T*)(_base + off);

        public static bool Create()
        {
            if (_base != null) return true;
            ulong size = (ulong)Proto.MappingBytes;
            var mapping = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE, (uint)(size >> 32), (uint)(size & 0xFFFFFFFF), Proto.MappingName);
            if (mapping == IntPtr.Zero)
            {
                Plugin.Error($"CreateFileMapping failed ({Marshal.GetLastWin32Error()})");
                return false;
            }
            bool existed = Marshal.GetLastWin32Error() == ERROR_ALREADY_EXISTS;
            var view = MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, UIntPtr.Zero);
            if (view == IntPtr.Zero)
            {
                Plugin.Error($"MapViewOfFile failed ({Marshal.GetLastWin32Error()})");
                return false;
            }
            _base = (byte*)view;

            // A stale mapping survives if Minecraft still has it open from a previous Valheim run:
            // reset everything we own so rings and the overlay swap start from a known state.
            Clear(Proto.OffValState, sizeof(ValState));
            Clear(Proto.OffOverlayCtl, 0x100);
            Clear(Proto.OffInputRing, Proto.InputRingDataOff);
            Clear(Proto.OffCollisionRing, Proto.ColRingDataOff);
            Clear(Proto.OffActorTable, 0x40 + 64 * Proto.MaxActors);
            Clear(Proto.OffEventRing, Proto.EventRingDataOff);
            Clear(Proto.OffWorldEntities, 0x40);
            Clear(Proto.OffRenderRing, Proto.RenRingDataOff);
            var h = At<Header>(Proto.OffHeader);
            h->version = Proto.Version;
            h->hostPid = GetCurrentProcessId();
            h->hostHeartbeatMs = GetTickCount64();
            Volatile.Write(ref h->magic, Proto.Magic);
            Plugin.Log($"shared memory {Proto.MappingName} ({size >> 20} MB, {(existed ? "reused" : "created")})");
            return true;
        }

        static void Clear(long off, long bytes)
        {
            byte* p = _base + off;
            for (long i = 0; i < bytes; i++) p[i] = 0;
        }

        public static void Heartbeat()
        {
            if (_base == null) return;
            Volatile.Write(ref At<Header>(Proto.OffHeader)->hostHeartbeatMs, GetTickCount64());
        }

        public static bool McAlive()
        {
            if (_base == null) return false;
            ulong last = Volatile.Read(ref At<Header>(Proto.OffHeader)->mcHeartbeatMs);
            return last != 0 && GetTickCount64() - last < McTimeoutMs;
        }

        public static uint McPid() => _base == null ? 0 : Volatile.Read(ref At<Header>(Proto.OffHeader)->mcPid);

        // ---- seqlocked state blocks -------------------------------------------------------------

        public static void WriteValState(ref ValState st)
        {
            if (_base == null) return;
            var dst = At<ValState>(Proto.OffValState);
            uint s = dst->seq;
            Volatile.Write(ref dst->seq, s + 1);
            Thread.MemoryBarrier();
            st.seq = s + 1;
            *dst = st;
            Volatile.Write(ref dst->seq, s + 2);
        }

        public static void WriteWaterGrid(int originX, int originZ, uint worldId, float[] surface)
        {
            if (_base == null) return;
            byte* b = _base + Proto.OffWaterGrid;
            uint* seq = (uint*)b;
            uint s = *seq;
            Volatile.Write(ref *seq, s + 1);
            Thread.MemoryBarrier();
            *(int*)(b + 4) = originX;
            *(int*)(b + 8) = originZ;
            *(uint*)(b + 12) = worldId;
            float* f = (float*)(b + 16);
            for (int i = 0; i < Proto.WaterGridSize * Proto.WaterGridSize; i++) f[i] = surface[i];
            Volatile.Write(ref *seq, s + 2);
        }

        public static bool ReadMcState(out McState st)
        {
            st = default;
            if (_base == null) return false;
            var src = At<McState>(Proto.OffMcState);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint s1 = Volatile.Read(ref src->seq);
                if ((s1 & 1) != 0) { Thread.SpinWait(8); continue; }
                st = *src;
                Thread.MemoryBarrier();
                if (Volatile.Read(ref src->seq) == s1) return true;
            }
            return false;
        }

        public static void WriteActors(ActorRecord[] records, int count)
        {
            if (_base == null) return;
            byte* b = _base + Proto.OffActorTable;
            uint* seq = (uint*)b;
            uint s = *seq;
            Volatile.Write(ref *seq, s + 1);
            Thread.MemoryBarrier();
            count = Math.Min(count, Proto.MaxActors);
            *(uint*)(b + 4) = (uint)count;
            var dst = (ActorRecord*)(b + 0x40);
            for (int i = 0; i < count; i++) dst[i] = records[i];
            Volatile.Write(ref *seq, s + 2);
        }

        // ---- input ring (producer) ----------------------------------------------------------------

        public static void PushInput(ushort type, ushort code, int a = 0, int b = 0, int c = 0)
        {
            if (_base == null) return;
            byte* ring = _base + Proto.OffInputRing;
            ulong* head = (ulong*)(ring + Proto.InputRingHeadOff);
            ulong* tail = (ulong*)(ring + Proto.InputRingTailOff);
            ulong h = *head;
            ulong t = Volatile.Read(ref *tail);
            if (h - t >= Proto.InputRingEntries) return;  // Minecraft a full ring behind: drop
            byte* e = ring + Proto.InputRingDataOff + (long)(h & (Proto.InputRingEntries - 1)) * 16;
            *(ushort*)e = type;
            *(ushort*)(e + 2) = code;
            *(int*)(e + 4) = a;
            *(int*)(e + 8) = b;
            *(int*)(e + 12) = c;
            Volatile.Write(ref *head, h + 1);
        }

        // ---- collision ring (producer, one thread only) -------------------------------------------

        public static bool WriteCollision(uint type, byte[] payload, int bytes)
        {
            if (_base == null) return false;
            byte* ring = _base + Proto.OffCollisionRing;
            ulong* headRef = (ulong*)(ring + Proto.ColRingHeadOff);
            ulong* tailRef = (ulong*)(ring + Proto.ColRingTailOff);
            byte* data = ring + Proto.ColRingDataOff;
            ulong size = (ulong)Proto.ColRingDataBytes;
            ulong msgBytes = (ulong)(8 + bytes + 7) & ~7ul;
            if (msgBytes > size / 2)
            {
                Plugin.Error($"collision message too large ({msgBytes} bytes)");
                return false;
            }
            ulong head = *headRef;
            ulong tail = Volatile.Read(ref *tailRef);
            ulong pos = head % size;
            ulong pad = pos + msgBytes > size ? size - pos : 0;
            if (size - (head - tail) < msgBytes + pad) return false;
            if (pad > 0)
            {
                *(uint*)(data + pos) = Proto.ColPad;
                *(uint*)(data + pos + 4) = 0;
                head += pad;
                pos = 0;
            }
            *(uint*)(data + pos) = type;
            *(uint*)(data + pos + 4) = (uint)bytes;
            if (bytes > 0) Marshal.Copy(payload, 0, (IntPtr)(data + pos + 8), bytes);
            Volatile.Write(ref *headRef, head + msgBytes);
            return true;
        }

        // ---- event ring (consumer) ----------------------------------------------------------------

        public static bool PopEvent(out McEvent ev)
        {
            ev = default;
            if (_base == null) return false;
            byte* ring = _base + Proto.OffEventRing;
            ulong* headRef = (ulong*)(ring + Proto.EventRingHeadOff);
            ulong* tailRef = (ulong*)(ring + Proto.EventRingTailOff);
            ulong head = Volatile.Read(ref *headRef);
            ulong tail = *tailRef;
            if (tail >= head) return false;
            if (head - tail > Proto.EventRingEntries) tail = head - Proto.EventRingEntries;
            ev = ((McEvent*)(ring + Proto.EventRingDataOff))[tail & (Proto.EventRingEntries - 1)];
            Volatile.Write(ref *tailRef, tail + 1);
            return true;
        }

        // ---- render ring (consumer) ---------------------------------------------------------------

        public delegate void RenderSink(uint type, byte* payload, uint bytes);

        // deadline: a Stopwatch timestamp to stop at (the rest waits in the ring for the next frame); 0 for none.
        public static void DrainRender(RenderSink sink, ulong maxBytes, long deadline = 0)
        {
            if (_base == null) return;
            byte* ring = _base + Proto.OffRenderRing;
            ulong* headRef = (ulong*)(ring + Proto.RenRingHeadOff);
            ulong* tailRef = (ulong*)(ring + Proto.RenRingTailOff);
            byte* data = ring + Proto.RenRingDataOff;
            ulong size = (ulong)Proto.RenRingDataBytes;
            ulong head = Volatile.Read(ref *headRef);
            ulong tail = *tailRef;
            ulong done = 0;
            while (tail < head && done < maxBytes && (deadline == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < deadline))
            {
                ulong pos = tail % size;
                uint type = *(uint*)(data + pos);
                uint bytes = *(uint*)(data + pos + 4);
                if (type == Proto.RenPad) { tail += size - pos; continue; }
                sink?.Invoke(type, data + pos + 8, bytes);
                ulong msg = (8ul + bytes + 7) & ~7ul;
                tail += msg;
                done += msg;
            }
            Volatile.Write(ref *tailRef, tail);
        }

        // ---- overlay triple buffer (consumer) -----------------------------------------------------

        public static bool AcquireOverlayFrame()
        {
            if (_base == null) return false;
            int* state = (int*)(_base + Proto.OffOverlayCtl);
            if ((Volatile.Read(ref *state) & (int)Proto.OverlayDirty) == 0) return false;
            int old = Interlocked.Exchange(ref *state, (int)_overlayFront);
            _overlayFront = (uint)old & 3;
            return true;
        }

        public static void ResetOverlay()
        {
            if (_base == null) return;
            Volatile.Write(ref *(int*)(_base + Proto.OffOverlayCtl), 0);
            _overlayFront = 2;
        }

        public static byte* FrontPixels => _base + Proto.OffOverlayPixels + Proto.OverlaySlotBytes * _overlayFront;
        public static OverlaySlotHdr* FrontHeader => (OverlaySlotHdr*)(_base + Proto.OffOverlaySlotHdr + 0x40 * _overlayFront);

        public static void CheckLayout()
        {
            // Same checks as the static_asserts in valcraft_protocol.h.
            Expect(sizeof(Header) == 0x20, "Header");
            Expect(sizeof(ValState) == 0x90, "ValState");
            Expect(sizeof(McState) == 0x100, "McState");
            Expect(sizeof(McEvent) == 32, "McEvent");
            Expect(sizeof(ColRegion) == 32, "ColRegion");
            Expect(sizeof(ColBlock) == 80, "ColBlock");
            Expect(sizeof(ColTri) == 40, "ColTri");
            Expect(sizeof(ActorRecord) == 64, "ActorRecord");
            Expect(sizeof(RenVertex) == 32, "RenVertex");
        }

        static void Expect(bool ok, string what)
        {
            if (!ok) throw new Exception("ValCraft protocol layout mismatch: " + what);
        }
    }
}
