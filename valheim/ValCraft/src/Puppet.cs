using System;
using System.Collections.Generic;
using ValCraft.Link;
using UnityEngine;

namespace ValCraft
{
    // The per-frame bridge (SkyCraft's Game.cpp, for Valheim). Minecraft is authoritative for the
    // player's position and physics; Valheim's player is a puppet moved to where Minecraft says, so
    // its enemies, triggers, portals and pickups keep working. Valheim tells Minecraft where the
    // player is after its own moves (portals, respawns, dungeon doors) and where they're looking.
    public static class Puppet
    {
        // ---- shared state (main thread) -------------------------------------------------------
        public static bool Puppeting;          // Minecraft drives the player this frame
        public static bool MinecraftOwnsPlayer; // puppeting, or waiting for Minecraft to arrive after a teleport
        public static bool McScreenOpen;       // a Minecraft screen (inventory, chat, ...) is open
        public static bool ValheimMenuOpen;    // a Valheim menu owns input
        public static bool McInWorld;
        // Minecraft stopped driving for a moment while still connected (F8 switching saves, the
        // teleport handshake after it, a world change): the Viking is parked where it stands instead
        // of falling, off builds that only exist in the other save, or through Valheim's ground while
        // block terrain replaces it.
        public static bool Parked;
        public static Vector3 ParkPos;
        static float _parkTime;
        const float ParkSeconds = 60f;
        public static bool McConnected;
        public static McState Mc;
        public static float Yaw, Pitch;        // MC degrees; integrated from the mouse here
        public static float Sensitivity = 0.5f;
        public static bool EyeValid;
        public static Vector3 EyePos;          // Valheim space, bob included
        public static Quaternion EyeRot;
        public static Vector3 FeetPos;         // Valheim space
        public static float FovDeg = 70f;
        public static uint Epoch;

        static uint _teleportSeq = (uint)Environment.TickCount | 1u;
        static bool _teleportPending = true;
        static bool _mcWasAlive;
        static uint _lastMcPid;
        static uint _worldId;
        // Where MotionPatch last put the body (FixedUpdate): Valheim moving it anywhere else is its own teleport.
        public static Vector3 LastSetPos;
        public static bool BodyKinematic;  // we made the body kinematic (MotionPatch); undone when Minecraft lets go
        public static bool HaveLastSet;
        static Player _lastPlayer;
        static Rigidbody _body;
        static float _holdMismatch;
        static float _settleTimer = 2f;
        static bool _lookInitialized;
        static bool _blockTerrain;
        static string _takeover;
        const float SettleSeconds = 1.5f;
        const float ValheimTeleportThreshold = 4f;  // metres; bigger jumps are Valheim moving the player

        // The body is placed in two places a frame (FixedUpdate and LateUpdate), so how far it may be
        // from the last placing grows with speed: at 30+ m/s (an elytra with fireworks) a frame or two
        // between them is already 4 m, and calling that a teleport snapped Minecraft back mid-flight.
        static float TeleportThreshold()
        {
            if (Mc.tickMs <= 0f) return ValheimTeleportThreshold;
            double dx = Mc.curX - Mc.prevX, dy = Mc.curY - Mc.prevY, dz = Mc.curZ - Mc.prevZ;
            float speed = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz) * (1000f / Mc.tickMs);
            return Mathf.Max(ValheimTeleportThreshold, 2f + speed * 0.5f);
        }

        // Minecraft's 20 Hz physics ticks, interpolated on our own frame clock (see SkyCraft's Game.cpp).
        struct Tick { public McState s; public long at; public int slots; }
        static readonly List<Tick> _ticks = new List<Tick>();
        static long _lastFrameQpc;
        static int _stampOutliers;
        static double _renderDelayMs = 10.0;
        static readonly double[] _tickDue = new double[40];
        static int _tickDueNext;
        static bool _tickDueInit;
        static float _zoom;
        static uint _zoomMode;

        public static void Reset()
        {
            Puppeting = MinecraftOwnsPlayer = McScreenOpen = EyeValid = false;
            _teleportPending = true;
            HaveLastSet = false;
            _lookInitialized = false;
            _ticks.Clear();
        }

