@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0artifacts\forest-bud-build\Razlom.exe" (
  echo The Pelag playtest build is missing. Open Unity: Razlom - Pelag - Combat tempo.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\forest-bud-build\Razlom.exe" -pelag-tempo-playtest -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "%~dp0artifacts\pelag-playtest.log"
