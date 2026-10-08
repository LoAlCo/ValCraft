using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Combat both ways (SkyCraft's Combat.cpp, for Valheim).
    //  - Every Valheim creature near the player goes into the actor table; Minecraft mirrors each as an
    //    invisible, hittable stand-in, so swords, crits, sweeps, bows and tridents work on it with
    //    Minecraft's own code.
    //  - Minecraft's hits come back as events and are applied to the real creature through Valheim's
    //    own damage path (hit reaction, stagger, aggro, loot, skill gain).
    //  - Valheim's hits on the player are cancelled and sent to Minecraft as damage from the attacker's
    //    stand-in, so Minecraft's armour, shields, knockback and death decide what happens.
    public static unsafe class Combat
    {
        public static ConfigEntry<float> DamageScale;
        public static ConfigEntry<float> Range;
        public static ConfigEntry<float> ExplosionDamage, ExplosionCraters;
        public static ConfigEntry<bool> CreeperGriefing, NaturalSpawning;

        static readonly ActorRecord[] _records = new ActorRecord[Proto.MaxActors];
        static readonly Dictionary<uint, Character> _byId = new Dictionary<uint, Character>();
        static readonly List<(float d, Character c)> _near = new List<(float, Character)>();
        static float _tableTimer;

        public static void Init(ConfigFile config)
        {
            DamageScale = config.Bind("Combat", "DamageScale", 4f,
                "Minecraft damage x this = Valheim damage (a diamond sword's 7, or 10.5 on a crit, becomes 28 / 42). Valheim's hits on you are divided by 5 on the Minecraft side.");
            Range = config.Bind("Combat", "Range", 48f, "Valheim creatures within this many metres get Minecraft stand-ins.");
            ExplosionDamage = config.Bind("Explosions", "Damage", 2f,
                "Multiplier for the damage Minecraft explosions (TNT, creepers) do to Valheim's creatures, trees, rocks and buildings. 1 = a TNT blast does about 100 at its centre, 2 = twice that.");
            ExplosionCraters = config.Bind("Explosions", "Craters", 1f,
                "Multiplier for the size of the craters Minecraft explosions blow in Valheim's ground. 1 = a TNT blast leaves one about 7 m across and 2 m deep, 2 = twice as big, 0 = no craters.");
            CreeperGriefing = config.Bind("Explosions", "CreeperGriefing", false,
                "Creeper explosions break the environment: craters in Valheim's ground, trees, rocks and building pieces, and Minecraft blocks in block terrain (Minecraft's mobGriefing rule, so endermen pick up blocks too). Off: they still hurt players and creatures. TNT always breaks things.");
            NaturalSpawning = config.Bind("Mobs", "NaturalSpawning", false,
                "Minecraft mobs spawn on their own in Valheim's world: monsters at night, animals any time, chosen by biome (husks on the Plains, strays in the Mountains, slimes and witches in the Swamp, ...). Off: only spawn eggs and commands make them.");
            MobPathing = config.Bind("Mobs", "Pathfinding", "Balanced",
                new ConfigDescription("How hard Minecraft's mobs work out their way over Valheim's terrain. High: they plan further and react quickest. Low: lightest on the CPU, for slower PCs (mobs re-plan less often and over shorter distances).",
                    new AcceptableValueList<string>("High", "Balanced", "Low")));
        }

        public static ConfigEntry<string> MobPathing;
        static readonly System.Reflection.FieldInfo LastHit = HarmonyLib.AccessTools.Field(typeof(Character), "m_lastHit");

        /** Mobs' pathfinding setting for Minecraft (Proto.ValMobPathingShift). */
        public static uint MobPathingBits => (MobPathing == null ? 0u : MobPathing.Value == "Low" ? 1u : MobPathing.Value == "High" ? 2u : 0u) << Proto.ValMobPathingShift;

        /** [Mobs] NaturalSpawning and [Explosions] CreeperGriefing for Minecraft (Proto.ValMobSpawning / ValMobGriefing). */
        public static uint MobSpawningBits => (NaturalSpawning != null && NaturalSpawning.Value ? Proto.ValMobSpawning : 0u) |
                                              (CreeperGriefing != null && CreeperGriefing.Value ? Proto.ValMobGriefing : 0u);

        public static uint IdOf(Character c)
        {
            var id = c.GetZDOID();
            uint h = (uint)id.ID * 2654435761u ^ (uint)id.UserID ^ (uint)(id.UserID >> 32);
            return h == 0 ? 1u : h;
        }

        // Main thread, every frame.
        public static void Frame(float dt)
        {
            Craters.Frame();
            var player = Player.m_localPlayer;
            bool active = Puppet.Puppeting && player && Shm.Valid;

            _tableTimer -= dt;
            if (_tableTimer <= 0f)
            {
                _tableTimer = 0.05f;  // 20 Hz, Minecraft's tick rate; ProxySync puts the stand-ins exactly each frame
                WriteTable(active ? player : null);
            }

            while (Shm.PopEvent(out var ev))
            {
                if (!active) continue;
                try { OnEvent(player, ev); }
                catch (System.Exception e) { Plugin.Error("combat event: " + e); }
            }
        }

        static void WriteTable(Player player)
        {
            _byId.Clear();
            _near.Clear();
            int count = 0;
            if (player)
            {
                var me = player.transform.position;
                float range = Range.Value;
                foreach (var c in Character.GetAllCharacters())
                {
                    if (!c || c == player || c.GetZDOID().IsNone()) continue;
                    if (Multiplayer.SharesWorld(c)) continue;  // in our Minecraft world: their Minecraft player is what gets hit
                    if (MobProxies.IsProxy(c)) continue;  // a Minecraft mob's own stand-in
                    float d = (c.transform.position - me).sqrMagnitude;
                    if (d < range * range) _near.Add((d, c));
                }
                _near.Sort((a, b) => a.d.CompareTo(b.d));
                foreach (var (_, c) in _near)
                {
                    if (count >= Proto.MaxActors) break;
                    uint id = IdOf(c);
                    _byId[id] = c;
                    var r = new ActorRecord();
                    r.formId = id;
                    uint flags = 0;
                    if (BaseAI.IsEnemy(c, player)) flags |= 1;          // hostile
                    if (c.IsDead()) flags |= 2;                         // dead
                    if (c.IsTamed() || c.IsPlayer()) flags |= 4;        // essential: never killed by Minecraft
                    var ai = c.GetBaseAI();
                    if (ai && ai.IsAlerted()) flags |= 8;               // in combat
                    r.flags = flags;
                    var p = Coords.ToMc(c.transform.position);
                    r.x = (float)p.x; r.y = (float)p.y; r.z = (float)p.z;
                    r.yaw = Coords.UnityYawToMc(c.transform.eulerAngles.y);
                    Size(c, out r.width, out r.height);
                    r.healthFrac = Mathf.Clamp01(c.GetHealthPercentage());
                    r.level = (ushort)Mathf.Max(1, c.GetLevel());
                    WriteName(ref r, Localization.instance != null ? Localization.instance.Localize(c.m_name) : c.m_name);
                    _records[count++] = r;
                }
            }
            Shm.WriteActors(_records, count);
        }

        static void Size(Character c, out float width, out float height)
        {
            var col = c.GetCollider();
            if (col is CapsuleCollider cap)
            {
                var s = cap.transform.lossyScale;
                width = cap.radius * 2f * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                height = cap.height * Mathf.Abs(s.y);
            }
            else if (col)
            {
                var b = col.bounds.size;
                width = Mathf.Max(b.x, b.z);
                height = b.y;
            }
            else { width = 0.6f; height = 1.8f; }
            width = Mathf.Clamp(width, 0.2f, 8f);
            height = Mathf.Clamp(height, 0.2f, 12f);
        }

        static void WriteName(ref ActorRecord r, string name)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name ?? "");
            int n = Mathf.Min(bytes.Length, 23);
            fixed (byte* dst = r.name)
            {
                for (int i = 0; i < n; i++) dst[i] = bytes[i];
                dst[n] = 0;
            }
        }

        const uint ExplosionKeepsBlocks = 1;  // EV_EXPLOSION flags: broke no blocks (creeper, mobGriefing off)
        const uint EvHitActor = 1, EvPlayerDied = 2, EvExplosion = 3, EvArrowStuck = 4, EvSkillUse = 5, EvValheimHit = 6, EvBuildSync = 7, EvSetTime = 9, EvConsume = 10, EvHitWeapon = 11, EvIgnite = 12;
        const uint HitCritical = 1, HitProjectile = 2, HitSweep = 4, HitFire = 8, HitMob = 16, HitPureFire = 32;
        const uint WeaponUnarmed = 0, WeaponBlade = 1, WeaponAxe = 2, WeaponBlunt = 3, WeaponPierce = 4, WeaponArrow = 5;

        static void OnEvent(Player player, McEvent ev)
        {
            switch (ev.type)
            {
                case EvHitActor:
                    if (_byId.TryGetValue(ev.formId, out var target) && target && !target.IsDead()) HitActor(player, target, ev);
                    break;
                case EvPlayerDied:
                    Plugin.Log("Minecraft player died: the Viking dies too");
                    // Valheim's death handling reads the last hit (for its death stats); setting the
                    // health alone left it null, OnDeath threw, and the respawn never came.
                    if (LastHit.GetValue(player) == null) LastHit.SetValue(player, new HitData { m_hitType = HitData.HitType.Undefined });
                    player.SetHealth(0f);
                    break;
                case EvExplosion:
                    Explosion(player, new Vector3d(ev.a, ev.b, ev.c), ev.d, (ev.flags & ExplosionKeepsBlocks) == 0);
                    break;
                case EvSkillUse:
                    // Minecraft reports shield blocks as ActorValue 9 (Block).
                    if (ev.formId == 9) player.RaiseSkill(Skills.SkillType.Blocking, Mathf.Clamp(ev.a, 0.1f, 5f));
                    break;
                case EvValheimHit:
                    Harvest(player, ev.formId, new Vector3d(ev.a, ev.b, ev.c), ev.d);
                    break;
                case EvHitWeapon:
                    _hitWeapon = ValheimItemByHash((int)ev.formId);
                    _hitWeaponBase = ev.a;
                    break;
                case EvConsume:
                    Consume(player, (int)ev.formId);
                    break;
                case EvIgnite:
                    if (_byId.TryGetValue(ev.formId, out var burning) && burning && !burning.IsDead()) Ignite(burning, ev.a);
                    break;
                case EvSetTime:
                    SetTime(ev.a, ev.b);
                    break;
                case EvBuildSync:
                    // Minecraft is putting builds from the other terrain mode (F8) into this one.
                    Plugin.Message(ev.formId != 0 ? "ValCraft: your builds from the other mode will appear shortly…" : "ValCraft: builds synced");
                    break;
                case EvArrowStuck:
                    // formId, a/b/c = where it hit (Minecraft), d = flight yaw, flags = flight pitch (float bits), weapon = arrow kind
                    if (_byId.TryGetValue(ev.formId, out var shot) && shot)
                        Render.StuckArrows.Stick(shot, new Vector3d(ev.a, ev.b, ev.c), ev.d, System.BitConverter.Int32BitsToSingle((int)ev.flags), (int)ev.weapon);
                    break;
                default:
                    break;
            }
        }

        // ---- Minecraft tools on Valheim's world --------------------------------------------------

        const uint ToolNone = 0, ToolSword = 1, ToolAxe = 2, ToolPickaxe = 3, ToolShovel = 4, ToolHoe = 5;
        static readonly Collider[] _probe = new Collider[16];
        static GameObject _digPrefab;

        // A swing that landed on Valheim geometry: axes chop trees and logs, pickaxes break rocks, ore
        // and buildings and dig the ground, shovels dig. Valheim's own hit handling does the rest
        // (effects, drops, tool-tier checks); a Minecraft tier maps to Valheim's one higher (wood =
        // antler/flint, stone = bronze, iron = iron, diamond = black metal, netherite beyond).
        static void Harvest(Player player, uint tool, Vector3d atMc, float strength)
        {
            uint kind = tool & 0xF;
            int tier = (int)((tool >> 4) & 0xF);
            if (BuildTools.Active) return;  // Valheim's build mode (hoe, hammer) has the click
            var point = Coords.ToValheim(atMc);
            int n = Physics.OverlapSphereNonAlloc(point, 0.3f, _probe, ~0, QueryTriggerInteraction.Ignore);
            IDestructible target = null;
            Collider targetCol = null;
            bool terrain = false;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = _probe[i];
                if (!col || (WorldRender.Root && col.transform.IsChildOf(WorldRender.Root))) continue;
                if (col.attachedRigidbody && col.attachedRigidbody.GetComponent<Character>()) continue;  // creatures: Minecraft's own attack
                float d = Vector3.Distance(point, col.ClosestPoint(point));
                if (col.GetComponentInParent<Heightmap>()) { if (d < 0.35f) terrain = true; continue; }
                var dest = col.GetComponentInParent<IDestructible>();
                if (dest != null && d < best) { best = d; target = dest; targetCol = col; }
            }
            float power = Mathf.Clamp(strength, 0.2f, 1f);
            if (target != null)
            {
                var hit = new HitData();
                hit.SetAttacker(player);
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.m_point = point;
                hit.m_dir = (point - player.GetEyePoint()).normalized;
                hit.m_hitCollider = targetCol;
                hit.m_toolTier = (short)(tier + 1);
                float amount = (12f + 6f * tier) * power;  // diamond axe 30: about a Valheim bronze axe
                switch (kind)
                {
                    case ToolAxe: hit.m_damage.m_chop = amount; hit.m_damage.m_slash = amount * 0.5f; hit.m_skill = Skills.SkillType.WoodCutting; break;
                    case ToolPickaxe: hit.m_damage.m_pickaxe = amount; hit.m_damage.m_pierce = amount * 0.3f; hit.m_skill = Skills.SkillType.Pickaxes; break;
                    case ToolSword: hit.m_damage.m_slash = amount * 0.5f; hit.m_skill = Skills.SkillType.Swords; break;
                    case ToolShovel: case ToolHoe: case ToolNone: default: hit.m_damage.m_blunt = 4f * power; break;
                }
                target.Damage(hit);
                return;
            }
            DigTerrain(player, kind, point, terrain);
        }

        static int _terrainMask = -1;
        static float _toolHintAt;

        // Valheim's ground: a shovel digs soil (dirt, grass, sand, snow), and rock (the steep slopes
        // Valheim draws as cliff, and paved ground) takes a pickaxe. The ground is found with a ray
        // from the eye through the hit, so a hit point slightly off Valheim's real terrain still counts.
        static void DigTerrain(Player player, uint kind, Vector3 point, bool terrainNear)
        {
            if (_terrainMask < 0) _terrainMask = LayerMask.GetMask("terrain");
            Vector3 eye = GameCamera.instance ? GameCamera.instance.transform.position : player.GetEyePoint();
            Vector3 to = point - eye;
            Vector3 normal = Vector3.up;
            bool found = false;
            if (to.sqrMagnitude > 1e-4f && Physics.Raycast(eye, to.normalized, out var rh, to.magnitude + 1f, _terrainMask, QueryTriggerInteraction.Ignore)
                && rh.collider.GetComponentInParent<Heightmap>())
            {
                point = rh.point;
                normal = rh.normal;
                found = true;
            }
            if (!found && !terrainNear) return;
            var hm = Heightmap.FindHeightmap(point);
            bool paved = hm && hm.GetPaintMask(point).b > 0.5f;
            bool rock = normal.y < RockSlope || paved;
            uint needed = rock ? ToolPickaxe : ToolShovel;
            if (kind == needed) { Dig(player, point); return; }
            if ((kind == ToolPickaxe || kind == ToolShovel) && Time.unscaledTime > _toolHintAt)
            {
                _toolHintAt = Time.unscaledTime + 3f;
                if (MessageHud.instance) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, rock ? "This is rock: dig it with a pickaxe" : "This is soil: dig it with a shovel");
            }
        }

        // Valheim's terrain shows cliff rock where it's steeper than about 40 degrees.
        const float RockSlope = 0.76f;

        static void Dig(Player player, Vector3 point)
        {
            var prefab = DigPrefab();
            if (prefab) global::Attack.SpawnOnHitTerrain(point, prefab, player, 0f, null, null);
        }

        // What a Valheim pickaxe leaves in the ground when it hits it (a dig).
        static GameObject DigPrefab()
        {
            if (!_digPrefab && ObjectDB.instance)
                foreach (var go in ObjectDB.instance.m_items)
                {
                    var shared = go ? go.GetComponent<ItemDrop>()?.m_itemData.m_shared : null;
                    if (shared != null && shared.m_spawnOnHitTerrain && shared.m_skillType == Skills.SkillType.Pickaxes) { _digPrefab = shared.m_spawnOnHitTerrain; break; }
                }
            return _digPrefab;
        }

        // A full hit of a Minecraft sword of each tier (1 wood .. 6 netherite), in Valheim damage: about
        // the one-handed Valheim weapons of the matching tier (flint, bronze, iron, black metal, ...).
        static readonly float[] TierDamage = { 0f, 12f, 35f, 45f, 55f, 90f, 120f };

        // Axes swing slower and hit harder; pickaxes, shovels and hoes are tools first.
        static float KindFactor(int kind) => kind == 2 ? 1.2f : kind == 3 ? 0.6f : 1f;

        // Set by EvHitWeapon: the next hit was made with a ValCraft version of this Valheim weapon.
        static GameObject _hitWeapon;
        static float _hitWeaponBase;

        static void HitActor(Player player, Character target, McEvent ev)
        {
            var weapon = _hitWeapon ? _hitWeapon.GetComponent<ItemDrop>()?.m_itemData.m_shared : null;
            float weaponBase = _hitWeaponBase;
            _hitWeapon = null;
            uint weaponClass = ev.weapon & 0xFF, tool = ev.weapon >> 8;
            float amount = ev.a * DamageScale.Value;
            bool fromMob = (ev.flags & HitMob) != 0;
            if (tool != 0 && weapon == null && !fromMob)
            {
                // A Minecraft tool hits like the Valheim weapons of its tier (wood ~ flint, stone ~ bronze,
                // iron ~ iron, diamond ~ black metal, netherite beyond): Minecraft's damage barely grows
                // with the material, Valheim's creatures' health does. Scaled by how much of a full hit
                // this was (crits, Sharpness, Strength, cooldown). DamageScale 4 (the default) = as listed.
                int tier = (int)(tool & 0xF), kind = (int)((tool >> 4) & 0xF);
                float full = ((tool >> 8) & 0xFF) / 10f;
                if (tier >= 1 && tier < TierDamage.Length && full > 0f)
                    amount = TierDamage[tier] * KindFactor(kind) * (ev.a / full) * (DamageScale.Value / 4f);
            }
            var hit = new HitData();
            // A Minecraft mob's hit (zombie, skeleton's arrow, creeper): no attacker, so the creature
            // doesn't turn on the player for it, and no skill for the player.
            bool byMob = (ev.flags & HitMob) != 0;
            hit.m_hitType = byMob ? HitData.HitType.EnemyHit : HitData.HitType.PlayerHit;
            if (!byMob) hit.SetAttacker(player);
            hit.m_point = target.GetCenterPoint();
            var push = new Vector3(ev.b, 0f, -ev.c);
            hit.m_dir = push.sqrMagnitude > 1e-6f ? push.normalized : (target.transform.position - player.transform.position).normalized;
            hit.m_pushForce = ev.d * 40f;  // Minecraft knockback 0.4 (a plain hit) ~ a Valheim sword's push
            hit.m_blockable = hit.m_dodgeable = false;
            hit.m_ranged = (ev.flags & HitProjectile) != 0;
            hit.m_staggerMultiplier = (ev.flags & HitCritical) != 0 ? 2f : 1f;
            switch (weaponClass)
            {
                case WeaponBlade: hit.m_damage.m_slash = amount; hit.m_skill = Skills.SkillType.Swords; break;
                case WeaponAxe: hit.m_damage.m_slash = amount; hit.m_skill = Skills.SkillType.Axes; break;
                case WeaponPierce: hit.m_damage.m_pierce = amount; hit.m_skill = Skills.SkillType.Spears; break;
                case WeaponArrow: hit.m_damage.m_pierce = amount; hit.m_skill = Skills.SkillType.Bows; break;
                case WeaponUnarmed: hit.m_damage.m_blunt = amount; hit.m_skill = Skills.SkillType.Unarmed; break;
                default: hit.m_damage.m_blunt = amount; hit.m_skill = Skills.SkillType.Clubs; break;
            }
            if ((ev.flags & HitFire) != 0) hit.m_damage.m_fire = amount * 0.25f;
            if (weapon != null && weaponBase > 0f)
            {
                // The Valheim weapon's own damage (with its fire, frost, poison, ...), by how much of a
                // full Minecraft hit this was (attack cooldown, crits, strength).
                var d = weapon.m_damages.Clone();
                d.Modify(ev.a / weaponBase);
                hit.m_damage = d;
                hit.m_skill = weapon.m_skillType;
                hit.m_toolTier = (short)weapon.m_toolTier;
                hit.m_backstabBonus = weapon.m_backstabBonus;
                amount = d.GetTotalDamage();
            }
            if ((ev.flags & HitPureFire) != 0)
            {
                // lava, fire, a fireball, a magma block: fire alone, so Valheim's resistances apply (Surtlings are immune)
                hit.m_damage = new HitData.DamageTypes { m_fire = ev.a * DamageScale.Value };
                hit.m_skill = Skills.SkillType.None;
                hit.m_hitType = HitData.HitType.Burning;
                hit.m_pushForce = 0f;
            }
            if (byMob) hit.m_skill = Skills.SkillType.None;
            target.Damage(hit);
            // a Minecraft mob's hit: the creature turns on that mob's stand-in (MobProxies)
            if (byMob && tool != 0) MobProxies.Provoke(target, (int)tool);
            Plugin.Log($"hit {target.m_name} for {amount:F1} (Minecraft {ev.a:F1}, weapon {ev.weapon}, flags {ev.flags})");
        }

        // Burning in Minecraft (lava, fire, a fire charge, Fire Aspect, Flame, a torch, ...) sets the
        // creature burning the Valheim way, for as long as Minecraft would: Valheim's flames on it and
        // its fire damage over time, Minecraft's 1 a second (times [Combat] DamageScale). Fire-proof
        // creatures (Surtlings, the Ashlands) shrug it off as they do Valheim's fire.
        public static float BurnSeconds;  // read by BurnTimePatch while this applies its hit

        static void Ignite(Character target, float seconds)
        {
            seconds = Mathf.Clamp(seconds, 1f, 10f);
            var hit = new HitData();
            hit.m_damage.m_fire = seconds * DamageScale.Value;
            hit.m_point = target.GetCenterPoint();
            hit.m_hitType = HitData.HitType.Burning;
            hit.m_blockable = hit.m_dodgeable = false;
            BurnSeconds = seconds;
            try { target.Damage(hit); }
            finally { BurnSeconds = 0f; }
        }

        // TNT and creepers: hurt Valheim creatures and break trees, rocks and building pieces like a
        // big blunt hit, falling off with distance. Not environment: creatures only, no crater.
        static void Explosion(Player player, Vector3d centreMc, float radius, bool environment)
        {
            var centre = Coords.ToValheim(centreMc);
            float reach = radius * 2f;
            var seen = new HashSet<IDestructible>();
            foreach (var col in Physics.OverlapSphere(centre, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (WorldRender.Root && col.transform.IsChildOf(WorldRender.Root)) continue;
                var d = col.GetComponentInParent<IDestructible>();
                if (d == null || !seen.Add(d)) continue;
                if (d is Character c && c == player) continue;  // Minecraft already hurt the player
                if (!environment && !(d is Character)) continue;
                var mb = d as MonoBehaviour;
                if (!mb) continue;
                float dist = Vector3.Distance(centre, col.ClosestPoint(centre));
                float falloff = Mathf.Clamp01(1f - dist / reach);
                if (falloff <= 0f) continue;
                var hit = new HitData();
                hit.SetAttacker(player);
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.m_point = col.ClosestPoint(centre);
                hit.m_dir = (hit.m_point - centre).sqrMagnitude > 1e-4f ? (hit.m_point - centre).normalized : Vector3.up;
                float amount = radius * 25f * falloff * DamageScale.Value / 4f * ExplosionDamage.Value;
                hit.m_damage.m_blunt = amount;
                hit.m_damage.m_chop = amount;
                hit.m_damage.m_pickaxe = amount;
                hit.m_pushForce = 60f * falloff;
                hit.m_toolTier = 4;
                d.Damage(hit);
            }
            if (environment) Craters.Add(centre, radius, ExplosionCraters.Value, DigPrefab());
            Plugin.Log($"explosion at {centre} radius {radius}{(environment ? "" : " (creatures only)")}");
        }

        // Minecraft's /time set or /time add: Valheim's clock goes forward to that hour (Minecraft's
        // hours, which follow Valheim's sky), plus any whole days skipped. Only the host can.
        static void SetTime(float hour, float days)
        {
            var net = ZNet.instance;
            var env = EnvMan.instance;
            if (!net || !env) return;
            if (!net.IsServer())
            {
                Plugin.Message("ValCraft: only the host can change the time");
                return;
            }
            double length = env.m_dayLengthSec;
            double now = net.GetTimeSeconds();
            double raw = now % length / length;
            double want = RawDayFraction(Mathf.Repeat(hour / 24f, 1f));
            double ahead = want - raw;
            if (ahead < 0) ahead += 1;
            double to = now + (ahead + Mathf.Max(0f, Mathf.Floor(days))) * length;
            net.SetNetTime(to);
            Plugin.Log($"time: Minecraft's /time moves Valheim to {hour:F1}h (+{days:F0} days, {to - now:F0} s ahead)");
        }

        // Drank a Valheim mead's Minecraft version: the Viking gets the mead's own effect.
        static void Consume(Player player, int prefabHash)
        {
            var prefab = ValheimItemByHash(prefabHash);
            var se = prefab ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_consumeStatusEffect : null;
            if (!se) { Plugin.Warn($"consume: no Valheim item with hash {prefabHash}"); return; }
            player.GetSEMan().AddStatusEffect(se, resetTime: true);
            Plugin.Log($"consume: {prefab.name} -> {se.name}");
        }

        static Dictionary<int, GameObject> _itemsByHash;

        // Minecraft names Valheim items by their prefab name's Java String.hashCode().
        public static GameObject ValheimItemByHash(int hash)
        {
            if (_itemsByHash == null && ObjectDB.instance && ObjectDB.instance.m_items.Count > 0)
            {
                _itemsByHash = new Dictionary<int, GameObject>();
                foreach (var go in ObjectDB.instance.m_items) if (go) _itemsByHash[JavaHash(go.name)] = go;
            }
            return _itemsByHash != null && _itemsByHash.TryGetValue(hash, out var found) ? found : null;
        }

        public static int JavaHash(string s)
        {
            int h = 0;
            unchecked { foreach (char c in s) h = 31 * h + c; }
            return h;
        }

        // The inverse of EnvMan.RescaleDayFraction: the sky's day fraction (0.25 = 6:00) to the clock's.
        static double RawDayFraction(float shown)
        {
            if (shown < 0.25f) return shown / 0.25f * 0.15f;
            if (shown <= 0.75f) return 0.15f + (shown - 0.25f) / 0.5f * 0.7f;
            return 0.85f + (shown - 0.75f) / 0.25f * 0.15f;
        }

        // ---- Valheim hits the player --------------------------------------------------------------

        // Called from the RPC_Damage patch on the owner (us). Returns true if Minecraft takes the hit.
        public static bool ForwardPlayerDamage(Player player, HitData hit)
        {
            if (!Puppet.Puppeting || !Shm.Valid) return false;
            // Only what hurts a player: chop, pickaxe and "non-player" damage are for trees, rocks and
            // buildings (bosses carry over a thousand of it to smash the scenery around them).
            float total = hit.GetTotalDamage() - hit.m_damage.m_chop - hit.m_damage.m_pickaxe - hit.m_damage.m_nonPlayer;
            if (total <= 0f) return true;  // nothing to take; also swallow Valheim's own reaction
            ushort kind;
            switch (hit.m_hitType)
            {
                case HitData.HitType.EnemyHit:
                case HitData.HitType.PlayerHit:
                    kind = hit.m_ranged ? (ushort)1 : (ushort)0;
                    if (hit.m_damage.m_fire + hit.m_damage.m_frost + hit.m_damage.m_lightning + hit.m_damage.m_spirit > total * 0.5f) kind = 2;
                    break;
                default:
                    kind = 3;  // burning, poison, smoke, drowning, ...
                    break;
            }
            var attacker = hit.GetAttacker();
            uint attackerId = attacker ? IdOf(attacker) : 0;
            int flags = hit.m_staggerMultiplier > 1.5f || hit.m_pushForce > 50f ? 2 : 0;  // power attack: extra shove
            Shm.PushInput(Proto.InHurt, kind, Mathf.RoundToInt(total * 100f), (int)attackerId, flags);
            return true;
        }
    }
}
