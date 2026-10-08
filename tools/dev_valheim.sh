#!/usr/bin/env bash
# Dev loop: start Valheim windowed (1600x900) on the MC-V2 Gale profile and click into the first
# world (CTest). Minecraft (./gradlew runClient in fabric/) can keep running; it re-links by itself.
# Needs the universal-modder `um` tool for clicks.
set -e
PROFILE="$APPDATA/com.kesomannen.gale/valheim/profiles/MC-V2"
GAME="/d/SteamLibrary/steamapps/common/Valheim"
LOG="$PROFILE/BepInEx/LogOutput.log"
PRELOADER="$(cygpath -w "$PROFILE/BepInEx/core/BepInEx.Preloader.dll")"
rm -f "$LOG"
(cd "$GAME" && ./valheim.exe --doorstop-enabled true --doorstop-target-assembly "$PRELOADER" \
  -screen-fullscreen 0 -screen-width 1600 -screen-height 900 &)
for i in $(seq 1 90); do grep -q "ValCraft .* loaded" "$LOG" 2>/dev/null && break; sleep 1; done
# Valheim 1.0's menu takes longer to settle: try the clicks a few times, each time waiting for the world.
# main menu -> Start Game -> character Start -> world Start (menu points measured at 1600x900)
sleep 20
for attempt in 1 2 3; do
  um win drive --proc valheim "focus" "move 800 650" "click 800 650"; sleep 4
  um win drive --proc valheim "focus" "move 800 864" "click 800 864"; sleep 4
  um win drive --proc valheim "focus" "move 516 682" "click 516 682"; sleep 2
  um win drive --proc valheim "focus" "move 516 682" "click 516 682"
  for i in $(seq 1 45); do grep -q "puppet on" "$LOG" 2>/dev/null && { echo "in world, puppet on"; exit 0; }; sleep 1; done
  echo "not in the world yet (try $attempt); clicking through the menu again"
done
echo "no puppet; see $LOG"
exit 1
