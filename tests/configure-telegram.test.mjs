import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, copyFileSync, writeFileSync, readFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

test('Windows bot setup verifies before saving, preserves rejected settings, and hides token output', { skip: process.platform !== 'win32' || !existsSync(resolve('scripts/configure-telegram.ps1')) }, () => {
  const dir = mkdtempSync(join(tmpdir(), 'illusion-token-setup-test-'));
  try {
    mkdirSync(join(dir, 'scripts'));
    copyFileSync(resolve('scripts/configure-telegram.ps1'), join(dir, 'scripts/configure-telegram.ps1'));
    const original = 'HOST=127.0.0.1\nTELEGRAM_BOT_TOKEN=existing-value\n';
    writeFileSync(join(dir, '.env.example'), original);
    writeFileSync(join(dir, 'runner.ps1'), `
function Read-Host { param([string]$Prompt,[switch]$AsSecureString) $value=[System.Security.SecureString]::new();foreach($character in $env:ILLUSION_SETUP_TEST_TOKEN.ToCharArray()){$value.AppendChar($character)};return $value }
function Invoke-RestMethod {
  if ($env:ILLUSION_SETUP_TEST_MODE -eq 'network') { throw 'Simulated unavailable network' }
  if ($env:ILLUSION_SETUP_TEST_MODE -eq 'rejected') { return [pscustomobject]@{ok=$false;error_code=401} }
  return [pscustomobject]@{ok=$true;result=[pscustomobject]@{username='IllusionFixtureBot'}}
}
& (Join-Path $PSScriptRoot 'scripts/configure-telegram.ps1')
exit $LASTEXITCODE
`);
    for (const [mode, input, expectedExit] of [
      ['accepted', ' 123456:fixture_test_token ', 0],
      ['rejected', '123456:fixture_test_token', 1],
      ['network', '123456:fixture_test_token', 1],
      ['accepted', '123456:duplicate123456:duplicate', 1]
    ]) {
      writeFileSync(join(dir, '.env'), original);
      const run = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', join(dir, 'runner.ps1')], {
        encoding: 'utf8', windowsHide: true, timeout: 20000,
        env: { ...process.env, ILLUSION_SETUP_TEST_TOKEN: input, ILLUSION_SETUP_TEST_MODE: mode }
      });
      assert.equal(run.status, expectedExit, `${mode}: ${run.stdout}\n${run.stderr}`);
      assert.ok(!run.stdout.includes('fixture_test_token') && !run.stderr.includes('fixture_test_token'));
      const saved = readFileSync(join(dir, '.env'), 'utf8');
      if (expectedExit === 0) { assert.match(saved, /TELEGRAM_BOT_TOKEN=123456:fixture_test_token/);assert.match(run.stdout, /Verified @IllusionFixtureBot/); }
      else assert.equal(saved, original, `${mode} must preserve existing configuration`);
    }
  } finally {
    if (!dir.startsWith(join(tmpdir(), 'illusion-token-setup-test-'))) throw new Error('Unexpected test path');
    rmSync(dir, { recursive: true, force: true });
  }
});
