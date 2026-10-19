@echo off
rem Test double for the GitHub CLI: see fake-cli.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-cli.ps1" gh %*
exit /b %ERRORLEVEL%
