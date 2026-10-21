@echo off
cd /d "%~dp0"
if exist "build\cloudflare-state.json" (
  node scripts/cloud-status.mjs
  pause
  exit /b
)
node --env-file-if-exists=.env scripts/public-host.mjs
pause
