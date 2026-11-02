@echo off
chcp 65001 >nul
setlocal
set "HERE=%~dp0"
set "PS1=%HERE%dsp.ps1"

if not exist "%PS1%" (
  echo [X] dsp.ps1 not found next to this launcher.
  pause
  exit /b 1
)

set "PSEXE="
for /f "delims=" %%i in ('where pwsh.exe 2^>nul') do if not defined PSEXE set "PSEXE=%%i"
if not defined PSEXE for /f "delims=" %%i in ('where powershell.exe 2^>nul') do if not defined PSEXE set "PSEXE=%%i"
if not defined PSEXE (
  echo [X] PowerShell not found in PATH.
  pause
  exit /b 1
)

"%PSEXE%" -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
if errorlevel 1 (
  echo.
  echo [X] Failed. Press any key to close this window.
  pause >nul
)
endlocal
