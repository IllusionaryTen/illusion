@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\setup-public.ps1
pause
