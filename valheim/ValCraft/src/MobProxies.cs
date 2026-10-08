using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Minecraft's mobs as Valheim sees them: each mob near the player (Proto.RenMobs, every Minecraft
    // tick) gets an invisible stand-in Character where the mob is, the mob's size, so Valheim's
    // creatures notice it, fight it or flee it the Valheim way. Minecraft's monsters (and its golems
    // and tamed pets) are on the side Valheim's monsters attack and its animals run from (the faction
    // of the player's own summons), so greydwarves go for zombies and tamed wolves defend against
    // them; Minecraft's animals are Valheim's animals to them. A Valheim creature's hit on a stand-in
    // goes to Minecraft (Proto.InMobHit) as that creature's hit on the mob, so the mob turns on it.
    // Stand-ins are this game's own (no network object): other players' games make their own.
    public static unsafe class MobProxies
    {
        struct Mob { public int id; public bool fights; public Vector3 mc; public float yaw, width, height, health, maxHealth; public string name; }

        sealed class Proxy
        {
            public GameObject go;
            public Character character;
            public CapsuleCollider collider;
            public Rigidbody body;
            public Transform eye;
            public bool fights;
            public float lastSeen;
        }

        static readonly Dictionary<int, Proxy> _proxies = new Dictionary<int, Proxy>();
        static readonly Dictionary<Character, int> _ids = new Dictionary<Character, int>();
        static readonly List<Mob> _mobs = new List<Mob>();
        static bool _fresh;
        static int _layer = -1;

        /** True if this is a stand-in for a Minecraft mob (not a Valheim creature). */
        public static bool IsProxy(Character c) => c && _ids.ContainsKey(c);

        public static Character ProxyFor(int mobId) => _proxies.TryGetValue(mobId, out var p) && p.character ? p.character : null;

        // From the render ring (WorldRender): int count, then per mob id, flags, x y z yaw width height
        // health maxHealth, name length, name.
        public static void OnMobs(byte* p, uint bytes)
        {
            _mobs.Clear();
            int n = *(int*)p;
            byte* q = p + 4, end = p + bytes;
            for (int i = 0; i < n && q + 44 <= end; i++)
            {
                var m = new Mob
                {
                    id = *(int*)q, fights = (*(int*)(q + 4) & 1) != 0,
                    mc = new Vector3(*(float*)(q + 8), *(float*)(q + 12), *(float*)(q + 16)),
                    yaw = *(float*)(q + 20), width = *(float*)(q + 24), height = *(float*)(q + 28),
                    health = *(float*)(q + 32), maxHealth = *(float*)(q + 36),
                };
                int len = *(int*)(q + 40);
                q += 44;
                if (len < 0 || q + len > end) break;
                m.name = len > 0 ? new string((sbyte*)q, 0, len, System.Text.Encoding.UTF8) : "";
                q += len;
                _mobs.Add(m);
            }
            _fresh = true;
        }

        // Main thread, every frame.
        public static void Frame()
        {
            bool active = Puppet.Puppeting && Shm.Valid && Player.m_localPlayer && ZNetScene.instance;
            if (!active)
            {
                if (_proxies.Count > 0) Clear();
                return;
            }
            float now = Time.time;
            if (_fresh)
            {
                _fresh = false;
                foreach (var m in _mobs)
                {
                    if (!_proxies.TryGetValue(m.id, out var proxy) || !proxy.go)
                    {
                        proxy = Make(m);
                        if (proxy == null) continue;
                        _proxies[m.id] = proxy;
                        _ids[proxy.character] = m.id;
                    }
                    proxy.lastSeen = now;
                    Place(proxy, m);
                }
            }
            // Gone from Minecraft (dead, unloaded, out of range): gone here.
            List<int> gone = null;
            foreach (var kv in _proxies)
                if (!kv.Value.go || now - kv.Value.lastSeen > 1.0f) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null) foreach (var id in gone) Remove(id);
        }

        static Proxy Make(Mob m)
        {
            if (_layer < 0) _layer = LayerMask.NameToLayer("character");
            // Built inactive, with its network object switched off before anything wakes up.
            var go = new GameObject("ValCraft mob " + m.id);
            go.SetActive(false);
            go.layer = _layer;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var collider = go.AddComponent<CapsuleCollider>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.AddComponent<Animator>();
            var eye = new GameObject("Eye").transform;
            eye.SetParent(go.transform, false);
            ZNetView.m_forceDisableInit = true;
            Character c;
            try
            {
                go.AddComponent<ZNetView>();
                c = go.AddComponent<Character>();
                c.m_name = string.IsNullOrEmpty(m.name) ? "Minecraft mob" : m.name;
                c.m_faction = m.fights ? Character.Faction.PlayerSpawned : Character.Faction.AnimalsVeg;
                c.m_eye = eye;
                c.m_health = Mathf.Max(1f, m.maxHealth * ValheimDamagePerMc);
                go.SetActive(true);
            }
            catch (System.Exception e)
            {
                Plugin.Warn("mob stand-in: " + e.Message);
                Object.Destroy(go);
                return null;
            }
            finally { ZNetView.m_forceDisableInit = false; }
            var animator = visual.GetComponent<Animator>();
            if (animator) animator.enabled = false;
            return new Proxy { go = go, character = c, collider = collider, body = body, eye = eye, fights = m.fights };
        }

        // Minecraft's health x this = Valheim's (Combat's Valheim -> Minecraft damage is / 5)
        const float ValheimDamagePerMc = 5f;

        static void Place(Proxy p, Mob m)
        {
            float w = Mathf.Clamp(m.width, 0.2f, 6f), h = Mathf.Clamp(m.height, 0.2f, 8f);
            p.collider.radius = w * 0.5f;
            p.collider.height = Mathf.Max(h, w);
            p.collider.center = new Vector3(0f, h * 0.5f, 0f);
            p.eye.localPosition = new Vector3(0f, h * 0.85f, w * 0.3f);
            var pos = Coords.ToValheim(new Vector3d(m.mc.x, m.mc.y, m.mc.z));
            p.go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Coords.McYawToUnity(m.yaw), 0f));
            if (p.fights != m.fights)
            {
                p.fights = m.fights;
                p.character.m_faction = m.fights ? Character.Faction.PlayerSpawned : Character.Faction.AnimalsVeg;
            }
        }

        static void Remove(int id)
        {
            if (!_proxies.TryGetValue(id, out var p)) return;
            _proxies.Remove(id);
            if (p.character) _ids.Remove(p.character);
            if (p.go) Object.Destroy(p.go);
            foreach (var key in new List<Character>(_ids.Keys)) if (!key) _ids.Remove(key);
        }

        static void Clear()
        {
            foreach (var id in new List<int>(_proxies.Keys)) Remove(id);
            _ids.Clear();
        }

        // ---- Valheim's creatures and the stand-ins -------------------------------------------------

        // A hit on a stand-in (a Valheim creature's bite, arrow, ...): to Minecraft, as that creature's.
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        static class HitOnStandIn
        {
            static bool Prefix(Character __instance, HitData hit)
            {
                if (!_ids.TryGetValue(__instance, out int mobId)) return true;
                // only a creature's own hit: explosions and Valheim's fire reach Minecraft's mobs by their own ways
                var attacker = hit.GetAttacker();
                float damage = hit.GetTotalDamage();
                if (damage > 0f && attacker && !IsProxy(attacker))
                    Shm.PushInput(Proto.InMobHit, 0, mobId, Mathf.RoundToInt(damage * 100f), attacker ? (int)Combat.IdOf(attacker) : 0);
                return false;
            }
        }

        // Valheim's floating health bars expect every character to have an AI: none over stand-ins.
        [HarmonyPatch(typeof(EnemyHud), "TestShow")]
        static class NoHudOverStandIns
        {
            static bool Prefix(Character c, ref bool __result)
            {
                if (!IsProxy(c)) return true;
                __result = false;
                return false;
            }
        }

        // A Minecraft mob hit a Valheim creature (Combat.HitActor): the creature turns on its stand-in.
        static readonly System.Reflection.MethodInfo MonsterSetTarget = AccessTools.Method(typeof(MonsterAI), "SetTarget", new[] { typeof(Character) });

        public static void Provoke(Character victim, int mobId)
        {
            var proxy = ProxyFor(mobId);
            if (!victim || !proxy) return;
            var ai = victim.GetBaseAI();
            if (ai is MonsterAI monster && MonsterSetTarget != null)
            {
                try { MonsterSetTarget.Invoke(monster, new object[] { proxy }); }
                catch (System.Exception e) { Plugin.Warn("mob stand-in: provoke: " + e.Message); }
            }
            if (ai) ai.Alert();
        }
    }
}
