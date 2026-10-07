@echo off
setlocal
cd /d "%~dp0"
if errorlevel 1 exit /b 1
if not exist bin\penwin.exe (
  call build.cmd
  if errorlevel 1 exit /b 1
)
bin\penwin.exe %*
exit /b %errorlevel%
