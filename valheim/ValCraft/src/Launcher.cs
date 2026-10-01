using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;

namespace ValCraft
{
    // Minecraft starts with Valheim (SkyCraft's Launcher.cpp, for Valheim). The mod ships
    // ValCraft-Minecraft.zip next to the plugin: a portable Prism Launcher with a ready "ValCraft"
    // instance (Minecraft 26.3, Fabric, Fabric API, the ValCraft Fabric mod). It's unpacked to
    // %LOCALAPPDATA%\ValCraft (again whenever the mod brings a different one; Prism's sign-in,
    // downloads and the Minecraft world are kept) and started; the instance starts Minecraft hidden,
    // it opens its world once Valheim's is up, and quits when Valheim closes.
    public static class Launcher
    {
        public enum State { Off, Running, Starting, SignIn, NoLauncher, Failed }
        public static volatile State Status = State.Off;

        static ConfigEntry<bool> _start;
        static ConfigEntry<string> _program, _arguments;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenMutexW(uint access, bool inherit, string name);

        [DllImport("kernel32")]
        static extern bool CloseHandle(IntPtr handle);

        public static void Init(ConfigFile config)
        {
            _start = config.Bind("Minecraft", "StartWithValheim", true,
                "Start Minecraft (hidden) when Valheim starts. Off: start it yourself, any way you like; it connects on its own.");
            _program = config.Bind("Minecraft", "Launcher", "",
                "Empty: the Minecraft that comes with ValCraft (portable Prism Launcher). Or a path to your own Prism/MultiMC exe or a .bat.");
            _arguments = config.Bind("Minecraft", "Arguments", "--launch ValCraft",
                "What the launcher needs to start the ValCraft instance.");
        }

        static string PluginDir => Path.GetDirectoryName(typeof(Launcher).Assembly.Location);
        static string Bundle => Path.Combine(PluginDir, "ValCraft-Minecraft.zip");
        static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValCraft");

        // A Minecraft with the ValCraft mod holds this mutex while it runs (ValLink.announceRunning).
        static bool MinecraftRunning()
        {
            var h = OpenMutexW(0x00100000 /* SYNCHRONIZE */, false, Link.Proto.MappingName + "_minecraft");
            if (h == IntPtr.Zero) return false;
            CloseHandle(h);
            return true;
        }

        public static void StartMinecraft()
        {
            if (!_start.Value)
            {
                Plugin.Log("Minecraft: not started with Valheim (StartWithValheim = false)");
                return;
            }
            if (MinecraftRunning())
            {
                Plugin.Log("Minecraft: already running");
                Status = State.Running;
                return;
            }
            string chosen = Environment.ExpandEnvironmentVariables(_program.Value ?? "").Trim().Trim('"');
            bool bundled = chosen.Length == 0 && File.Exists(Bundle);
            if (chosen.Length == 0 && !bundled)
            {
                Plugin.Warn("Minecraft: not started: no ValCraft-Minecraft.zip next to the plugin and no Launcher set in the config");
                Status = State.NoLauncher;
                return;
            }
            if (chosen.Length > 0 && !File.Exists(chosen))
            {
                Plugin.Warn($"Minecraft: not started: {chosen} doesn't exist (Launcher in the config)");
                Status = State.NoLauncher;
                return;
            }
            Status = State.Starting;
            string args = _arguments.Value;
            // Off the main thread: unpacking takes a moment the first time.
            new Thread(() =>
            {
                try
                {
                    string program = chosen;
                    if (bundled)
                    {
                        program = EnsureBundle();
                        if (program == null) { Status = State.Failed; return; }
                        if (!File.Exists(Path.Combine(Path.GetDirectoryName(program), "accounts.json"))) Status = State.SignIn;
                    }
                    Start(program, args);
                }
                catch (Exception e)
                {
                    Plugin.Error("Minecraft: couldn't start: " + e);
                    Status = State.Failed;
                }
            }) { IsBackground = true, Name = "ValCraft launcher" }.Start();
        }

