@echo off
cd /d "%~dp0"
node scripts/deploy-cloudflare.mjs
pause
