@echo off
setlocal
title App Amplifier
cd /d "%~dp0"

if not exist "AppAmplifier.exe" (
  echo AppAmplifier.exe is missing.
  echo Extract the complete release ZIP before running the launcher.
  pause
  exit /b 1
)

set "APO_FOUND="
if exist "C:\Program Files\EqualizerAPO\Editor.exe" set "APO_FOUND=1"
if exist "C:\Program Files (x86)\EqualizerAPO\Editor.exe" set "APO_FOUND=1"
reg query "HKLM\SOFTWARE\EqualizerAPO" >nul 2>&1 && set "APO_FOUND=1"

if not defined APO_FOUND (
  echo Equalizer APO was not detected.
  echo App Amplifier can still run in limited ratio mode, but it cannot boost
  echo a session above 100%% without Equalizer APO providing device headroom.
  echo.
  choice /C YN /M "Open the official Equalizer APO download page"
  if not errorlevel 2 start "" "https://sourceforge.net/projects/equalizerapo/"
  echo.
  choice /C YN /M "Run App Amplifier in limited mode now"
  if errorlevel 2 exit /b 2
)

start "" "AppAmplifier.exe"
exit /b 0
