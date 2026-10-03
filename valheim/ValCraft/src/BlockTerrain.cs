using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Block terrain (F8, or [Terrain] BlockTerrain): Valheim's ground becomes real Minecraft blocks.
    // Valheim sends Minecraft each column's height and biome around the player (kColTerrain on the
    // collision ring); Minecraft builds the columns once, in a save of its own (ValCraft-<id>-blocks,
    // so normal-mode builds are untouched), and they mine, light and save like any Minecraft terrain.
    // Meanwhile Valheim's terrain is hidden and no longer sent as collision (the blocks are the
    // ground now); its colliders stay, so Valheim's creatures still walk on it. Trees, rocks,
    // buildings and dungeons stay Valheim's.
    public static class BlockTerrain
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> Radius;
        public static bool On => Enabled != null && Enabled.Value && !Coords.Interior;

        // When each chunk was last sent. Sent again every ResendSeconds while near: one lost on the way
        // (Minecraft switching saves, a full ring) fills in by itself; Minecraft skips chunks it built.
        static readonly Dictionary<long, float> _sent = new Dictionary<long, float>();
        const float ResendSeconds = 10f;
        static readonly List<Renderer> _hidden = new List<Renderer>();
        static bool _wasOn;
        static float _hideTimer;
        static uint _epoch;
        static readonly byte[] _payload = new byte[16 + 256 * 4 + 256];

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("Terrain", "BlockTerrain", false,
                "Valheim's ground as real Minecraft blocks (mine it, build into it). Toggle in game with F8 ([Controls] BlockTerrain). Uses its own Minecraft save, so builds in normal mode are kept apart.");
            Radius = config.Bind("Terrain", "Radius", 80, "How far around you (metres) Valheim's ground is turned into blocks.");
        }

        public static void Toggle()
        {
            Enabled.Value = !Enabled.Value;
            Plugin.Message(Enabled.Value ? "ValCraft: block terrain ON (Minecraft switches worlds)" : "ValCraft: block terrain OFF");
        }

        // Main thread, every frame, while Minecraft drives the player.
        public static void Frame(Vector3d playerMc, uint epoch, float dt)
        {
            bool on = On && Player.m_localPlayer;
            if (on != _wasOn)
            {
                _wasOn = on;
                _sent.Clear();
                if (!on) ShowValheimGround();
            }
            if (epoch != _epoch) { _epoch = epoch; _sent.Clear(); }  // Minecraft dropped everything (new world): send again
            if (!on)
            {
                // Valheim terrain: Minecraft still gets the ground's height around the player, which its
                // mobs walk on (ValGround) instead of reading it from collision.
                if (Player.m_localPlayer && !Coords.Interior) SendChunks(playerMc, PathingRadius);
                return;
            }
            _hideTimer -= dt;
            if (_hideTimer <= 0f) { _hideTimer = 0.5f; HideValheimGround(); }
            SendChunks(playerMc, Radius.Value);
        }

        const int PathingRadius = 64;

        static void HideValheimGround()
        {
            foreach (var hm in Heightmap.GetAllHeightmaps())
            {
                if (!hm) continue;
                foreach (var r in hm.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; _hidden.Add(r); }
            }
            // Valheim's grass would poke through the blocks.
            if (ClutterSystem.instance && ClutterSystem.instance.enabled)
            {
                ClutterSystem.instance.enabled = false;
                ClutterSystem.instance.ClearAll();
            }
        }

        static void ShowValheimGround()
        {
            foreach (var r in _hidden) if (r) r.enabled = true;
            _hidden.Clear();
            if (ClutterSystem.instance) ClutterSystem.instance.enabled = true;
        }

        // Nearest chunks first, a few per frame; each once per Minecraft session (Minecraft also
        // remembers which it built, so resending is harmless).
        static void SendChunks(Vector3d p, int radius)
        {
            int pcx = Mathf.FloorToInt((float)p.x / 16f), pcz = Mathf.FloorToInt((float)p.z / 16f);
            int r = Mathf.Clamp(radius, 16, 256) / 16;
            int budget = 3;
            float now = Time.realtimeSinceStartup;
            if (_sent.Count > 4096) _sent.Clear();
            for (int ring = 0; ring <= r && budget > 0; ring++)
                for (int dz = -ring; dz <= ring && budget > 0; dz++)
                    for (int dx = -ring; dx <= ring && budget > 0; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                        int cx = pcx + dx, cz = pcz + dz;
                        long key = ((long)cx << 32) ^ (uint)cz;
                        if (_sent.TryGetValue(key, out float at) && now - at < ResendSeconds) continue;
                        if (!SendChunk(cx, cz)) continue;  // its heightmap isn't loaded yet: later
                        _sent[key] = now;
                        budget--;
                    }
        }

        // Payload: cx, cz (Minecraft chunk), epoch, flags (1: floors follow); then 256 columns [x + 16 z]
        // of {short top (Minecraft y of the surface block), byte biome, byte surface fraction}; then 256
        // bytes: how many blocks under top the bedrock goes (Valheim's dig limit, 8 m under the ground
        // as the world made it).
        static unsafe bool SendChunk(int cx, int cz)
        {
            var gen = WorldGenerator.instance;
            if (gen == null) return false;
            fixed (byte* b = _payload)
            {
                *(int*)b = cx;
                *(int*)(b + 4) = cz;
                *(uint*)(b + 8) = _epoch;
                *(uint*)(b + 12) = 1;
                byte* col = b + 16;
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        // Minecraft column (mx, mz) spans mz..mz+1, which is Valheim -(mz+1)..-mz.
                        int mx = cx * 16 + x, mz = cz * 16 + z;
                        var v = Coords.ToValheim(mx + 0.5, 0, mz + 0.5);
                        if (!Ground.Height(v, out float h)) return false;
                        float surface = h - (float)Coords.YOffset;
                        int top = Mathf.RoundToInt(surface) - 1;  // surface block's top face at the ground
                        var biome = gen.GetBiome(v.x, v.z);
                        byte* c = col + (x + 16 * z) * 4;
                        *(short*)c = (short)Mathf.Clamp(top, -1000, 1000);
                        c[2] = BiomeCode(biome);
                        // The exact surface: top + 0.5 + c[3] / 255 (Minecraft's mobs walk on it, ValGround).
                        c[3] = (byte)Mathf.Clamp(Mathf.RoundToInt((surface - top - 0.5f) * 255f), 0, 255);
                        // Bedrock: its top face at the deepest Valheim's ground can be dug to.
                        float floor = (Ground.BaseHeight(v, out float baseH) ? baseH : h) - Ground.DigLimit - (float)Coords.YOffset;
                        int bedrock = Mathf.RoundToInt(floor) - 1;
                        b[16 + 256 * 4 + x + 16 * z] = (byte)Mathf.Clamp(top - bedrock, 1, 255);
                    }
            }
            // Through the collision worker: the collision ring has a single writer.
            Collision.EnqueueRaw(Proto.ColTerrain, (byte[])_payload.Clone());
            return true;
        }

        // Minecraft side mirror: TerrainGen.BIOME_*.
        static byte BiomeCode(Heightmap.Biome b)
        {
            switch (b)
            {
                case Heightmap.Biome.Meadows: return 1;
                case Heightmap.Biome.BlackForest: return 2;
                case Heightmap.Biome.Swamp: return 3;
                case Heightmap.Biome.Mountain: return 4;
                case Heightmap.Biome.Plains: return 5;
                case Heightmap.Biome.Mistlands: return 6;
                case Heightmap.Biome.AshLands: return 7;
                case Heightmap.Biome.DeepNorth: return 8;
                case Heightmap.Biome.Ocean: return 9;
                default: return 1;
            }
        }
    }
}
