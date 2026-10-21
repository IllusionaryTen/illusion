@echo off
cd /d "%~dp0"
if exist "build\cloudflare-state.json" (
  node scripts/cloud-status.mjs
  pause
  exit /b
)
where node >nul 2>nul
if errorlevel 1 (
  echo Install Node.js 24 LTS from https://nodejs.org first.
  pause
  exit /b 1
)
node --env-file-if-exists=.env bot/index.mjs
pause
