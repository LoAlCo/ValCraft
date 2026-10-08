using System.Collections.Generic;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Fire between the two worlds.
    // Minecraft's fire and lava (the blocks WorldRender knows burn) set what Valheim lets burn beside
    // them alight: wooden building pieces, trees, logs, dry grass. Valheim's own spreading fire (the
    // one its Ashlands cinders start) takes over from there, spreads, and is put out by rain as usual.
    // Valheim's fires near the player go to Minecraft (Proto.InValheimFire), which sets its flammable
    // blocks and mobs there alight (FireBridge.java).
    public static class FireBridge
    {
        const float CheckEvery = 1.5f;
        const float Range = 48f;           // Minecraft fire this near the player can spread into Valheim
        const float TellRange = 40f;       // Valheim fire this near the player is told to Minecraft
        const float Reach = 0.9f;          // how far from a Minecraft fire block it catches
        const float BlockCooldown = 20f;   // a Minecraft fire block lights Valheim at most this often
        const float GrassChance = 0.35f;   // dry grass beside Minecraft fire catches at this chance per check
        const int MaxNewFiresPerCheck = 3;

        static float _timer;
        static GameObject _firePrefab, _houseFirePrefab;
        static int _spread = 4;
        static bool _lookedUp;
        static readonly List<(Vector3 pos, int hazard)> _burning = new List<(Vector3, int)>();
        static readonly Dictionary<Vector3Int, float> _litAt = new Dictionary<Vector3Int, float>();
        static readonly Collider[] _hits = new Collider[24];
        static int _mask;

        // Main thread, every frame.
        public static void Frame(float dt)
        {
            var player = Player.m_localPlayer;
            if (!player || !Puppet.Puppeting || !Shm.Valid || !ZNetScene.instance) return;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = CheckEvery;
            try
            {
                SpreadIntoValheim(player);
                TellMinecraft(player);
            }
            catch (System.Exception e) { Plugin.Error("fire: " + e); }
        }

        // Valheim's spreading fire: the prefabs its cinders spawn when they land on something that burns.
        static bool LookUpPrefabs()
        {
            if (_lookedUp) return _firePrefab || _houseFirePrefab;
            _lookedUp = true;
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                var cinder = prefab ? prefab.GetComponent<Cinder>() : null;
                if (cinder == null || (!cinder.m_firePrefab && !cinder.m_houseFirePrefab)) continue;
                _firePrefab = cinder.m_firePrefab;
                _houseFirePrefab = cinder.m_houseFirePrefab ? cinder.m_houseFirePrefab : cinder.m_firePrefab;
                _spread = cinder.m_spread;
                Plugin.Log($"fire: Minecraft fire spreads as Valheim's {(_houseFirePrefab ? _houseFirePrefab.name : "?")} / {(_firePrefab ? _firePrefab.name : "?")} (from {prefab.name})");
                break;
            }
            if (!_firePrefab && !_houseFirePrefab) Plugin.Warn("fire: Valheim's spreading fire wasn't found; Minecraft fire won't spread into Valheim");
            return _firePrefab || _houseFirePrefab;
        }

        static void SpreadIntoValheim(Player player)
        {
            WorldRender.BurningBlocks(_burning);
            if (_burning.Count == 0 || !LookUpPrefabs()) return;
            if (_mask == 0) _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
            var me = player.transform.position;
            float now = Time.time;
            int started = 0;
            foreach (var (mcPos, hazard) in _burning)
            {
                if (started >= MaxNewFiresPerCheck) break;
                var at = Coords.ToValheim(new Vector3d(mcPos.x, mcPos.y, mcPos.z));
                if ((at - me).sqrMagnitude > Range * Range) continue;
                var key = Vector3Int.FloorToInt(mcPos);
                if (_litAt.TryGetValue(key, out float last) && now - last < BlockCooldown) continue;
                if (FireNear(at, 1.5f)) continue;
                int n = Physics.OverlapSphereNonAlloc(at, Reach, _hits, _mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var col = _hits[i];
                    if (!col || (WorldRender.Root && col.transform.IsChildOf(WorldRender.Root))) continue;
                    var point = col.ClosestPoint(at);
                    if (!Cinder.CanBurn(col, point, out bool isTerrain, hazard == WorldRender.HazardLava ? 1f : GrassChance)) continue;
                    var prefab = isTerrain ? _firePrefab : _houseFirePrefab;
                    if (!prefab) continue;
                    var normal = (at - point).sqrMagnitude > 1e-6f ? (at - point).normalized : Vector3.up;
                    var fire = Object.Instantiate(prefab, point + normal * 0.1f, Quaternion.identity);
                    fire.GetComponent<CinderSpawner>()?.Setup(_spread, col.gameObject);
                    _litAt[key] = now;
                    started++;
                    Plugin.Log($"fire: Minecraft {(hazard == WorldRender.HazardLava ? "lava" : "fire")} at {key} set {col.name} alight");
                    break;
                }
            }
            if (_litAt.Count > 512) _litAt.Clear();
        }

        static bool FireNear(Vector3 at, float radius)
        {
            foreach (var f in Fire.s_fires)
                if (f && (f.transform.position - at).sqrMagnitude < radius * radius) return true;
            return false;
        }

        // Every Valheim fire near the player, for Minecraft: its flammable blocks and mobs there catch.
        static void TellMinecraft(Player player)
        {
            var me = player.transform.position;
            int told = 0;
            foreach (var f in Fire.s_fires)
            {
                if (!f || (f.transform.position - me).sqrMagnitude > TellRange * TellRange) continue;
                var mc = Coords.ToMc(f.transform.position);
                Shm.PushInput(Proto.InValheimFire, 0, Mathf.RoundToInt((float)mc.x * 8f), Mathf.RoundToInt((float)mc.y * 8f), Mathf.RoundToInt((float)mc.z * 8f));
                if (++told >= 16) break;
            }
        }
    }
}
