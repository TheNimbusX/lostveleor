@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0artifacts\camp-polish-build\Razlom.exe" (
  echo Camp build is missing. Open the razlom project in Unity.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\camp-polish-build\Razlom.exe" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "%~dp0artifacts\camp-play.log"
