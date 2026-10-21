@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\configure-telegram.ps1
pause
