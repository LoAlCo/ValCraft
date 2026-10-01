@echo off
rem Play ValCraft: starts Valheim through Steam with a Gale/r2modman profile that has BepInEx and
rem ValCraft (MC-V2 by default; pass another profile folder name as the first argument).
rem Minecraft starts by itself, hidden, once Valheim loads ValCraft. Same as Gale's "Launch game".
setlocal
set "PROFILE_NAME=%~1"
if "%PROFILE_NAME%"=="" set "PROFILE_NAME=MC-V2"
set "PROFILE=%APPDATA%\com.kesomannen.gale\valheim\profiles\%PROFILE_NAME%"
set "PRELOADER=%PROFILE%\BepInEx\core\BepInEx.Preloader.dll"
if not exist "%PRELOADER%" (
  echo ValCraft: no BepInEx in the Gale profile "%PROFILE_NAME%" ^(%PROFILE%^).
  echo Install ValCraft into a Gale profile first, or pass that profile's name: "Play ValCraft.bat" MyProfile
  pause
  exit /b 1
)
set "STEAM="
for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Valve\Steam" /v SteamExe 2^>nul ^| find "SteamExe"') do set "STEAM=%%b"
if not defined STEAM (
  echo ValCraft: Steam isn't installed ^(or not found in the registry^).
  pause
  exit /b 1
)
start "" "%STEAM%" -applaunch 892970 --doorstop-enabled true --doorstop-target-assembly "%PRELOADER%"
