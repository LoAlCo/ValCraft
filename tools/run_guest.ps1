# Starts a second ValCraft Minecraft client on this PC, as a multiplayer test guest.
#
# It runs from fabric/run-guest as the offline user "Guest", on its own link (Local\ValCraft_guest)
# served by tools/fake_valheim_guest.py, which tells it to join the host's shared world. The host is
# the dev client (./gradlew runClient with VALCRAFT_LAN_OFFLINE=1, so offline guests may join) linked
# to the real Valheim. Uses the host's dev launch files (build the mod and run runClient once first).
param([string]$Name = "Guest", [string]$Link = "Local\ValCraft_guest", [string]$RunDir = "run-guest", [string]$Log = "$env:TEMP\valcraft_guest.log")

$root = Split-Path -Parent $PSScriptRoot
$fabric = Join-Path $root "fabric"
$java = Join-Path $env:JAVA_HOME "bin\java.exe"
if (-not (Test-Path $java)) { throw "Set JAVA_HOME to a JDK 25 first." }
$runDir = Join-Path $fabric $RunDir
New-Item -ItemType Directory -Force (Join-Path $runDir "config") | Out-Null

$javaArgs = @(
    "-Dfabric.dli.config=$fabric\.gradle\loom-cache\launch.cfg",
    "-Dfabric.dli.env=client",
    "-Dfabric.dli.main=net.fabricmc.loader.impl.launch.knot.KnotClient",
    "@$fabric\build\loom-cache\argFiles\runClient",
    "--enable-native-access=ALL-UNNAMED",
    "-XX:StackShadowPages=32",
    "--sun-misc-unsafe-memory-access=allow",
    "-Dvalcraft.link=$Link",
    "-Dvalcraft.quitWithValheim=false",
    "net.fabricmc.devlaunchinjector.Main",
    "--username", $Name
)
$p = Start-Process -FilePath $java -ArgumentList $javaArgs -WorkingDirectory $runDir -RedirectStandardOutput $Log -RedirectStandardError "$Log.err" -PassThru -WindowStyle Minimized
"guest Minecraft started (pid $($p.Id)); log: $Log"
