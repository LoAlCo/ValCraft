using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Streams Valheim's collision around the player to Minecraft (SkyCraft's Collision.cpp, with
    // Unity physics instead of Havok). Main thread: picks which 8x8x8-block regions need (re)sending
    // and copies out every collider's triangles / primitives touching one. Worker thread: voxelizes
    // them to 1/8-block occupancy and writes regions and exact triangles to the collision ring.
    public static class Collision
    {
        public const int RegionSize = 8;  // blocks per region edge (must match ValCollision.REGION_SIZE)
        const int Radius = 5;             // regions around the player horizontally
        const int Below = 3, Above = 2;
        const int Grid = RegionSize * 8;  // voxels per region edge (64)
        const float RefreshNear = 1.0f;   // seconds between re-sends of the regions next to the player
        const double FrameBudgetMs = 2.5;
        const int MaxHarvestsPerFrame = 3;
        const float SteepMin = 0.1f;      // |n.y| below this: a wall, kept fine-grained
        const float SteepMax = 0.643f;    // |n.y| below this (steeper than ~50 deg): block-coarsened
        const float PrimMargin = 0.5f;    // voxels; lets thin primitives still register

        static int _mask;
        static readonly Collider[] _overlap = new Collider[1024];
        static readonly List<Vector3Int> _offsets = new List<Vector3Int>();
        static readonly Dictionary<long, float> _harvested = new Dictionary<long, float>();
        static readonly BlockingCollection<Job> _queue = new BlockingCollection<Job>(new ConcurrentQueue<Job>());
        static Thread _worker;
        static volatile uint _epoch;
        static readonly Dictionary<int, MeshData> _meshCache = new Dictionary<int, MeshData>();
        static readonly HashSet<int> _loggedUnreadable = new HashSet<int>();
        public static Transform OwnRoot;  // our own colliders (Minecraft blocks for NPCs): never exported back

        // stats (diagnostics)
        public static int StatRegions, StatTris, StatBoxes, StatUnreadable;

        class Job
        {
            public int rx, ry, rz;
            public uint epoch;
            public bool clear;
            public List<float> tris = new List<float>();     // 9 floats per triangle, MC space
            public List<Obb> boxes = new List<Obb>();
            public List<Capsule> capsules = new List<Capsule>();
            public List<float> rayVoxels = new List<float>(); // points known to be on a surface (unreadable meshes)
        }

        struct Obb { public Vector3 c, ax, ay, az; public Vector3 half; }   // MC space; unit axes
        struct Capsule { public Vector3 a, b; public float r; }            // MC space

        class MeshData { public Vector3[] verts; public int[] tris; public Bounds bounds; }

        public static void Start()
        {
            _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle");
            for (int dx = -Radius; dx <= Radius; dx++)
                for (int dz = -Radius; dz <= Radius; dz++)
                    for (int dy = -Below; dy <= Above; dy++)
                        _offsets.Add(new Vector3Int(dx, dy, dz));
            _offsets.Sort((a, b) => (a.x * a.x + a.z * a.z + a.y * a.y * 2).CompareTo(b.x * b.x + b.z * b.z + b.y * b.y * 2));
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ValCraft collision" };
            _worker.Start();
        }

        public static void Reset(uint epoch)
        {
            _epoch = epoch;
            _harvested.Clear();
            _meshCache.Clear();
            _doorOf.Clear();
            while (_queue.TryTake(out _)) { }
            _queue.Add(new Job { clear = true, epoch = epoch });
        }

        static long Key(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        // Something moved (a door swinging open or shut): its regions go again now and as it moves.
        static readonly List<(float at, Vector3 pos, float radius)> _invalidations = new List<(float, Vector3, float)>();

        // Doors mid-swing (until the time given) are left out of Minecraft's collision altogether: an
        // opening door is passable at once, and solid again where it ends up once it stops.
        static readonly Dictionary<Door, float> _swinging = new Dictionary<Door, float>();
        static readonly Dictionary<int, Door> _doorOf = new Dictionary<int, Door>();
        public const float DoorSwingSeconds = 1.2f;

        public static void DoorMoved(Door door)
        {
            _swinging[door] = Time.realtimeSinceStartup + DoorSwingSeconds;
            Invalidate(door.transform.position, 3f, 0f, DoorSwingSeconds + 0.05f, DoorSwingSeconds + 0.8f);
        }

        static bool IsSwingingDoor(Collider col)
        {
            if (_swinging.Count == 0) return false;
            int id = col.GetInstanceID();
            if (!_doorOf.TryGetValue(id, out var door))
            {
                door = col.GetComponentInParent<Door>();
                if (_doorOf.Count > 8192) _doorOf.Clear();
                _doorOf[id] = door;
            }
            if (!door || !_swinging.TryGetValue(door, out float until)) return false;
            if (Time.realtimeSinceStartup < until) return true;
            _swinging.Remove(door);
            return false;
        }

        public static void Invalidate(Vector3 valheimPos, float radius, params float[] delays)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var d in delays) _invalidations.Add((now + d, valheimPos, radius));
        }

        static void ProcessInvalidations()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _invalidations.Count - 1; i >= 0; i--)
            {
                var (at, pos, radius) = _invalidations[i];
                if (at > now) continue;
                _invalidations.RemoveAt(i);
                var c = Coords.ToMcF(pos);
                int x0 = Mathf.FloorToInt((c.x - radius) / RegionSize), x1 = Mathf.FloorToInt((c.x + radius) / RegionSize);
                int y0 = Mathf.FloorToInt((c.y - radius) / RegionSize), y1 = Mathf.FloorToInt((c.y + radius) / RegionSize);
                int z0 = Mathf.FloorToInt((c.z - radius) / RegionSize), z1 = Mathf.FloorToInt((c.z + radius) / RegionSize);
                for (int rx = x0; rx <= x1; rx++)
                    for (int ry = y0; ry <= y1; ry++)
                        for (int rz = z0; rz <= z1; rz++)
                            _harvested.Remove(Key(rx, ry, rz));
            }
        }

        // Moving fast (sprint-jumping downhill, an elytra, a minecart), the regions ahead are sent
        // first and more go per frame: arriving somewhere Minecraft has no collision for yet lets
        // the player straight into the ground.
        const float FastSpeed = 6f;                       // blocks per second
        static readonly float[] Lookahead = { 0.25f, 0.5f, 0.75f, 1.0f, 1.5f };  // seconds

        public static void Update(Vector3d playerMc, Vector3 velocityMc)
        {
            ProcessInvalidations();
            int prx = Mathf.FloorToInt((float)playerMc.x / RegionSize);
            int pry = Mathf.FloorToInt((float)playerMc.y / RegionSize);
            int prz = Mathf.FloorToInt((float)playerMc.z / RegionSize);
            float now = Time.realtimeSinceStartup;
            var sw = Stopwatch.StartNew();
            int done = 0;
            float speed = velocityMc.magnitude;
            bool fast = speed > FastSpeed;
            int maxHarvests = fast ? 10 : MaxHarvestsPerFrame;
            double budget = fast ? 6.0 : FrameBudgetMs;
            if (fast)
            {
                var p = new Vector3((float)playerMc.x, (float)playerMc.y, (float)playerMc.z);
                foreach (float t in Lookahead)
                {
                    var ahead = p + velocityMc * t;
                    if ((ahead - p).sqrMagnitude > 64f * 64f) break;
                    int ax = Mathf.FloorToInt(ahead.x / RegionSize), ay = Mathf.FloorToInt(ahead.y / RegionSize), az = Mathf.FloorToInt(ahead.z / RegionSize);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                long k = Key(ax + dx, ay + dy, az + dz);
                                if (_harvested.ContainsKey(k)) continue;
                                Harvest(ax + dx, ay + dy, az + dz);
                                _harvested[k] = now;
                                if (++done >= maxHarvests || sw.Elapsed.TotalMilliseconds > budget) return;
                            }
                }
            }
            foreach (var o in _offsets)
            {
                int rx = prx + o.x, ry = pry + o.y, rz = prz + o.z;
                long key = Key(rx, ry, rz);
                // Next to the player (including the region above: a doorway reaches 2.5 m up from the feet).
                bool near = Math.Abs(o.x) <= 1 && Math.Abs(o.z) <= 1 && o.y >= -1 && o.y <= 1;
                if (_harvested.TryGetValue(key, out float at) && !(near && now - at > RefreshNear)) continue;
                Harvest(rx, ry, rz);
                _harvested[key] = now;
                if (++done >= maxHarvests || sw.Elapsed.TotalMilliseconds > budget) break;
            }
            if (_harvested.Count > _offsets.Count * 4) _harvested.Clear();
            if (_meshCache.Count > 4096) _meshCache.Clear();
        }

        static void Harvest(int rx, int ry, int rz)
        {
            var job = new Job { rx = rx, ry = ry, rz = rz, epoch = _epoch };
            const float margin = 0.25f;
            var lo = new Vector3(rx * RegionSize - margin, ry * RegionSize - margin, rz * RegionSize - margin);
            var hi = new Vector3((rx + 1) * RegionSize + margin, (ry + 1) * RegionSize + margin, (rz + 1) * RegionSize + margin);
            // Region box in Valheim space (Z flips; extents are the same).
            var centre = Coords.ToValheim((lo.x + hi.x) * 0.5, (lo.y + hi.y) * 0.5, (lo.z + hi.z) * 0.5);
            var half = (hi - lo) * 0.5f;
            int n = Physics.OverlapBoxNonAlloc(centre, half, _overlap, Quaternion.identity, _mask, QueryTriggerInteraction.Ignore);
            var vb = new Bounds(centre, half * 2f);
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (!col || !col.enabled || col.isTrigger) continue;
                if (OwnRoot && col.transform.IsChildOf(OwnRoot)) continue;
                if (col.attachedRigidbody && col.attachedRigidbody.GetComponent<Character>()) continue;
                if (IsSwingingDoor(col)) continue;
                try { Collect(col, vb, job); }
                catch (Exception e) { Plugin.Warn($"collision: skipped {col.name} ({e.Message})"); }
            }
            StatRegions++;
            _queue.Add(job);
        }

        static void Collect(Collider col, Bounds regionV, Job job)
        {
            switch (col)
            {
                case BoxCollider box:
                    {
                        var t = box.transform;
                        var s = t.lossyScale;
                        var ax = t.TransformDirection(Vector3.right);
                        var ay = t.TransformDirection(Vector3.up);
                        var az = t.TransformDirection(Vector3.forward);
                        var h = new Vector3(Mathf.Abs(box.size.x * s.x), Mathf.Abs(box.size.y * s.y), Mathf.Abs(box.size.z * s.z)) * 0.5f;
                        var c = t.TransformPoint(box.center);
                        job.boxes.Add(new Obb { c = V(c), ax = Coords.DirToMc(ax), ay = Coords.DirToMc(ay), az = Coords.DirToMc(az), half = h });
                        StatBoxes++;
                        break;
                    }
                case SphereCollider sphere:
                    {
                        var t = sphere.transform;
                        var s = t.lossyScale;
                        float r = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                        var c = V(t.TransformPoint(sphere.center));
                        job.capsules.Add(new Capsule { a = c, b = c, r = r });
                        break;
                    }
                case CapsuleCollider cap:
                    {
                        var t = cap.transform;
                        var s = t.lossyScale;
                        Vector3 axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
                        float axisScale = Mathf.Abs(cap.direction == 0 ? s.x : cap.direction == 1 ? s.y : s.z);
                        float radScale = cap.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)) : cap.direction == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)) : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
                        float r = cap.radius * radScale;
                        float halfLen = Mathf.Max(0f, cap.height * axisScale * 0.5f - r);
                        var c = t.TransformPoint(cap.center);
                        var dir = t.TransformDirection(axis).normalized;
                        job.capsules.Add(new Capsule { a = V(c - dir * halfLen), b = V(c + dir * halfLen), r = r });
                        break;
                    }
                case MeshCollider mc:
                    CollectMesh(mc, regionV, job);
                    break;
                case TerrainCollider tc:
                    CollectTerrain(tc, regionV, job);
                    break;
                default:
                    // Wheel colliders etc.: their bounds as a box.
                    var b = col.bounds;
                    job.boxes.Add(new Obb { c = V(b.center), ax = Vector3.right, ay = Vector3.up, az = Vector3.forward, half = b.extents });
                    break;
            }
        }

        static Vector3 V(Vector3 p) => Coords.ToMcF(p);

        static void CollectMesh(MeshCollider mc, Bounds regionV, Job job)
        {
            var mesh = mc.sharedMesh;
            if (!mesh) return;
            if (!mesh.isReadable)
            {
                StatUnreadable++;
                if (_loggedUnreadable.Add(mesh.GetInstanceID()) && _loggedUnreadable.Count <= 40)
                    Plugin.Log($"collision: mesh '{mesh.name}' on '{mc.name}' isn't readable; probing it with rays");
                ProbeWithRays(mc, regionV, job);
                return;
            }
            // Valheim's terrain meshes change when the ground is dug or raised: never cache those.
            bool terrain = mc.GetComponentInParent<Heightmap>() != null;
            int id = mesh.GetInstanceID();
            if (terrain || !_meshCache.TryGetValue(id, out var data))
            {
                data = new MeshData { verts = mesh.vertices, tris = mesh.triangles, bounds = mesh.bounds };
                if (!terrain) _meshCache[id] = data;
            }
            var m = mc.transform.localToWorldMatrix;
            var verts = data.verts;
            var tris = data.tris;
            Vector3 lo = regionV.min, hi = regionV.max;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                var a = m.MultiplyPoint3x4(verts[tris[i]]);
                var b = m.MultiplyPoint3x4(verts[tris[i + 1]]);
                var c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < lo.x || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > hi.x) continue;
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < lo.y || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > hi.y) continue;
                if (Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < lo.z || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > hi.z) continue;
                AddTri(job.tris, V(a), V(b), V(c));
            }
        }

        static void AddTri(List<float> list, Vector3 a, Vector3 b, Vector3 c)
        {
            list.Add(a.x); list.Add(a.y); list.Add(a.z);
            list.Add(b.x); list.Add(b.y); list.Add(b.z);
            list.Add(c.x); list.Add(c.y); list.Add(c.z);
            StatTris++;
        }

        static void CollectTerrain(TerrainCollider tc, Bounds regionV, Job job)
        {
            var data = tc.terrainData;
            if (!data) return;
            var origin = tc.transform.position;
            var size = data.size;
            int res = data.heightmapResolution;
            float cell = size.x / (res - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((regionV.min.x - origin.x) / cell), 0, res - 2);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((regionV.max.x - origin.x) / cell), 0, res - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((regionV.min.z - origin.z) / cell), 0, res - 2);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((regionV.max.z - origin.z) / cell), 0, res - 2);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 P(int ix, int iz) => origin + new Vector3(ix * cell, data.GetHeight(ix, iz), iz * (size.z / (res - 1)));
                    var a = P(x, z); var b = P(x + 1, z); var c = P(x + 1, z + 1); var d = P(x, z + 1);
                    AddTri(job.tris, V(a), V(c), V(b));
                    AddTri(job.tris, V(a), V(d), V(c));
                }
        }

        // Meshes whose vertices aren't on the CPU: cast rays through the collider on a 1/4-block
        // grid along all three axes, both ways, and keep where they hit (voxels only; the worker
        // turns those voxels' exposed faces into triangles for the player's smooth collider).
        static void ProbeWithRays(MeshCollider mc, Bounds regionV, Job job)
        {
            var b = mc.bounds;
            var lo = Vector3.Max(b.min, regionV.min);
            var hi = Vector3.Min(b.max, regionV.max);
            if (lo.x > hi.x || lo.y > hi.y || lo.z > hi.z) return;
            const float step = 0.25f;
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (float cu = lo[u] + step * 0.5f; cu < hi[u]; cu += step)
                    for (float cv = lo[v] + step * 0.5f; cv < hi[v]; cv += step)
                    {
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            var o = Vector3.zero;
                            o[u] = cu; o[v] = cv;
                            o[axis] = dir < 0 ? hi[axis] + 0.01f : lo[axis] - 0.01f;
                            var d = Vector3.zero;
                            d[axis] = dir < 0 ? -1f : 1f;
                            float len = hi[axis] - lo[axis] + 0.02f;
                            float travelled = 0f;
                            for (int hits = 0; hits < 6 && travelled < len; hits++)
                            {
                                if (!mc.Raycast(new Ray(o, d), out var hit, len - travelled)) break;
                                var p = V(hit.point + d * 0.01f);
                                job.rayVoxels.Add(p.x); job.rayVoxels.Add(p.y); job.rayVoxels.Add(p.z);
                                float adv = hit.distance + 0.05f;
                                o += d * adv;
                                travelled += adv;
                            }
                        }
                    }
            }
        }

        // ---- worker thread ------------------------------------------------------------------------

        static void WorkerLoop()
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (job.clear)
                    {
                        Send(Proto.ColClear, BitConverter.GetBytes(job.epoch), 4);
                        continue;
                    }
                    if (job.epoch != _epoch) continue;
                    var solid = new ulong[Grid * Grid];
                    Voxelize(job, solid);
                    SendTriangles(job, solid);
                    SendRegion(job, solid);
                }
                catch (Exception e)
                {
                    Plugin.Error("collision worker: " + e);
                }
            }
        }

        static void Send(uint type, byte[] payload, int bytes)
        {
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                if (Shm.WriteCollision(type, payload, bytes)) return;
                Thread.Sleep(1);  // ring full: Minecraft is behind (or not running)
            }
            Plugin.Warn("collision ring stayed full; dropped a message");
        }

        static unsafe void SendTriangles(Job job, ulong[] solid)
        {
            var list = new List<float>(job.tris.Count + 256);
            list.AddRange(job.tris);
            Triangulate(job, list, solid);
            float ox = job.rx * RegionSize - 0.5f, oy = job.ry * RegionSize - 0.5f, oz = job.rz * RegionSize - 0.5f;
            float ex = ox + RegionSize + 1f, ey = oy + RegionSize + 1f, ez = oz + RegionSize + 1f;
            int count = 0;
            var kept = new List<int>(list.Count / 9);
            for (int i = 0; i + 8 < list.Count; i += 9)
            {
                float minX = Mathf.Min(list[i], Mathf.Min(list[i + 3], list[i + 6])), maxX = Mathf.Max(list[i], Mathf.Max(list[i + 3], list[i + 6]));
                float minY = Mathf.Min(list[i + 1], Mathf.Min(list[i + 4], list[i + 7])), maxY = Mathf.Max(list[i + 1], Mathf.Max(list[i + 4], list[i + 7]));
                float minZ = Mathf.Min(list[i + 2], Mathf.Min(list[i + 5], list[i + 8])), maxZ = Mathf.Max(list[i + 2], Mathf.Max(list[i + 5], list[i + 8]));
                if (maxX < ox || minX > ex || maxY < oy || minY > ey || maxZ < oz || minZ > ez) continue;
                kept.Add(i);
                count++;
            }
            int bytes = sizeof(ColRegion) + count * sizeof(ColTri);
            var payload = new byte[bytes];
            fixed (byte* p = payload)
            {
                var h = (ColRegion*)p;
                FillHeader(h, job, (uint)count);
                var t = (ColTri*)(p + sizeof(ColRegion));
                for (int k = 0; k < count; k++)
                {
                    int i = kept[k];
                    for (int j = 0; j < 9; j++) t[k].v[j] = list[i + j];
                    t[k].flags = 0;
                }
            }
            Send(Proto.ColTris, payload, bytes);
        }

        static unsafe void FillHeader(ColRegion* h, Job job, uint count)
        {
            h->minX = job.rx * RegionSize;
            h->minY = job.ry * RegionSize;
            h->minZ = job.rz * RegionSize;
            h->maxX = h->minX + RegionSize - 1;
            h->maxY = h->minY + RegionSize - 1;
            h->maxZ = h->minZ + RegionSize - 1;
            h->epoch = job.epoch;
            h->count = count;
        }

        // Boxes and capsules (as boxes) to triangles; ray-probed voxels to their exposed faces.
        static void Triangulate(Job job, List<float> o, ulong[] solid)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { AddTriRaw(o, a, b, c); AddTriRaw(o, a, c, d); }
            void Box(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector3 h)
            {
                var k = new Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    float sx = (i & 1) != 0 ? 1f : -1f, sy = (i & 2) != 0 ? 1f : -1f, sz = (i & 4) != 0 ? 1f : -1f;
                    k[i] = c + ax * (h.x * sx) + ay * (h.y * sy) + az * (h.z * sz);
                }
                Quad(k[0], k[1], k[3], k[2]); Quad(k[4], k[5], k[7], k[6]);
                Quad(k[0], k[1], k[5], k[4]); Quad(k[2], k[3], k[7], k[6]);
                Quad(k[0], k[2], k[6], k[4]); Quad(k[1], k[3], k[7], k[5]);
            }
            foreach (var b in job.boxes) Box(b.c, b.ax, b.ay, b.az, b.half);
            foreach (var cap in job.capsules)
            {
                var ab = cap.b - cap.a;
                float len = ab.magnitude;
                var az = len > 1e-4f ? ab / len : Vector3.up;
                var reference = Mathf.Abs(az.y) < 0.9f ? Vector3.up : Vector3.right;
                var ax = Vector3.Cross(reference, az).normalized;
                var ay = Vector3.Cross(az, ax);
                Box((cap.a + cap.b) * 0.5f, ax, ay, az, new Vector3(cap.r, cap.r, len * 0.5f + cap.r));
            }
            if (job.rayVoxels.Count == 0) return;
            // Exposed faces of the ray-probed voxels, so the player's smooth collider sees them too.
            var probed = new ulong[Grid * Grid];
            float ox = job.rx * RegionSize, oy = job.ry * RegionSize, oz = job.rz * RegionSize;
            for (int i = 0; i + 2 < job.rayVoxels.Count; i += 3)
            {
                int x = Mathf.FloorToInt((job.rayVoxels[i] - ox) * 8f), y = Mathf.FloorToInt((job.rayVoxels[i + 1] - oy) * 8f), z = Mathf.FloorToInt((job.rayVoxels[i + 2] - oz) * 8f);
                if (x < 0 || y < 0 || z < 0 || x >= Grid || y >= Grid || z >= Grid) continue;
                probed[y * Grid + z] |= 1ul << x;
            }
            const float e = 1f / 8f;
            bool Has(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Grid && y < Grid && z < Grid && (probed[y * Grid + z] & (1ul << x)) != 0;
            for (int y = 0; y < Grid; y++)
                for (int z = 0; z < Grid; z++)
                {
                    ulong row = probed[y * Grid + z];
                    while (row != 0)
                    {
                        int x = BitOps.TrailingZeros(row);
                        row &= row - 1;
                        var p = new Vector3(ox + x * e, oy + y * e, oz + z * e);
                        if (!Has(x, y + 1, z)) Quad(p + new Vector3(0, e, 0), p + new Vector3(e, e, 0), p + new Vector3(e, e, e), p + new Vector3(0, e, e));
                        if (!Has(x, y - 1, z)) Quad(p, p + new Vector3(0, 0, e), p + new Vector3(e, 0, e), p + new Vector3(e, 0, 0));
                        if (!Has(x + 1, y, z)) Quad(p + new Vector3(e, 0, 0), p + new Vector3(e, 0, e), p + new Vector3(e, e, e), p + new Vector3(e, e, 0));
                        if (!Has(x - 1, y, z)) Quad(p, p + new Vector3(0, e, 0), p + new Vector3(0, e, e), p + new Vector3(0, 0, e));
                        if (!Has(x, y, z + 1)) Quad(p + new Vector3(0, 0, e), p + new Vector3(0, e, e), p + new Vector3(e, e, e), p + new Vector3(e, 0, e));
                        if (!Has(x, y, z - 1)) Quad(p, p + new Vector3(e, 0, 0), p + new Vector3(e, e, 0), p + new Vector3(0, e, 0));
                    }
                }
            for (int i = 0; i < probed.Length; i++) solid[i] |= probed[i];
        }

        static void AddTriRaw(List<float> l, Vector3 a, Vector3 b, Vector3 c)
        {
            l.Add(a.x); l.Add(a.y); l.Add(a.z); l.Add(b.x); l.Add(b.y); l.Add(b.z); l.Add(c.x); l.Add(c.y); l.Add(c.z);
        }

        static void Voxelize(Job job, ulong[] solid)
        {
            const int G = Grid;
            var steep = new ulong[G * G];
            float ox = job.rx * RegionSize, oy = job.ry * RegionSize, oz = job.rz * RegionSize;
            int ClampLo(float v) => Math.Min(Math.Max((int)Math.Floor(v), 0), G - 1);
            int ClampHi(float v) => Math.Min(Math.Max((int)Math.Ceiling(v) - 1, 0), G - 1);

            var tri = job.tris;
            var a = new float[3]; var b = new float[3]; var c = new float[3]; var n = new float[3];
            for (int i = 0; i + 8 < tri.Count; i += 9)
            {
                a[0] = (tri[i] - ox) * 8f; a[1] = (tri[i + 1] - oy) * 8f; a[2] = (tri[i + 2] - oz) * 8f;
                b[0] = (tri[i + 3] - ox) * 8f; b[1] = (tri[i + 4] - oy) * 8f; b[2] = (tri[i + 5] - oz) * 8f;
                c[0] = (tri[i + 6] - ox) * 8f; c[1] = (tri[i + 7] - oy) * 8f; c[2] = (tri[i + 8] - oz) * 8f;
                float lo0 = Math.Min(a[0], Math.Min(b[0], c[0])), hi0 = Math.Max(a[0], Math.Max(b[0], c[0]));
                float lo1 = Math.Min(a[1], Math.Min(b[1], c[1])), hi1 = Math.Max(a[1], Math.Max(b[1], c[1]));
                float lo2 = Math.Min(a[2], Math.Min(b[2], c[2])), hi2 = Math.Max(a[2], Math.Max(b[2], c[2]));
                if (hi0 < 0 || hi1 < 0 || hi2 < 0 || lo0 > G || lo1 > G || lo2 > G) continue;
                float e10 = b[0] - a[0], e11 = b[1] - a[1], e12 = b[2] - a[2];
                float e20 = c[0] - a[0], e21 = c[1] - a[1], e22 = c[2] - a[2];
                n[0] = e11 * e22 - e12 * e21; n[1] = e12 * e20 - e10 * e22; n[2] = e10 * e21 - e11 * e20;
                float len = (float)Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                if (len < 1e-9f) continue;
                n[0] /= len; n[1] /= len; n[2] /= len;
                float ny = Math.Abs(n[1]);
                var grid = ny >= SteepMax || ny < SteepMin ? solid : steep;

                int dom = 0;
                if (Math.Abs(n[1]) > Math.Abs(n[dom])) dom = 1;
                if (Math.Abs(n[2]) > Math.Abs(n[dom])) dom = 2;
                int u = (dom + 1) % 3, v = (dom + 2) % 3;
                float d = n[0] * a[0] + n[1] * a[1] + n[2] * a[2];
                float r = 0.5f * (Math.Abs(n[0]) + Math.Abs(n[1]) + Math.Abs(n[2]));
                float[] lo = { lo0, lo1, lo2 }, hi = { hi0, hi1, hi2 };
                int iu0 = ClampLo(lo[u]), iu1 = ClampHi(hi[u]), iv0 = ClampLo(lo[v]), iv1 = ClampHi(hi[v]);
                int id0 = ClampLo(lo[dom]), id1 = ClampHi(hi[dom]);
                var cen = new float[3];
                var p = new int[3];
                for (int iu = iu0; iu <= iu1; iu++)
                    for (int iv = iv0; iv <= iv1; iv++)
                    {
                        float cu = iu + 0.5f, cv = iv + 0.5f;
                        float s0 = (d - r - n[u] * cu - n[v] * cv) / n[dom];
                        float s1 = (d + r - n[u] * cu - n[v] * cv) / n[dom];
                        int a0 = Math.Max(id0, (int)Math.Floor(Math.Min(s0, s1) - 0.5f));
                        int a1 = Math.Min(id1, (int)Math.Ceiling(Math.Max(s0, s1) - 0.5f));
                        for (int id = a0; id <= a1; id++)
                        {
                            cen[dom] = id + 0.5f; cen[u] = cu; cen[v] = cv;
                            if (TriBoxOverlap(cen, 0.5f, a, b, c, n))
                            {
                                p[dom] = id; p[u] = iu; p[v] = iv;
                                grid[p[1] * G + p[2]] |= 1ul << p[0];
                            }
                        }
                    }
            }

            // Primitives: voxel-centre containment with a small margin.
            const float m = PrimMargin / 8f;
            void Fill(Vector3 plo, Vector3 phi, Func<Vector3, bool> inside)
            {
                float lx = (plo.x - ox) * 8f, ly = (plo.y - oy) * 8f, lz = (plo.z - oz) * 8f;
                float hx = (phi.x - ox) * 8f, hy = (phi.y - oy) * 8f, hz = (phi.z - oz) * 8f;
                if (hx < 0 || hy < 0 || hz < 0 || lx > G || ly > G || lz > G) return;
                for (int y = ClampLo(ly - 1); y <= ClampHi(hy + 1); y++)
                    for (int z = ClampLo(lz - 1); z <= ClampHi(hz + 1); z++)
                        for (int x = ClampLo(lx - 1); x <= ClampHi(hx + 1); x++)
                        {
                            var q = new Vector3(ox + (x + 0.5f) / 8f, oy + (y + 0.5f) / 8f, oz + (z + 0.5f) / 8f);
                            if (inside(q)) solid[y * G + z] |= 1ul << x;
                        }
            }
            foreach (var box in job.boxes)
            {
                var ext = new Vector3(
                    Math.Abs(box.ax.x) * box.half.x + Math.Abs(box.ay.x) * box.half.y + Math.Abs(box.az.x) * box.half.z,
                    Math.Abs(box.ax.y) * box.half.x + Math.Abs(box.ay.y) * box.half.y + Math.Abs(box.az.y) * box.half.z,
                    Math.Abs(box.ax.z) * box.half.x + Math.Abs(box.ay.z) * box.half.y + Math.Abs(box.az.z) * box.half.z);
                var bx = box;
                Fill(box.c - ext, box.c + ext, q =>
                {
                    var dd = q - bx.c;
                    return Math.Abs(Vector3.Dot(dd, bx.ax)) <= bx.half.x + m && Math.Abs(Vector3.Dot(dd, bx.ay)) <= bx.half.y + m && Math.Abs(Vector3.Dot(dd, bx.az)) <= bx.half.z + m;
                });
            }
            foreach (var cap in job.capsules)
            {
                var cp = cap;
                var rr = new Vector3(cap.r, cap.r, cap.r);
                Fill(Vector3.Min(cap.a, cap.b) - rr, Vector3.Max(cap.a, cap.b) + rr, q =>
                {
                    var ab = cp.b - cp.a;
                    float l2 = Vector3.Dot(ab, ab);
                    float t = l2 > 0 ? Mathf.Clamp01(Vector3.Dot(q - cp.a, ab) / l2) : 0f;
                    return (cp.a + ab * t - q).sqrMagnitude <= (cp.r + m) * (cp.r + m);
                });
            }

            // Steep (50-84 deg) surfaces snap to whole-block footprints, so the risers between
            // neighbouring columns beat Minecraft's 0.6 step height: cliffs behave like block cliffs.
            for (int by = 0; by < RegionSize; by++)
                for (int bz = 0; bz < RegionSize; bz++)
                    for (int bx = 0; bx < RegionSize; bx++)
                    {
                        ulong xmask = 0xFFul << (bx * 8);
                        int minY = 99, maxY = -1;
                        for (int y = by * 8; y < by * 8 + 8; y++)
                            for (int z = bz * 8; z < bz * 8 + 8; z++)
                                if ((steep[y * G + z] & xmask) != 0) { minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                        if (maxY < 0) continue;
                        for (int y = minY; y <= maxY; y++)
                            for (int z = bz * 8; z < bz * 8 + 8; z++)
                                solid[y * G + z] |= xmask;
                    }
        }

        static unsafe void SendRegion(Job job, ulong[] solid)
        {
            var blocks = new List<ColBlock>(64);
            for (int by = 0; by < RegionSize; by++)
                for (int bz = 0; bz < RegionSize; bz++)
                    for (int bx = 0; bx < RegionSize; bx++)
                    {
                        var blk = new ColBlock();
                        bool any = false;
                        for (int sy = 0; sy < 8; sy++)
                        {
                            ulong layer = 0;
                            for (int sz = 0; sz < 8; sz++)
                            {
                                ulong row = (solid[(by * 8 + sy) * Grid + (bz * 8 + sz)] >> (bx * 8)) & 0xFF;
                                layer |= row << (sz * 8);
                            }
                            blk.bits[sy] = layer;
                            any |= layer != 0;
                        }
                        if (!any) continue;
                        blk.x = job.rx * RegionSize + bx;
                        blk.y = job.ry * RegionSize + by;
                        blk.z = job.rz * RegionSize + bz;
                        blocks.Add(blk);
                    }
            int bytes = sizeof(ColRegion) + blocks.Count * sizeof(ColBlock);
            var payload = new byte[bytes];
            fixed (byte* p = payload)
            {
                FillHeader((ColRegion*)p, job, (uint)blocks.Count);
                var dst = (ColBlock*)(p + sizeof(ColRegion));
                for (int i = 0; i < blocks.Count; i++) dst[i] = blocks[i];
            }
            Send(Proto.ColRegion, payload, bytes);
        }

        // ---- triangle / box overlap (Akenine-Moller SAT), voxel units ---------------------------

        static bool AxisTest(float[] v0, float[] v1, float[] v2, float ax, float ay, float az, float h)
        {
            float p0 = v0[0] * ax + v0[1] * ay + v0[2] * az;
            float p1 = v1[0] * ax + v1[1] * ay + v1[2] * az;
            float p2 = v2[0] * ax + v2[1] * ay + v2[2] * az;
            float mn = Math.Min(p0, Math.Min(p1, p2)), mx = Math.Max(p0, Math.Max(p1, p2));
            float r = h * (Math.Abs(ax) + Math.Abs(ay) + Math.Abs(az));
            return !(mn > r || mx < -r);
        }

        [ThreadStatic] static float[] _v0, _v1, _v2;

        static bool TriBoxOverlap(float[] c, float h, float[] ta, float[] tb, float[] tc, float[] n)
        {
            var v0 = _v0 ??= new float[3];
            var v1 = _v1 ??= new float[3];
            var v2 = _v2 ??= new float[3];
            for (int i = 0; i < 3; i++) { v0[i] = ta[i] - c[i]; v1[i] = tb[i] - c[i]; v2[i] = tc[i] - c[i]; }
            for (int i = 0; i < 3; i++)
            {
                float mn = Math.Min(v0[i], Math.Min(v1[i], v2[i])), mx = Math.Max(v0[i], Math.Max(v1[i], v2[i]));
                if (mn > h || mx < -h) return false;
            }
            float d = n[0] * v0[0] + n[1] * v0[1] + n[2] * v0[2];
            float r = h * (Math.Abs(n[0]) + Math.Abs(n[1]) + Math.Abs(n[2]));
            if (Math.Abs(d) > r) return false;
            // edges
            float e0x = v1[0] - v0[0], e0y = v1[1] - v0[1], e0z = v1[2] - v0[2];
            float e1x = v2[0] - v1[0], e1y = v2[1] - v1[1], e1z = v2[2] - v1[2];
            float e2x = v0[0] - v2[0], e2y = v0[1] - v2[1], e2z = v0[2] - v2[2];
            // cross(edge, unit axis) for x, y, z
            return AxisTest(v0, v1, v2, 0, e0z, -e0y, h) && AxisTest(v0, v1, v2, -e0z, 0, e0x, h) && AxisTest(v0, v1, v2, e0y, -e0x, 0, h) &&
                   AxisTest(v0, v1, v2, 0, e1z, -e1y, h) && AxisTest(v0, v1, v2, -e1z, 0, e1x, h) && AxisTest(v0, v1, v2, e1y, -e1x, 0, h) &&
                   AxisTest(v0, v1, v2, 0, e2z, -e2y, h) && AxisTest(v0, v1, v2, -e2z, 0, e2x, h) && AxisTest(v0, v1, v2, e2y, -e2x, 0, h);
        }
    }

    static class BitOps
    {
        public static int TrailingZeros(ulong v)
        {
            int n = 0;
            while ((v & 1) == 0) { v >>= 1; n++; }
            return n;
        }
    }
}
