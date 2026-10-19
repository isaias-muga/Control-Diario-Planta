@echo off
rem Test double for the Azure CLI: see fake-cli.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-cli.ps1" az %*
exit /b %ERRORLEVEL%
