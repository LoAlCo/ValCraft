using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // Minecraft's bosses (the Wither, the Ender Dragon, the Warden, Elder Guardians, raids) get
    // Valheim's own boss health bar: a copy of EnemyHud's boss bar each, named as Minecraft names
    // them, filled to their health (Minecraft's BossBars.java sends them in the boss table). Stacked
    // under any Valheim boss's bar. Minecraft's own boss bars are hidden meanwhile.
    public static class BossBars
    {
        class Bar
        {
            public GameObject gui;
            public RectTransform rect;
            public Vector2 home;
            public GuiBar fast, slow;
            public TextMeshProUGUI name;
        }

        static readonly Shm.Boss[] _read = new Shm.Boss[Proto.MaxBosses];
        static readonly List<Shm.Boss> _bosses = new List<Shm.Boss>();
        static readonly Dictionary<uint, Bar> _bars = new Dictionary<uint, Bar>();
        static readonly HashSet<uint> _seen = new HashSet<uint>();
        static EnemyHud _hudFor;

        // Main thread, every frame.
        public static void Frame()
        {
            var hud = EnemyHud.instance;
            if (hud != _hudFor) { _bars.Clear(); _hudFor = hud; }  // a new scene: the old bars went with the old hud
            bool active = hud && Puppet.Puppeting && Shm.Valid && Player.m_localPlayer;
            if (active)
            {
                int n = Shm.ReadBosses(_read);
                if (n >= 0)
                {
                    _bosses.Clear();
                    for (int i = 0; i < n; i++) _bosses.Add(_read[i]);
                }
            }
            else _bosses.Clear();

            _seen.Clear();
            // Under the bar of a Valheim boss being fought, if there is one.
            int slot = hud && hud.ShowingBossHud() ? 1 : 0;
            foreach (var boss in _bosses)
            {
                _seen.Add(boss.id);
                if (!_bars.TryGetValue(boss.id, out var bar) || !bar.gui)
                {
                    bar = Make(hud);
                    if (bar == null) continue;
                    _bars[boss.id] = bar;
                    Plugin.Log($"boss bar: {boss.name}");
                }
                bar.name.text = boss.name;
                bar.fast.SetValue(boss.progress);
                bar.slow.SetValue(boss.progress);
                float step = Mathf.Max(bar.rect.rect.height, 40f) + 8f;
                bar.rect.anchoredPosition = bar.home - new Vector2(0f, step * slot);
                slot++;
            }
            if (_bars.Count == _seen.Count) return;
            var gone = new List<uint>();
            foreach (var kv in _bars)
                if (!_seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (_bars[id].gui) Object.Destroy(_bars[id].gui);
                _bars.Remove(id);
            }
        }

        static Bar Make(EnemyHud hud)
        {
            if (!hud.m_baseHudBoss || !hud.m_hudRoot) return null;
            var gui = Object.Instantiate(hud.m_baseHudBoss, hud.m_hudRoot.transform);
            gui.name = "ValCraft boss bar";
            gui.SetActive(true);
            var bar = new Bar { gui = gui, rect = gui.transform as RectTransform };
            bar.home = bar.rect ? bar.rect.anchoredPosition : Vector2.zero;
            bar.fast = gui.transform.Find("Health/health_fast")?.GetComponent<GuiBar>();
            bar.slow = gui.transform.Find("Health/health_slow")?.GetComponent<GuiBar>();
            bar.name = gui.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            var friendly = gui.transform.Find("Health/health_fast_friendly");
            if (friendly) friendly.gameObject.SetActive(false);
            foreach (var extra in new[] { "level_2", "level_3", "Alerted", "Aware" })
            {
                var t = gui.transform.Find(extra);
                if (t) t.gameObject.SetActive(false);
            }
            if (!bar.rect || !bar.fast || !bar.slow || !bar.name)
            {
                Plugin.Warn("boss bar: Valheim's boss bar isn't laid out as expected");
                Object.Destroy(gui);
                return null;
            }
            return bar;
        }
    }
}