        public static void Frame(float dt)
        {
            Shm.Heartbeat();
            bool mcAlive = Shm.McAlive();
            bool haveMc = mcAlive && Shm.ReadMcState(out Mc);
            uint mcPid = Shm.McPid();
            bool newMcProcess = mcAlive && mcPid != 0 && mcPid != _lastMcPid;
            if (mcAlive) _lastMcPid = mcPid;
            if (mcAlive && (!_mcWasAlive || newMcProcess))
            {
                Plugin.Log("Minecraft connected (pid " + mcPid + ")");
                Shm.ResetOverlay();
                _settleTimer = SettleSeconds;
                ResetCollision();
                _teleportPending = true;
                WorldRender.OnMinecraftConnected();
            }
            if (!mcAlive && _mcWasAlive)
            {
                Plugin.Log("Minecraft disconnected");
                InputBridge.ReleaseAll();
            }
            _mcWasAlive = mcAlive;
            McConnected = mcAlive;

            McInWorld = haveMc && (Mc.flags & Proto.McInWorld) != 0;
            bool screenOpen = haveMc && (Mc.flags & Proto.McScreenOpen) != 0;
            McScreenOpen = screenOpen;
            if (haveMc && Mc.sensitivity > 0f) Sensitivity = Mc.sensitivity;

            var player = Player.m_localPlayer;
            bool loading = player == null || player.IsTeleporting() || !ZNetScene.instance;
            bool menu = player != null && AnyValheimMenuOpen();
            if ((menu || loading) && !ValheimMenuOpen) InputBridge.ReleaseAll();
            ValheimMenuOpen = menu || loading;

            if (player == null)
            {
                Puppeting = MinecraftOwnsPlayer = EyeValid = false;
                _teleportPending = true;
                HaveLastSet = false;
                WriteState(null, loading: true, menu: false);
                return;
            }

            // Which world we're in: the Valheim world, or one of its dungeons/interiors (5 km up,
            // brought down to Minecraft's height range). A change wipes Minecraft's collision.
            Vector3 current = player.transform.position;
            if (!Puppeting || loading) Coords.UpdateFor(current);
            uint worldId = WorldIdFor(Coords.Interior);
            if (worldId != _worldId)
            {
                Plugin.Log($"world changed {_worldId:X8} -> {worldId:X8}{(Coords.Interior ? " (interior)" : "")}");
                _worldId = worldId;
                ResetCollision();
                _teleportPending = true;
                _settleTimer = SettleSeconds;
            }

            // Valheim moved the player itself (portal, respawn, dungeon door, bed, ...).
            if (loading)
            {
                _teleportPending = true;
                HaveLastSet = false;
                _settleTimer = SettleSeconds;
            }
            else if (player != _lastPlayer)
            {
                _lastPlayer = player;
                _body = player.GetComponent<Rigidbody>();
                _teleportPending = true;
                HaveLastSet = false;
            }
            else if (HaveLastSet && _body && Vector3.Distance(_body.position, LastSetPos) > TeleportThreshold())
            {
                Plugin.Log($"Valheim moved the player ({Vector3.Distance(_body.position, LastSetPos):F1} m); resyncing Minecraft");
                Coords.UpdateFor(current);
                _teleportPending = true;
                HaveLastSet = false;
            }
            if (_teleportPending && !loading)
            {
                _teleportSeq++;
                _teleportPending = false;
                TakeLookFromValheim(player);
            }
            if (!_lookInitialized) TakeLookFromValheim(player);

            // Mouse look with Minecraft's formula, integrated here so the camera has no added latency.
            InputBridge.ConsumeLook(out float dx, out float dy);
            if (!McScreenOpen && !ValheimMenuOpen)
            {
                float s = Sensitivity * 0.6f + 0.2f;
                float factor = s * s * s * 8f * 0.15f;
                Yaw = Mathf.Repeat(Yaw + dx * factor, 360f);
                Pitch = Mathf.Clamp(Pitch + dy * factor, -90f, 90f);
            }

            string takeoverNow = player.IsDead() ? null : ValheimTakeover(player);
            if ((takeoverNow != null) != (_takeover != null))
            {
                if (takeoverNow != null) Plugin.Log($"Valheim takes the player ({takeoverNow})");
                else { Plugin.Log("Valheim hands the player back"); _teleportPending = true; }
            }
            _takeover = takeoverNow;

            bool arriving = haveMc && McInWorld && !loading && Mc.teleportAck != _teleportSeq && _takeover == null;
            if (arriving)
            {
                var here = Coords.ToMc(current);
                double gap = here.DistanceTo(new Vector3d(Mc.x, Mc.y, Mc.z));
                _holdMismatch = gap > 8.0 ? _holdMismatch + dt : 0f;
                if (_holdMismatch > 1f)
                {
                    Plugin.Log($"Minecraft is waiting {gap:F0} blocks from Valheim's player; teleporting it again");
                    _teleportPending = true;
                    _holdMismatch = 0f;
                }
            }
            else _holdMismatch = 0f;

            bool puppet = haveMc && McInWorld && Mc.teleportAck == _teleportSeq && !loading && !player.IsDead() && _takeover == null;
            MinecraftOwnsPlayer = puppet || (arriving && !player.IsDead());
            if (puppet != Puppeting) Plugin.Log("puppet " + (puppet ? "on (Minecraft drives the player)" : "off"));
            bool park = !puppet && haveMc && !loading && !player.IsDead() && _takeover == null && !Plugin.Paused && (Puppeting || Parked);
            if (park && !Parked)
            {
                Parked = true;
                // Where it stands now: after a move of Valheim's own, that's already the destination.
                ParkPos = player.transform.position;
                _parkTime = 0f;
                Plugin.Log("Minecraft isn't driving for a moment: holding the player in place");
            }
            if (Parked)
            {
                _parkTime += dt;
                if (!park || _parkTime > ParkSeconds)
                {
                    Parked = false;
                    Plugin.Log(park ? "Minecraft took too long; Valheim has the player back" : "Minecraft is back; releasing the hold");
                }
            }
            Puppeting = puppet;

            Interpolate(haveMc, out double feetX, out double feetY, out double feetZ, out double eyeX, out double eyeY, out double eyeZ,
                out float bobPhaseNow, out float bobAmountNow);

            // While Minecraft holds the player in place (after a teleport or world switch, until the
            // ground under them has arrived) its camera and mouse look stay on, as long as it's holding
            // them where Valheim's player is.
            bool holding = !puppet && MinecraftOwnsPlayer && haveMc &&
                           Vector3.Distance(Coords.ToValheim(Mc.x, Mc.y, Mc.z), current) < 4f;
            if (puppet || holding)
            {
                FeetPos = Coords.ToValheim(feetX, feetY, feetZ);

                // Minecraft's walk bob (GameRenderer.bobView) as a camera offset and tilt.
                float phase = bobPhaseNow * Mathf.PI;
                float bob = bobAmountNow;
                float side = -Mathf.Sin(phase) * bob * 0.5f;
                float lift = Mathf.Abs(Mathf.Cos(phase) * bob);
                float bobPitch = Mathf.Abs(Mathf.Cos(phase - 0.2f) * bob) * 5f;
                float bobRoll = Mathf.Sin(phase) * bob * 3f;

                var look = Quaternion.Euler(Pitch, Coords.McYawToUnity(Yaw), 0f);
                Vector3 eye = Coords.ToValheim(eyeX, eyeY, eyeZ);
                eye += look * Vector3.right * side;
                eye.y += lift;

                // Minecraft's F5 camera: behind the player, or in front looking back, pulled in where
                // Minecraft's own zoom collision stopped it.
                bool mirrored = Mc.cameraMode == 2;
                float camYaw = mirrored ? Yaw + 180f : Yaw;
                float camPitch = mirrored ? -Pitch : Pitch;
                bool detached = Mc.cameraMode != 0 && Mc.cameraDistance > 0f;
                if (!detached || _zoomMode != Mc.cameraMode || Mc.cameraDistance < _zoom) _zoom = detached ? Mc.cameraDistance : 0f;
                else _zoom += (Mc.cameraDistance - _zoom) * (1f - Mathf.Exp(-Mathf.Max(dt, 0f) / 0.2f));
                _zoomMode = Mc.cameraMode;
                var camLook = Quaternion.Euler(camPitch, Coords.McYawToUnity(camYaw), 0f);
                if (detached) eye -= camLook * Vector3.forward * _zoom;

                EyePos = eye;
                EyeRot = Quaternion.Euler(camPitch + bobPitch, Coords.McYawToUnity(camYaw), bobRoll);
                EyeValid = true;
                if (Mc.fovDeg > 1f) FovDeg = Mc.fovDeg;
                CameraSettings.Read(Mc);

                // Valheim uses the player's look direction for hover/interaction and what the body faces.
                player.SetLookDir(look * Vector3.forward);
                // Minecraft draws the hand in first person and its own body in third: the Viking stays hidden.
                Body.Apply(player, firstPerson: Mc.cameraMode == 0 || Render.SceneRender.AvatarVisible);
            }
            else
            {
                EyeValid = false;
                if (BodyKinematic && _body)
                {
                    _body.isKinematic = false;
                    BodyKinematic = false;
                }
                // Through a teleport (portals, dungeon doors) the Minecraft player is only between places:
                // the Viking stays hidden. It shows when Valheim really has the player (sitting, a bed, a ship).
                bool betweenPlaces = McInWorld && (player.IsTeleporting() || MinecraftOwnsPlayer);
                if (betweenPlaces) Body.Apply(player, firstPerson: true);
                else Body.Apply(player, firstPerson: false, restore: true);
            }

            WriteState(player, loading, menu);

            _settleTimer -= dt;
            Vector3d centre = puppet ? new Vector3d(Mc.x, Mc.y, Mc.z) : Coords.ToMc(current);
            // Minecraft's own velocity (last tick), so collision ahead of a fast player arrives in time.
            var velocity = Vector3.zero;
            if (puppet && Mc.tickMs > 0f)
                velocity = new Vector3((float)(Mc.curX - Mc.prevX), (float)(Mc.curY - Mc.prevY), (float)(Mc.curZ - Mc.prevZ)) * (1000f / Mc.tickMs);
            // Block terrain switched: Minecraft changes saves, and collision goes again with (or without) the terrain.
            if (BlockTerrain.On != _blockTerrain)
            {
                _blockTerrain = BlockTerrain.On;
                ResetCollision();
                _teleportPending = true;
                _settleTimer = SettleSeconds;
            }
            long pt = Prof.Start();
            if (haveMc && !loading && _settleTimer <= 0f) Collision.Update(centre, velocity);
            Prof.Stop("puppet/collision", pt);
            pt = Prof.Start();
            if (haveMc && McInWorld && !loading) BlockTerrain.Frame(centre, Epoch, dt);
            Prof.Stop("puppet/terrain", pt);
            pt = Prof.Start();
            if (haveMc && McInWorld && !loading && Time.frameCount % 3 == 0) Water.Write(centre, _worldId);
            Prof.Stop("puppet/water", pt);
        }

