$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskConfigPath = Join-Path $taskRoot '.env'
$taskText = if (Test-Path -LiteralPath $taskConfigPath) {[System.IO.File]::ReadAllText($taskConfigPath)} else {[System.IO.File]::ReadAllText((Join-Path $taskRoot '.env.example'))}
Write-Output 'Create a Telegram bot in @BotFather with /newbot.'
Write-Output 'Paste its token below. It will be hidden and saved only in this project''s .env file.'
$taskSecret = Read-Host 'BotFather token' -AsSecureString
$taskToken = [System.Net.NetworkCredential]::new('', $taskSecret).Password.Trim()
if ($taskToken -notmatch '^\d+:[A-Za-z0-9_-]+$') {
  Write-Output 'That does not look like a bot token. Copy only the token from @BotFather and paste it once.'
  Write-Output 'The hidden prompt does not show pasted characters. Existing settings were not changed.'
  $taskToken = $null
  $taskSecret.Dispose()
  exit 1
}
Write-Output 'Checking the token with Telegram...'
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
try {
  $taskIdentity = Invoke-RestMethod -Method Post -Uri ('https://api.telegram.org/bot' + $taskToken + '/getMe') -TimeoutSec 15 -MaximumRedirection 0 -ErrorAction Stop -Verbose:$false -Debug:$false
} catch {
  $taskStatusCode = 0
  if ($_.Exception.Response) { $taskStatusCode = [int]$_.Exception.Response.StatusCode }
  if ($taskStatusCode -eq 401) {
    Write-Output 'Telegram rejected this token (401). In @BotFather, select your bot and copy its current API token.'
  } else {
    Write-Output 'Could not verify the token with Telegram. Check your internet connection and try again.'
  }
  Write-Output 'Existing settings were not changed. No token details were displayed.'
  $taskToken = $null
  $taskSecret.Dispose()
  exit 1
}
if (-not $taskIdentity.ok -or -not $taskIdentity.result.username) {
  Write-Output 'Telegram did not accept this bot token. Copy the current API token from @BotFather and paste it once.'
  Write-Output 'Existing settings were not changed.'
  $taskToken = $null
  $taskSecret.Dispose()
  exit 1
}
if ($taskText -match '(?m)^TELEGRAM_BOT_TOKEN=') {
  $taskText = [regex]::Replace($taskText,'(?m)^TELEGRAM_BOT_TOKEN=.*$',("TELEGRAM_BOT_TOKEN=" + $taskToken))
} else { $taskText += "`r`nTELEGRAM_BOT_TOKEN=$taskToken`r`n" }
[System.IO.File]::WriteAllText($taskConfigPath,$taskText,[System.Text.UTF8Encoding]::new($false))
$taskToken = $null
$taskSecret.Dispose()
Write-Output ('Verified @' + $taskIdentity.result.username + ' and saved. Run Start-Telegram-Bot.cmd to enable /download.')
