@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\configure-connection.ps1
pause