        // Valheim's weather for Minecraft's (WeatherSync.java): rain or snow outside, a thunderstorm.
        // Dungeons and other interiors are dry.
        static uint WeatherBits(Player player)
        {
            if (player == null || !EnvMan.instance || Coords.Interior) return 0u;
            var env = EnvMan.instance.GetCurrentEnvironment();
            if (env == null) return 0u;
            bool wet = env.m_isWet || EnvMan.IsWet();
            bool thunder = env.m_name != null && env.m_name.IndexOf("Thunder", System.StringComparison.OrdinalIgnoreCase) >= 0;
            return (wet || thunder ? Proto.ValWet : 0u) | (thunder ? Proto.ValThunder : 0u);
        }

        static void WriteState(Player player, bool loading, bool menu)
        {
            var st = new ValState();
            st.flags = (player != null ? Proto.ValInGame : 0u) | (menu ? Proto.ValMenuOpen : 0u) | (loading ? Proto.ValLoading : 0u) |
                       (BlockTerrain.On ? Proto.ValBlockTerrain : 0u) | Combat.MobPathingBits | Combat.MobSpawningBits |
                       (player != null && ZNet.instance && Game.IsPaused() ? Proto.ValPaused : 0u) | WeatherBits(player);
            CameraSettings.Write(ref st);
            Multiplayer.Write(ref st);
            st.worldId = _worldId;
            st.collisionEpoch = Epoch;
            if (player != null)
            {
                var p = Coords.ToMc(player.transform.position);
                st.posX = p.x;
                st.posY = p.y;
                st.posZ = p.z;
            }
            st.yaw = Yaw;
            st.pitch = Pitch;
            st.teleportSeq = _teleportSeq;
            st.viewportW = (uint)Mathf.Min(Screen.width, Proto.MaxOverlayW);
            st.viewportH = (uint)Mathf.Min(Screen.height, Proto.MaxOverlayH);
            st.gameHour = EnvMan.instance ? EnvMan.instance.GetDayFraction() * 24f : 12f;
            Shm.WriteValState(ref st);
        }

