# Closes the ValCraft Minecraft (the gradlew dev client or the bundled one Valheim starts) the normal way (it saves its world), even though its
# window is hidden: sends WM_CLOSE to its SDL windows. Falls back to nothing: never kills.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic; using System.Text;
public static class McWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<IntPtr> SdlWindows(uint pid) {
    var r = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var sb = new StringBuilder(64); GetClassName(h, sb, 64);
      if (p == pid && sb.ToString() == "SDL_app") r.Add(h); return true; }, IntPtr.Zero);
    return r;
  }
}
"@
$mc = Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" | Where-Object { $_.CommandLine -like '*fabric.dli.config*' -or $_.CommandLine -like '*-Dvalcraft.startHidden*' }
if (-not $mc) { "Minecraft isn't running"; exit 0 }
foreach ($proc in $mc) {
  foreach ($h in [McWin]::SdlWindows([uint32]$proc.ProcessId)) { [void][McWin]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
  $p = Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue
  if ($p -and $p.WaitForExit(30000)) { "Minecraft ($($proc.ProcessId)) saved and closed" } else { "Minecraft ($($proc.ProcessId)) is still running" }
}
