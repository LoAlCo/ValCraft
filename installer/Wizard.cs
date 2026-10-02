using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ValCraftInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Wizard());
        }
    }

    // Step by step: Valheim, Minecraft (unpack, sign in, first download), the Valheim mod in a mod
    // manager of the player's choice, done. Each step can be come back to; checks re-run on a timer.
    sealed class Wizard : Form
    {
        sealed class Step
        {
            public string Title;
            public Action<FlowLayoutPanel> Build;
            public Func<bool> CanNext = () => true;
            public Action Tick = () => { };
        }

        readonly Label _title = new Label { Dock = DockStyle.Top, Height = 48, Font = new Font("Segoe UI", 15f, FontStyle.Bold), Padding = new Padding(16, 12, 0, 0) };
        readonly FlowLayoutPanel _body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20, 4, 20, 8) };
        readonly Button _back = new Button { Text = "< Back", Width = 90, Height = 30 };
        readonly Button _next = new Button { Text = "Next >", Width = 90, Height = 30 };
        readonly Label _stepCount = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Top = 14, Left = 16 };
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly Step[] _steps;
        int _index;

        // State shared by the steps.
        string _valheim;
        volatile string _work;        // a background job's status line; null when idle
        volatile string _error;
        bool _unpacked, _downloadStarted, _minecraftWorked, _closing;
        int _seenRunningTicks;
        Setup.Manager _manager = Setup.Managers[0];

        public Wizard()
        {
            Text = $"ValCraft installer {Setup.Version}";
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(600, 440);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.75f);
            BackColor = SystemColors.Window;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }  // the .exe's creeper, in the title bar too

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = SystemColors.Control };
            _next.Anchor = _back.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            footer.Controls.Add(_stepCount);
            footer.Controls.Add(_back);
            footer.Controls.Add(_next);
            footer.Resize += (s, e) => { _next.Left = footer.Width - _next.Width - 16; _back.Left = _next.Left - _back.Width - 8; _next.Top = _back.Top = 12; };
            Controls.Add(_body);
            Controls.Add(footer);
            Controls.Add(_title);
            _back.Click += (s, e) => Show(_index - 1);
            _next.Click += (s, e) => { if (_index == _steps.Length - 1) Close(); else Show(_index + 1); };
            _timer.Tick += (s, e) => { _steps[_index].Tick(); UpdateButtons(_index); };

            _steps = new[] { Welcome(), ValheimStep(), MinecraftStep(), FirstDownloadStep(), ModManagerStep(), DoneStep() };
            Load += (s, e) => { Show(0); _timer.Start(); };
        }

        void Show(int index)
        {
            if (index < 0 || index >= _steps.Length) return;
            _index = index;
            _body.SuspendLayout();
            _body.Controls.Clear();
            _title.Text = _steps[index].Title;
            _steps[index].Build(_body);
            _body.ResumeLayout();
            _steps[index].Tick();
            UpdateButtons(index);
        }

        void UpdateButtons(int index)
        {
            _stepCount.Text = $"Step {index + 1} of {_steps.Length}";
            _back.Enabled = index > 0 && _work == null;
            _next.Enabled = _work == null && _steps[index].CanNext();
            _next.Text = index == _steps.Length - 1 ? "Finish" : "Next >";
        }

        // ---- building blocks ---------------------------------------------------------------------

        Label Say(FlowLayoutPanel p, string text, bool bold = false, Color? colour = null)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(Math.Max(300, p.ClientSize.Width - 50), 0), Margin = new Padding(0, 4, 0, 6) };
            if (bold) l.Font = new Font(Font, FontStyle.Bold);
            if (colour.HasValue) l.ForeColor = colour.Value;
            p.Controls.Add(l);
            return l;
        }

        Button Btn(FlowLayoutPanel p, string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 4, 8, 4) };
            b.Click += (s, e) => { try { click(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "ValCraft installer", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
            p.Controls.Add(b);
            return b;
        }

        static readonly Color Good = Color.FromArgb(0, 120, 40), Bad = Color.FromArgb(170, 40, 30), Muted = SystemColors.GrayText;

        /// <summary>Runs a job off the window's thread; its status lines show in <paramref name="status"/>.</summary>
        void Run(Action<Action<string>> job, Action done = null)
        {
            _error = null;
            _work = "Working…";
            new Thread(() =>
            {
                try { job(s => _work = s); }
                catch (Exception e) { _error = e.Message; }
                finally
                {
                    _work = null;
                    BeginInvoke((Action)(() => { done?.Invoke(); Show(_index); }));
                }
            }) { IsBackground = true }.Start();
            Show(_index);
        }

        void WorkLine(FlowLayoutPanel p)
        {
            if (_work != null) Say(p, _work, colour: Muted).Tag = "work";
            if (_error != null) Say(p, "Something went wrong: " + _error, colour: Bad);
        }

        // ---- the steps ---------------------------------------------------------------------------

        Step Welcome() => new Step
        {
            Title = "Welcome to ValCraft",
            Build = p =>
            {
                Say(p, "ValCraft lets you play Valheim as a Minecraft player: Minecraft runs hidden next to Valheim and the two games talk to each other.");
                Say(p, "This installer walks you through everything, one step at a time:");
                Say(p, "  1. Check that Valheim is installed.\n  2. Set up Minecraft (ValCraft brings its own copy) and sign in.\n  3. Let Minecraft download its game files once.\n  4. Put the ValCraft mod into your mod manager: Gale, r2modman or the Thunderstore Mod Manager.");
                Say(p, "You need Valheim on Steam and a Microsoft account that owns Minecraft: Java Edition. Budget about 3 GB of free memory and 2 GB of disk.");
                if (!Setup.HasEmbeddedMod) Say(p, "This copy of the installer has no mod inside it. Download the installer again from the ValCraft releases page.", true, Bad);
            },
            CanNext = () => Setup.HasEmbeddedMod,
        };

        Step ValheimStep() => new Step
        {
            Title = "Valheim",
            Build = p =>
            {
                _valheim = Setup.FindValheim();
                if (_valheim != null)
                {
                    Say(p, "Found Valheim:", true, Good);
                    Say(p, _valheim);
                    Say(p, "ValCraft doesn't change anything in this folder; your mod manager takes care of that when you start the game.");
                }
                else
                {
                    Say(p, "Couldn't find Valheim in your Steam libraries.", true, Bad);
                    Say(p, "Install Valheim from Steam, then press Check again. If it is installed somewhere unusual you can continue anyway.");
                    Btn(p, "Open Valheim on Steam", () => Setup.OpenUrl("steam://store/892970"));
                    Btn(p, "Check again", () => Show(_index));
                }
            },
        };

        Step MinecraftStep() => new Step
        {
            Title = "Minecraft",
            Build = p =>
            {
                Say(p, "ValCraft comes with its own Minecraft: a ready-made Prism Launcher with Minecraft and the ValCraft Fabric mod set up. It goes into " + Setup.InstallDir + ".");
                if (_work == null)
                {
                    // Always offered: the first time it sets Minecraft up, later it updates or repairs it.
                    Btn(p, Setup.MinecraftUnpacked ? "Set up Minecraft again (updates or repairs it)" : "Set up Minecraft", () => Run(status =>
                    {
                        status("Copying the mod out of the installer…");
                        Setup.WriteModZip();
                        Setup.UnpackMinecraft(status);
                    }, () => _unpacked = true));
                    if (_unpacked) Say(p, "Minecraft is set up.", true, Good);
                    else if (Setup.MinecraftUnpacked) Say(p, "(Minecraft is already set up here; you can keep it and continue once you're signed in.)", colour: Muted);
                }
                WorkLine(p);
                if (!Setup.MinecraftUnpacked || _work != null) return;

                Say(p, "Sign in", true);
                string who = Setup.SignedInAs();
                if (who != null)
                {
                    Say(p, "Signed in as " + who + ".", true, Good);
                    return;
                }
                Say(p, "1. Press Open Prism Launcher. The first time, click through its quick setup (the defaults are fine).\n2. Click Accounts (top right), then Add Microsoft, and sign in with the account that owns Minecraft.\n3. Come back here; this page notices by itself.");
                Btn(p, "Open Prism Launcher", Setup.OpenPrism);
                Say(p, "Waiting for the sign-in…", colour: Muted);
                Say(p, "If Prism says the sign-in failed, Microsoft's servers may be down: try again later.", colour: Muted);
            },
            CanNext = () => Setup.MinecraftUnpacked && Setup.SignedInAs() != null,
            Tick = () =>
            {
                // Re-render once the sign-in shows up.
                if (_index == 2 && _work == null && Setup.MinecraftUnpacked && Setup.SignedInAs() != null && !_body.Controls.OfType<Label>().Any(l => l.Text.StartsWith("Signed in"))) Show(2);
            },
        };

        Step FirstDownloadStep() => new Step
        {
            Title = "First start of Minecraft",
            Build = p =>
            {
                Say(p, "Minecraft downloads its game files, Fabric and Java the first time it starts (a few minutes). Doing it now means Valheim doesn't have to wait later, and it shows that Minecraft works.");
                if (_minecraftWorked)
                {
                    Say(p, "Minecraft started and closed again: it works.", true, Good);
                    return;
                }
                if (!_downloadStarted)
                {
                    if (Setup.MinecraftRunning())
                    {
                        // Already running (perhaps with Valheim): that's proof enough, and it isn't ours to close.
                        Say(p, "Minecraft with ValCraft is already running: it works.", true, Good);
                        return;
                    }
                    Btn(p, "Start Minecraft once", () => { Setup.StartFirstDownload(); _downloadStarted = true; _seenRunningTicks = 0; Show(_index); });
                    Say(p, "You can also skip this; Minecraft then downloads the first time you start Valheim.", colour: Muted);
                    return;
                }
                Say(p, _closing ? "Minecraft is up. Closing it again…" : "Downloading and starting Minecraft… Prism shows the progress. Minecraft starts hidden; this page closes it again when it's ready.", colour: Muted);
                Btn(p, "Start it again", () => { Setup.StartFirstDownload(); _seenRunningTicks = 0; });
            },
            Tick = () =>
            {
                if (_index != 3 || !_downloadStarted || _minecraftWorked) return;
                if (Setup.MinecraftRunning())
                {
                    // Give it a moment to finish loading before closing it the normal way.
                    if (++_seenRunningTicks == 5) { _closing = true; Setup.CloseMinecraft(); Show(3); }
                }
                else if (_closing)
                {
                    _closing = false;
                    _minecraftWorked = true;
                    Show(3);
                }
            },
        };

        Step ModManagerStep() => new Step
        {
            Title = "The Valheim mod",
            Build = p =>
            {
                Say(p, "Pick your mod manager. It keeps ValCraft (and BepInEx, which Valheim mods need) in its own profile, so your normal Valheim stays untouched.");
                foreach (var m in Setup.Managers)
                {
                    var r = new RadioButton { Text = m.Name + (m.Installed ? "   (installed)" : ""), AutoSize = true, Checked = m == _manager, Margin = new Padding(0, 2, 0, 2) };
                    r.CheckedChanged += (s, e) => { if (r.Checked) { _manager = m; Show(_index); } };
                    p.Controls.Add(r);
                }
                Say(p, "");
                var m0 = _manager;
                if (!m0.Installed)
                {
                    Say(p, m0.Name + " isn't installed yet.", true);
                    if (m0.GitHubRepo != null && _work == null) Btn(p, "Download and install " + m0.Name, () => Run(status => Setup.InstallManager(m0, status)));
                    Btn(p, "Open " + m0.Name + "'s website", () => Setup.OpenUrl(m0.Site));
                    WorkLine(p);
                    return;
                }
                Say(p, "In " + m0.Name + ":", true);
                for (int i = 0; i < m0.Steps.Length; i++) Say(p, $"{i + 1}. {m0.Steps[i]}");
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 6) };
                p.Controls.Add(row);
                if (m0.Exe != null) Btn(row, "Open " + m0.Name, () => Setup.OpenManager(m0));
                Btn(row, "Show the mod zip", () =>
                {
                    if (!System.IO.File.Exists(Setup.ModZip)) Setup.WriteModZip();
                    Setup.ShowInExplorer(Setup.ModZip);
                });
                Btn(row, "Copy the zip's path", () =>
                {
                    if (!System.IO.File.Exists(Setup.ModZip)) Setup.WriteModZip();
                    Clipboard.SetText(Setup.ModZip);
                });
                var found = Setup.ProfilesWithValCraft(m0);
                if (found.Count > 0) Say(p, $"ValCraft is installed in your {m0.Name} profile \"{string.Join("\", \"", found)}\".", true, Good);
                else Say(p, "Waiting for ValCraft to show up in a " + m0.Name + " profile… (this page checks by itself)", colour: Muted);
            },
            CanNext = () => Setup.ProfilesWithValCraft(_manager).Count > 0,
            Tick = () =>
            {
                if (_index != 4 || _work != null) return;
                bool done = Setup.ProfilesWithValCraft(_manager).Count > 0;
                bool shown = _body.Controls.OfType<Label>().Any(l => l.Text.StartsWith("ValCraft is installed"));
                if (done != shown) Show(4);
            },
        };

        Step DoneStep() => new Step
        {
            Title = "All set",
            Build = p =>
            {
                Say(p, "ValCraft is ready.", true, Good);
                Say(p, $"To play: open {_manager.Name}, select your ValCraft profile and start the game modded. Minecraft starts hidden with Valheim and closes with it.");
                Say(p, "Load a Valheim world. When the top left says \"ValCraft: Minecraft is ready\", you're a Minecraft player.");
                Say(p, "Keys", true);
                Say(p, "Mouse, WASD, Space, Shift, Ctrl, 1-9, E, Q, T, F5: Minecraft, as usual\nG: use Valheim things (doors, chests, portals, beds)\nEsc / M: Valheim's menu / map\nO: Minecraft's options\nF7: Valheim controls on/off\nF8: block terrain on/off");
                Btn(p, "Open the ValCraft page", () => Setup.OpenUrl("https://github.com/LoAlCo/ValCraft"));
            },
        };
    }
}
