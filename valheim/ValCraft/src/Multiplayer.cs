using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Multiplayer: everyone with ValCraft in the same Valheim world plays in one Minecraft world, so
    // they see each other's Minecraft avatars, blocks and mobs. The ValCraft players agree on a host
    // over Valheim's own network: a newcomer asks; if someone already hosts, it joins their link
    // (ValState.mpMode = MpJoin); if nobody answers, it hosts (MpHost: Minecraft opens its world to
    // friends and e4mc gives it a link, McState.mpLink, which this hands out). The longest-standing
    // host wins if two start at once. While two players share a Minecraft world, each sees the
    // other's Minecraft avatar, so the other's Viking is hidden (players without ValCraft, or not in
    // that Minecraft world, still see Vikings as usual).
    public static unsafe class Multiplayer
    {
        public static ConfigEntry<bool> SharedWorld;

        const string RpcHost = "ValCraft_McHost";  // ZPackage: link, since (network seconds)
        const string RpcAsk = "ValCraft_McAsk";
        const string ZdoWorld = "valcraft_mc";      // on a player's ZDO: the Minecraft world they're in ("": their own)
        const float AskSeconds = 6f, AnnounceSeconds = 10f, StaleSeconds = 35f;
        // Testing on one PC (tools/fake_valheim_guest.py): host even with nobody else in the Valheim world.
        static readonly bool Alone = System.Environment.GetEnvironmentVariable("VALCRAFT_MP_ALONE") == "1";

        enum Role { Idle, Asking, Host, Guest }

        struct Announce { public string link; public double since; public float at; }

        static Role _role;
        static float _timer, _announceTimer, _hideTimer;
        static double _hostSince;
        static string _joinLink = "";
        static uint _mode = Proto.MpOwn, _seq;
        static string _sentLink = "";
        static bool _wasInFriendWorld;
        static ZRoutedRpc _rpcFor;
        static string _myWorld = "";
        static string _zdoWorld;
        static readonly Dictionary<long, Announce> _hosts = new Dictionary<long, Announce>();
        static readonly Dictionary<Player, List<Renderer>> _hidden = new Dictionary<Player, List<Renderer>>();

        public static void Init(ConfigFile config)
        {
            SharedWorld = config.Bind("Multiplayer", "SharedWorld", true,
                "Everyone with ValCraft in this Valheim world plays in one Minecraft world, the first one's (you see each other's Minecraft avatars, blocks and mobs). " +
                "It happens on its own: the first ValCraft player in opens their Minecraft world to the others (through e4mc), and the rest join it. Off: your own Minecraft world.");
            SharedWorld.SettingChanged += (s, e) => Reset();
        }

        static void Reset()
        {
            _role = Role.Idle;
            _hosts.Clear();
            SetMode(Proto.MpOwn, "");
        }

        static void SetMode(uint mode, string link)
        {
            link = link ?? "";
            if (mode == _mode && link == _joinLink) return;
            _mode = mode;
            _joinLink = link;
            _seq++;
            Plugin.Log($"multiplayer: {(mode == Proto.MpHost ? "hosting the shared Minecraft world" : mode == Proto.MpJoin ? "joining the shared Minecraft world at " + link : "own Minecraft world")}");
        }

        public static void Write(ref ValState st)
        {
            st.mpMode = _mode;
            st.mpSeq = _seq;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_joinLink);
            int n = Mathf.Min(bytes.Length, 63);
            fixed (byte* dst = st.mpLink)
            {
                for (int i = 0; i < n; i++) dst[i] = bytes[i];
                dst[n] = 0;
            }
        }

        static string McLink()
        {
            fixed (byte* src = Puppet.Mc.mpLink)
            {
                int n = 0;
                while (n < 44 && src[n] != 0) n++;
                return n == 0 ? "" : new string((sbyte*)src, 0, n, System.Text.Encoding.UTF8);
            }
        }

        // Main thread, every frame.
        public static void Frame(float dt)
        {
            var player = Player.m_localPlayer;
            var net = ZNet.instance;
            bool inWorld = player && net && ZRoutedRpc.instance != null && Puppet.McConnected;
            RegisterRpcs();

            uint mcState = Puppet.McConnected ? Puppet.Mc.mpState : 0u;
            string mcLink = McLink();
            bool published = (mcState & Proto.MpPublished) != 0 && mcLink.Length > 0;
            bool inFriendWorld = (mcState & Proto.MpInFriendWorld) != 0;
            _myWorld = published || inFriendWorld ? mcLink : "";
            MarkPlayer(player);
            HideSharers(player, dt);

            if (!inWorld || !SharedWorld.Value)
            {
                if (_role != Role.Idle || _mode != Proto.MpOwn) Reset();
                return;
            }

            float now = Time.unscaledTime;
            foreach (var key in new List<long>(_hosts.Keys))
                if (now - _hosts[key].at > StaleSeconds) _hosts.Remove(key);
            long me = ZNet.GetUID();
            bool haveHost = BestHost(me, out long hostId, out Announce host);

            switch (_role)
            {
                case Role.Idle:
                    if (!Puppet.McInWorld) break;  // once Minecraft has a world to open to friends
                    // Alone in the world (single player, or nobody else on yet): nothing to share. A
                    // host already announcing still gets joined (a ValCraft friend is in).
                    if (!haveHost && net.GetPlayerList().Count < 2 && !Alone) break;
                    _role = Role.Asking;
                    _timer = AskSeconds;
                    Invoke(RpcAsk, new ZPackage());
                    Plugin.Log("multiplayer: asking who hosts the shared Minecraft world");
                    break;
                case Role.Asking:
                    if (haveHost) { BecomeGuest(hostId, host); break; }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _role = Role.Host;
                        _hostSince = net.GetTimeSeconds();
                        _announceTimer = 0f;
                        SetMode(Proto.MpHost, "");
                    }
                    break;
                case Role.Host:
                    // Two started at once: the earlier one keeps hosting.
                    if (haveHost && (host.since < _hostSince - 0.01 || (System.Math.Abs(host.since - _hostSince) <= 0.01 && hostId < me)))
                    {
                        Plugin.Log("multiplayer: someone else was hosting first; joining theirs");
                        BecomeGuest(hostId, host);
                        break;
                    }
                    _announceTimer -= dt;
                    if (published && (_announceTimer <= 0f || mcLink != _sentLink))
                    {
                        _announceTimer = AnnounceSeconds;
                        AnnounceHost(ZRoutedRpc.Everybody, mcLink);
                    }
                    break;
                case Role.Guest:
                    if (!haveHost)
                    {
                        Plugin.Log("multiplayer: the shared world's host is gone");
                        _role = Role.Idle;
                        SetMode(Proto.MpOwn, "");
                        break;
                    }
                    if (host.link != _joinLink) SetMode(Proto.MpJoin, host.link);
                    // We were in it and Minecraft dropped out (the host closed it): don't wait for the announcements to go stale.
                    if (_wasInFriendWorld && !inFriendWorld && Puppet.McInWorld)
                    {
                        Plugin.Log("multiplayer: dropped out of the shared world; asking again");
                        _hosts.Remove(hostId);
                        _role = Role.Idle;
                        SetMode(Proto.MpOwn, "");
                    }
                    break;
            }
            _wasInFriendWorld = inFriendWorld;
        }

        static void BecomeGuest(long hostId, Announce host)
        {
            _role = Role.Guest;
            _wasInFriendWorld = false;
            SetMode(Proto.MpJoin, host.link);
        }

        static bool BestHost(long me, out long id, out Announce best)
        {
            id = 0;
            best = default;
            bool any = false;
            foreach (var kv in _hosts)
            {
                if (kv.Key == me || string.IsNullOrEmpty(kv.Value.link)) continue;
                if (!any || kv.Value.since < best.since || (kv.Value.since == best.since && kv.Key < id))
                {
                    any = true;
                    id = kv.Key;
                    best = kv.Value;
                }
            }
            return any;
        }

        static void RegisterRpcs()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || rpc == _rpcFor) return;
            _rpcFor = rpc;
            _hosts.Clear();
            _role = Role.Idle;
            rpc.Register<ZPackage>(RpcHost, OnHost);
            rpc.Register<ZPackage>(RpcAsk, OnAsk);
        }

        static void Invoke(string method, ZPackage pkg)
        {
            try { ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, method, pkg); }
            catch (System.Exception e) { Plugin.Warn("multiplayer: " + method + ": " + e.Message); }
        }

        static void AnnounceHost(long to, string link)
        {
            var pkg = new ZPackage();
            pkg.Write(link);
            pkg.Write(_hostSince);
            _sentLink = link;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(to, RpcHost, pkg); }
            catch (System.Exception e) { Plugin.Warn("multiplayer: announce: " + e.Message); }
        }

        static void OnHost(long sender, ZPackage pkg)
        {
            if (sender == ZNet.GetUID()) return;
            string link = pkg.ReadString();
            double since = pkg.ReadDouble();
            bool known = _hosts.ContainsKey(sender);
            _hosts[sender] = new Announce { link = link, since = since, at = Time.unscaledTime };
            if (!known) Plugin.Log($"multiplayer: {sender} hosts a shared Minecraft world at {link}");
        }

        static void OnAsk(long sender, ZPackage pkg)
        {
            if (sender == ZNet.GetUID()) return;
            if (_role == Role.Host && _sentLink.Length > 0 && SharedWorld.Value) AnnounceHost(sender, _sentLink);
        }

        // Our player's ZDO says which Minecraft world we're in, for the other ValCraft players.
        static void MarkPlayer(Player player)
        {
            if (!player) { _zdoWorld = null; return; }
            if (_zdoWorld == _myWorld) return;
            var view = player.GetComponent<ZNetView>();
            var zdo = view ? view.GetZDO() : null;
            if (zdo == null || !view.IsOwner()) return;
            zdo.Set(ZdoWorld, _myWorld);
            _zdoWorld = _myWorld;
        }

        /** True if this is another player in our Minecraft world (their Minecraft avatar stands in for the Viking). */
        public static bool SharesWorld(Character c)
        {
            if (_myWorld.Length == 0 || !(c is Player p) || p == Player.m_localPlayer) return false;
            var view = p.GetComponent<ZNetView>();
            var zdo = view ? view.GetZDO() : null;
            return zdo != null && string.Equals(zdo.GetString(ZdoWorld, ""), _myWorld, System.StringComparison.OrdinalIgnoreCase);
        }

        static void HideSharers(Player local, float dt)
        {
            _hideTimer -= dt;
            if (_hideTimer > 0f) return;
            _hideTimer = 0.5f;  // equipment changes add renderers: re-scan now and then
            var gone = new List<Player>();
            foreach (var kv in _hidden)
                if (!kv.Key || !SharesWorld(kv.Key)) gone.Add(kv.Key);
            foreach (var p in gone)
            {
                foreach (var r in _hidden[p]) if (r) r.forceRenderingOff = false;
                _hidden.Remove(p);
            }
            if (!local) return;
            foreach (var p in Player.GetAllPlayers())
            {
                if (!p || p == local || !SharesWorld(p)) continue;
                var visual = p.GetVisual();
                if (!visual) continue;
                if (!_hidden.TryGetValue(p, out var list)) _hidden[p] = list = new List<Renderer>();
                foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; list.Add(r); }
            }
        }
    }
}
