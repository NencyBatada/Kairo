@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\deploy.ps1" %*
exit /b %ERRORLEVEL%