        static void ResetCollision()
        {
            Epoch++;
            Collision.Reset(Epoch);
        }

        static uint WorldIdFor(bool interior)
        {
            // The world's id, the same on the host and every client (Minecraft keeps one save per world).
            long uid = ZNet.instance ? ZNet.instance.GetWorldUID() : 0;
            if (uid == 0 && ZNet.World != null) uid = ZNet.World.m_uid;
            uint id = (uint)(uid ^ (uid >> 32)) & 0x7FFFFFFF;
            return (id == 0 ? 1u : id) | (interior ? 0x80000000u : 0u);
        }

        static void TakeLookFromValheim(Player player)
        {
            var fwd = player.GetLookDir();
            if (fwd.sqrMagnitude < 1e-6f) fwd = player.transform.forward;
            var e = Quaternion.LookRotation(fwd).eulerAngles;
            Yaw = Coords.UnityYawToMc(e.y);
            Pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -90f, 90f);
            _lookInitialized = true;
        }

        // Valheim takes the player for its own things: sitting, beds, steering a ship, the cart,
        // riding, the intro, cutscenes. Minecraft lets go meanwhile and picks up where Valheim leaves it.
        static string ValheimTakeover(Player player)
        {
            if (player.IsAttached()) return "attached (chair, bed, ship, saddle)";
            if (player.InCutscene()) return "cutscene";
            if (player.InIntro()) return "intro";
            if (player.IsDebugFlying()) return "debug fly";
            if (GameCamera.InFreeFly()) return "free-fly camera";
            return null;
        }

        public static bool AnyValheimMenuOpen()
        {
            return Hud.InRadial() || InventoryGui.IsVisible() || TextInput.IsVisible() || Menu.IsActive() || Minimap.IsOpen() ||
                   StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || UnifiedPopup.IsVisible() || global::Console.IsVisible() ||
                   (Chat.instance && Chat.instance.HasFocus()) || (TextViewer.instance && TextViewer.instance.IsVisible()) ||
                   ConfigManagerCompat.WindowOpen;
        }

        // ---- tick interpolation ---------------------------------------------------------------

        static void Interpolate(bool haveMc, out double fx, out double fy, out double fz, out double ex, out double ey, out double ez,
            out float bobPhase, out float bobAmount)
        {
            fx = Mc.x; fy = Mc.y; fz = Mc.z;
            ex = Mc.eyeX; ey = Mc.eyeY; ez = Mc.eyeZ;
            bobPhase = Mc.bobPhase; bobAmount = Mc.bobAmount;
            if (!haveMc || Mc.tickQpc == 0 || Mc.tickMs <= 0f) return;

            double qpcPerMs = Shm.QpcFrequency / 1000.0;
            long period = Math.Max(1, (long)Math.Round(Mc.tickMs * qpcPerMs));
            long now = Shm.Qpc();

            if (_ticks.Count == 0 || _ticks[_ticks.Count - 1].s.tickQpc != Mc.tickQpc)
            {
                if (_ticks.Count > 0 && Mc.tickQpc < _ticks[_ticks.Count - 1].s.tickQpc) _ticks.Clear();  // Minecraft restarted
                var tick = new Tick { s = Mc, at = Mc.tickQpc, slots = 1 };
                if (_ticks.Count > 0)
                {
                    var last = _ticks[_ticks.Count - 1];
                    long n = (long)Math.Round((double)(Mc.tickQpc - last.at) / period);
                    long err = Mc.tickQpc - (last.at + n * period);
                    if (n == 0 && last.slots >= 2)
                    {
                        // Minecraft ran two ticks in one frame and we saw both: the first belongs a tick earlier.
                        last.at -= period;
                        last.slots -= 1;
                        _ticks[_ticks.Count - 1] = last;
                        tick.at = last.at + period;
                    }
                    else if (n >= 1 && n <= 10 && Math.Abs(err) < period * 3 / 10)
                    {
                        tick.at = last.at + n * period + err / 16;  // the rhythm is exact; the stamps are noisy
                        tick.slots = (int)n;
                        _stampOutliers = 0;
                    }
                    else if (n <= 10 && ++_stampOutliers < 3)
                    {
                        tick.slots = (int)Math.Max(n, 1);
                        tick.at = last.at + tick.slots * period;
                    }
                    else _stampOutliers = 0;
                }
                if (_lastFrameQpc != 0)
                {
                    if (!_tickDueInit)
                    {
                        for (int i = 0; i < _tickDue.Length; i++) _tickDue[i] = _renderDelayMs - 1.0;
                        _tickDueInit = true;
                    }
                    double dueMs = (_lastFrameQpc - tick.at) / qpcPerMs;
                    if (dueMs < 30.0) _tickDue[_tickDueNext++ % _tickDue.Length] = dueMs;
                }
                _ticks.Add(tick);
                if (_ticks.Count > 8) _ticks.RemoveAt(0);
            }

            double frameMs = _lastFrameQpc != 0 ? (now - _lastFrameQpc) / qpcPerMs : 0.0;
            _lastFrameQpc = now;
            if (_tickDueInit)
            {
                double max = double.MinValue;
                foreach (var d in _tickDue) max = Math.Max(max, d);
                double target = Math.Min(Math.Max(max + 1.0, 4.0), 30.0);
                double sec = Math.Min(frameMs, 100.0) / 1000.0;
                _renderDelayMs = target > _renderDelayMs ? Math.Min(target, _renderDelayMs + 20.0 * sec) : Math.Max(target, _renderDelayMs - 2.0 * sec);
            }
            long renderQpc = now - (long)Math.Round(_renderDelayMs * qpcPerMs);

            int k = 0;
            for (int j = _ticks.Count - 1; j >= 0; j--)
                if (_ticks[j].at <= renderQpc) { k = j; break; }
            var t0 = _ticks[k];
            bool hasNext = k + 1 < _ticks.Count;
            double ticks = (double)(renderQpc - t0.at) / period;
            double t = Math.Min(Math.Max(ticks, 0.0), 1.0);
            var s0 = t0.s;
            fx = s0.prevX + (s0.curX - s0.prevX) * t;
            fy = s0.prevY + (s0.curY - s0.prevY) * t;
            fz = s0.prevZ + (s0.curZ - s0.prevZ) * t;
            double eyeHeight = s0.tickEyeO + (s0.tickEye - s0.tickEyeO) * t;
            bobPhase = -(s0.walkDist + (s0.walkDist - s0.walkDistO) * (float)t);
            bobAmount = s0.bobO + (s0.bob - s0.bobO) * (float)t;
            if (ticks > 1.0 && hasNext)
            {
                // Past this tick's end and the next one we have starts later: carry on to its start.
                var n = _ticks[k + 1];
                double gap = n.at - (t0.at + period);
                double u = gap > 0.0 ? Math.Min(Math.Max((renderQpc - (t0.at + period)) / gap, 0.0), 1.0) : 1.0;
                fx = s0.curX + (n.s.prevX - s0.curX) * u;
                fy = s0.curY + (n.s.prevY - s0.curY) * u;
                fz = s0.curZ + (n.s.prevZ - s0.curZ) * u;
                eyeHeight = s0.tickEye + (n.s.tickEyeO - s0.tickEye) * u;
                float endPhase = -(s0.walkDist + (s0.walkDist - s0.walkDistO));
                bobPhase = endPhase + (-n.s.walkDist - endPhase) * (float)u;
                bobAmount = s0.bob + (n.s.bobO - s0.bob) * (float)u;
            }
            ex = fx;
            ey = fy + eyeHeight;
            ez = fz;
        }
    }
}
