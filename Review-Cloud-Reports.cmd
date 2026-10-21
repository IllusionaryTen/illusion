@echo off
cd /d "%~dp0"
node scripts/cloud-admin.mjs list
pause