        // Unpacks the bundle the first time, and again when this ValCraft brings a different one. The
        // instance and its ValCraft and Fabric API jars are replaced, so both halves always match.
        static string EnsureBundle()
        {
            string dir = InstallDir;
            string prism = Path.Combine(dir, "Prism", "prismlauncher.exe");
            var info = new FileInfo(Bundle);
            string stamp = info.Length + " " + info.LastWriteTimeUtc.Ticks;
            string stampFile = Path.Combine(dir, "bundle.stamp");
            if (File.Exists(prism) && File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp) return prism;

            Plugin.Log("Minecraft: unpacking ValCraft's Minecraft to " + dir);
            Directory.CreateDirectory(dir);
            string mods = Path.Combine(dir, "Prism", "instances", "ValCraft", ".minecraft", "mods");
            if (Directory.Exists(mods))
                foreach (var f in Directory.GetFiles(mods))
                {
                    string n = Path.GetFileName(f);
                    if (n.StartsWith("valcraft-") || n.StartsWith("fabric-api-")) File.Delete(f);
                }
            string copy = Path.Combine(dir, "bundle.zip");
            File.Copy(Bundle, copy, true);
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            var psi = new ProcessStartInfo(tar, $"-xf \"{copy}\" -C \"{dir}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(5 * 60 * 1000) || p.ExitCode != 0)
                {
                    Plugin.Warn("Minecraft: unpacking failed (tar exit code " + (p.HasExited ? p.ExitCode.ToString() : "timeout") + ")");
                    return null;
                }
            }
            File.Delete(copy);
            if (!File.Exists(prism))
            {
                Plugin.Warn("Minecraft: unpacked, but " + prism + " isn't there");
                return null;
            }
            // Prism's settings: only the first time (after that they're the player's).
            string cfg = Path.Combine(dir, "Prism", "prismlauncher.cfg");
            string defaults = Path.Combine(dir, "defaults", "prismlauncher.cfg");
            if (!File.Exists(cfg) && File.Exists(defaults)) File.Copy(defaults, cfg);
            File.WriteAllText(stampFile, stamp);
            return prism;
        }

        static void Start(string program, string args)
        {
            string ext = Path.GetExtension(program).ToLowerInvariant();
            bool script = ext == ".bat" || ext == ".cmd";
            var psi = script
                ? new ProcessStartInfo("cmd.exe", $"/c \"\"{program}\" {args}\"") { UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo(program, args) { UseShellExecute = true };
            psi.WorkingDirectory = Path.GetDirectoryName(program);
            Process.Start(psi);
            Plugin.Log($"Minecraft: started {program} {args}");
        }

        // Main thread, in game: tells the player what Minecraft is up to until it connects.
        static float _waited, _nextNote = 3f;
        static bool _told, _gaveUp;

        public static void Report(bool connected, bool inGame, float dt)
        {
            if (connected)
            {
                if (_told) Plugin.Message("ValCraft: Minecraft is ready");
                _waited = 0f; _nextNote = 3f; _told = _gaveUp = false;
                return;
            }
            if (!inGame || _gaveUp) return;
            _waited += dt;
            if (_waited < _nextNote) return;
            var status = Status;
            if (_waited > (status == State.SignIn ? 600f : 180f))
            {
                Plugin.Message("ValCraft: Minecraft still hasn't connected. See BepInEx/LogOutput.log.");
                _gaveUp = true;
                return;
            }
            string note = null;
            switch (status)
            {
                case State.SignIn: note = "ValCraft: setting up Minecraft. Alt-Tab to the Prism Launcher window and sign in with your Microsoft account."; break;
                case State.Starting:
                case State.Running: note = _told ? null : "ValCraft: starting Minecraft..."; break;
                case State.NoLauncher: note = "ValCraft: Minecraft isn't set up (no ValCraft-Minecraft.zip). See the mod's README."; _gaveUp = true; break;
                case State.Failed: note = "ValCraft: couldn't start Minecraft. See BepInEx/LogOutput.log."; _gaveUp = true; break;
                default: note = _told ? null : "ValCraft: waiting for Minecraft (start it with the ValCraft Fabric mod)"; break;
            }
            if (note != null) { Plugin.Message(note); _told = true; }
            _nextNote = _waited + 120f;
        }
    }
}
