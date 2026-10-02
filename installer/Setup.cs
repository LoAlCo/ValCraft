using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ValCraftInstaller
{
    // Everything the wizard does, without the window. Mirrors the plugin's own Launcher (the same
    // %LOCALAPPDATA%\ValCraft folder and unpacking), so the plugin finds Minecraft ready.
    static class Setup
    {
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValCraft");
        public static string PrismExe => Path.Combine(InstallDir, "Prism", "prismlauncher.exe");
        static string AccountsFile => Path.Combine(InstallDir, "Prism", "accounts.json");
        const string MinecraftMutex = @"Local\ValCraft_v1_minecraft";
        const string ValheimAppId = "892970";

        public static string Version => typeof(Setup).Assembly.GetName().Version.ToString(3);

        // ---- the mod zip carried inside this .exe -------------------------------------------------

        /// <summary>The release zip (plugin + ValCraft-Minecraft.zip), written next to the installer's data for importing.</summary>
        public static string ModZip => Path.Combine(InstallDir, "installer", $"LoAlCo-ValCraft-{Version}.zip");

        public static bool HasEmbeddedMod => typeof(Setup).Assembly.GetManifestResourceStream("ValCraft.zip") != null;

        public static string WriteModZip()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModZip));
            using (var s = typeof(Setup).Assembly.GetManifestResourceStream("ValCraft.zip"))
            {
                if (s == null) throw new InvalidOperationException("This installer was built without the mod inside it.");
                using (var f = File.Create(ModZip)) s.CopyTo(f);
            }
            return ModZip;
        }

        // ---- Valheim ---------------------------------------------------------------------------------

        /// <summary>Valheim's folder from Steam's libraries, or null.</summary>
        public static string FindValheim()
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(steam)) return null;
            steam = steam.Replace('/', '\\');
            var libraries = new List<string> { steam };
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string manifest = Path.Combine(lib, "steamapps", $"appmanifest_{ValheimAppId}.acf");
                string dir = Path.Combine(lib, "steamapps", "common", "Valheim");
                if (File.Exists(manifest) && File.Exists(Path.Combine(dir, "valheim.exe"))) return dir;
            }
            return null;
        }

        // ---- Minecraft (the bundled Prism Launcher) ------------------------------------------------

        public static bool MinecraftUnpacked => File.Exists(PrismExe);

        /// <summary>Unpacks ValCraft-Minecraft.zip from the mod zip into %LOCALAPPDATA%\ValCraft, like the plugin does.</summary>
        public static void UnpackMinecraft(Action<string> status)
        {
            Directory.CreateDirectory(InstallDir);
            string bundle = Path.Combine(InstallDir, "bundle.zip");
            status("Unpacking Minecraft…");
            using (var zip = ZipFile.OpenRead(ModZip))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("ValCraft-Minecraft.zip", StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new InvalidOperationException("ValCraft-Minecraft.zip isn't in the mod zip.");
                entry.ExtractToFile(bundle, true);
            }
            // The instance's ValCraft and Fabric API jars are replaced, so both halves always match.
            string mods = Path.Combine(InstallDir, "Prism", "instances", "ValCraft", ".minecraft", "mods");
            if (Directory.Exists(mods))
                foreach (var f in Directory.GetFiles(mods))
                {
                    string n = Path.GetFileName(f);
                    if (n.StartsWith("valcraft-") || n.StartsWith("fabric-api-")) File.Delete(f);
                }
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            var psi = new ProcessStartInfo(tar, $"-xf \"{bundle}\" -C \"{InstallDir}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = InstallDir };
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(5 * 60 * 1000) || p.ExitCode != 0) throw new InvalidOperationException("Unpacking failed (tar).");
            }
            File.Delete(bundle);
            if (!File.Exists(PrismExe)) throw new InvalidOperationException("Unpacked, but Prism Launcher isn't there.");
            // Prism's settings: only the first time (after that they're the player's).
            string cfg = Path.Combine(InstallDir, "Prism", "prismlauncher.cfg");
            string defaults = Path.Combine(InstallDir, "defaults", "prismlauncher.cfg");
            if (!File.Exists(cfg) && File.Exists(defaults)) File.Copy(defaults, cfg);
        }

        /// <summary>The signed-in Minecraft name, or null when no Microsoft account is added yet.</summary>
        public static string SignedInAs()
        {
            if (!File.Exists(AccountsFile)) return null;
            string json;
            try { json = File.ReadAllText(AccountsFile); } catch (IOException) { return null; }
            if (!Regex.IsMatch(json, "\"type\"\\s*:\\s*\"MSA\"")) return null;
            // The Minecraft name is the profile's "name" (it comes after its capes, which have none).
            int at = json.IndexOf("\"profile\"", StringComparison.Ordinal);
            var name = at < 0 ? Match.Empty : new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"").Match(json, at);
            return name.Success ? name.Groups[1].Value : "your Microsoft account";
        }

        public static bool PrismRunning => Process.GetProcessesByName("prismlauncher").Length > 0;

        public static void OpenPrism() => Process.Start(new ProcessStartInfo(PrismExe) { WorkingDirectory = Path.GetDirectoryName(PrismExe), UseShellExecute = true });

        /// <summary>Starts the ValCraft instance once so Prism downloads Minecraft, Fabric and Java now.</summary>
        public static void StartFirstDownload() =>
            Process.Start(new ProcessStartInfo(PrismExe, "--launch ValCraft") { WorkingDirectory = Path.GetDirectoryName(PrismExe), UseShellExecute = true });

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenMutexW(uint access, bool inherit, string name);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        /// <summary>A Minecraft with the ValCraft mod is running (it holds this mutex once it's up).</summary>
        public static bool MinecraftRunning()
        {
            var h = OpenMutexW(0x00100000, false, MinecraftMutex);
            if (h == IntPtr.Zero) return false;
            CloseHandle(h);
            return true;
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        /// <summary>Closes the (hidden) ValCraft Minecraft the normal way, as tools/stop_minecraft.ps1 does.</summary>
        public static void CloseMinecraft()
        {
            var pids = new List<uint>();
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='java.exe' OR Name='javaw.exe'"))
                foreach (ManagementObject o in q.Get())
                    if ((o["CommandLine"] as string ?? "").Contains("-Dvalcraft.startHidden")) pids.Add((uint)o["ProcessId"]);
            foreach (uint pid in pids)
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out uint p);
                    var sb = new StringBuilder(64);
                    GetClassName(h, sb, 64);
                    if (p == pid && sb.ToString() == "SDL_app") PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
        }

        // ---- mod managers ------------------------------------------------------------------------------

        public sealed class Manager
        {
            public string Name, Site, ProfilesRoot;
            public Func<string> FindExe;
            public string GitHubRepo, AssetPattern;  // for "download and install it for me"
            public string[] Steps;
            public string Exe => FindExe();
            public bool Installed => Exe != null || Directory.Exists(ProfilesRoot);
        }

        static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        static string FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

        public static readonly Manager[] Managers =
        {
            new Manager
            {
                Name = "Gale",
                Site = "https://github.com/Kesomannen/gale/releases/latest",
                ProfilesRoot = Path.Combine(AppData, "com.kesomannen.gale", "valheim", "profiles"),
                FindExe = () => FirstExisting(Path.Combine(ProgramFiles, "Gale", "gale.exe"), Path.Combine(LocalAppData, "Programs", "Gale", "gale.exe"), Path.Combine(LocalAppData, "Gale", "gale.exe")),
                GitHubRepo = "Kesomannen/gale", AssetPattern = "_x64-setup\\.exe$",
                Steps = new[]
                {
                    "Open Gale and pick Valheim from the game list.",
                    "Click the profile name at the top left, then New profile. Name it ValCraft.",
                    "Open the File menu (top left), then Import, then Local mod, and pick the ValCraft zip (the Show the mod zip button below opens its folder).",
                    "If Gale asks to install dependencies (BepInExPack), say yes.",
                },
            },
            new Manager
            {
                Name = "r2modman",
                Site = "https://github.com/ebkr/r2modmanPlus/releases/latest",
                ProfilesRoot = Path.Combine(AppData, "r2modmanPlus-local", "Valheim", "profiles"),
                FindExe = () => FirstExisting(Path.Combine(LocalAppData, "Programs", "r2modman", "r2modman.exe"), Path.Combine(ProgramFiles, "r2modman", "r2modman.exe")),
                GitHubRepo = "ebkr/r2modmanPlus", AssetPattern = "^r2modman-Setup-.*\\.exe$",
                Steps = new[]
                {
                    "Open r2modman and pick Valheim.",
                    "Create a new profile (Create new) and name it ValCraft, then Select it.",
                    "In the Online tab, search BepInExPack_Valheim and Download it.",
                    "Open Settings, then Profile, then Import local mod, and pick the ValCraft zip (the Show the mod zip button below opens its folder).",
                },
            },
            new Manager
            {
                Name = "Thunderstore Mod Manager",
                Site = "https://www.overwolf.com/app/thunderstore-thunderstore_mod_manager",
                ProfilesRoot = Path.Combine(AppData, "Thunderstore Mod Manager", "DataFolder", "Valheim", "profiles"),
                FindExe = () => null,  // an Overwolf app: started from Overwolf
                Steps = new[]
                {
                    "Open the Thunderstore Mod Manager (from Overwolf) and pick Valheim.",
                    "Create a new profile (Create new) and name it ValCraft, then Select it.",
                    "In the Online tab, search BepInExPack_Valheim and Download it.",
                    "Open Settings, then Profile, then Import local mod, and pick the ValCraft zip (the Show the mod zip button below opens its folder).",
                },
            },
        };

        /// <summary>Profiles of this manager that have BepInEx and ValCraft in them.</summary>
        public static List<string> ProfilesWithValCraft(Manager m)
        {
            var found = new List<string>();
            if (!Directory.Exists(m.ProfilesRoot)) return found;
            foreach (var profile in Directory.GetDirectories(m.ProfilesRoot))
            {
                string bep = Path.Combine(profile, "BepInEx");
                string plugins = Path.Combine(bep, "plugins");
                bool loader = File.Exists(Path.Combine(bep, "core", "BepInEx.Preloader.dll")) || File.Exists(Path.Combine(bep, "core", "BepInEx.dll"));
                bool mod = Directory.Exists(plugins) && Directory.GetFiles(plugins, "ValCraft.dll", SearchOption.AllDirectories).Length > 0;
                if (loader && mod) found.Add(Path.GetFileName(profile));
            }
            return found;
        }

        public static void OpenManager(Manager m)
        {
            if (m.Exe != null) Process.Start(new ProcessStartInfo(m.Exe) { UseShellExecute = true });
            else OpenUrl(m.Site);
        }

        /// <summary>Downloads the manager's latest Windows installer from its GitHub releases and runs it (waits for it).</summary>
        public static void InstallManager(Manager m, Action<string> status)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "ValCraft-Installer";
                status($"Finding the latest {m.Name}…");
                string json = web.DownloadString($"https://api.github.com/repos/{m.GitHubRepo}/releases/latest");
                string url = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>()
                    .Select(x => x.Groups[1].Value)
                    .FirstOrDefault(u => Regex.IsMatch(Path.GetFileName(new Uri(u).LocalPath), m.AssetPattern, RegexOptions.IgnoreCase));
                if (url == null) throw new InvalidOperationException($"Couldn't find {m.Name}'s Windows installer; get it from {m.Site}.");
                string file = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));
                status($"Downloading {Path.GetFileName(file)}…");
                web.Headers[HttpRequestHeader.UserAgent] = "ValCraft-Installer";
                web.DownloadFile(url, file);
                status($"Running the {m.Name} installer…");
                using (var p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })) p.WaitForExit();
            }
        }

        public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        public static void ShowInExplorer(string file) => Process.Start("explorer.exe", $"/select,\"{file}\"");
    }
}
