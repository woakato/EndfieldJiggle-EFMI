@echo off
setlocal
set "ROOT=%~dp0"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%ROOT%Collect-Diagnostics.ps1" -PackageDirectory "%ROOT%"
if errorlevel 1 (
  echo.
  echo Diagnostic collection failed. Check the message above.
  pause
  exit /b 1
)
echo.
echo Diagnostic collection complete. Review the generated Diagnostics folder before sharing.
pause
