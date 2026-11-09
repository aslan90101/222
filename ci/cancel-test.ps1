# Verifies that the power button cancels an in-flight connection attempt.
#
# Flow: launch the app, click power, wait until the log says an attempt started,
# click power again, and assert that the app reports a cancellation and is back
# to idle within a few seconds. Then connect normally to make sure cancelling
# did not leave the engine wedged.
param(
  [string]$Exe = "dist/EndorVPN/EndorVPN.exe",
  [string]$Report = "ci/reports/cancel-test.json"
)
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
New-Item -ItemType Directory -Force -Path (Split-Path $Report -Parent) | Out-Null

$full = (Resolve-Path $Exe).Path
$appDir = Split-Path $full -Parent
$logPath = Join-Path $appDir "data\endorvpn.log"
$result = @{ launched = $false; cancelClicked = $false; sawAttempt = $false; sawCancel = $false; msToIdle = -1; ok = $false }

$p = Start-Process $full -PassThru
$ready = Join-Path $appDir "data\ui-ready.txt"
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline -and -not (Test-Path $ready)) { Start-Sleep -Milliseconds 500; if ($p.HasExited) { break } }
if (-not $p.HasExited) { $result.launched = $true }
Start-Sleep -Seconds 2

$hwnd = $p.MainWindowHandle
for ($i = 0; $i -lt 30 -and $hwnd -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 500; $p.Refresh(); $hwnd = $p.MainWindowHandle }
if ($hwnd -eq [IntPtr]::Zero) { $result | ConvertTo-Json | Set-Content $Report; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
function Invoke-ById($id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($el -eq $null) { Write-Host "UIA: '$id' not found"; return $false }
  $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Write-Host "UIA: invoked '$id'"
  return $true
}

$logStart = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
function Read-NewLog() {
  if (-not (Test-Path $logPath)) { return "" }
  $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
  try {
    if ($fs.Length -le $logStart) { return "" }
    $fs.Seek($logStart, 'Begin') | Out-Null
    $sr = New-Object System.IO.StreamReader($fs)
    return $sr.ReadToEnd()
  } finally { $fs.Dispose() }
}

# 0. wait until the server list is ready.
#
# The power button does nothing while the feed is still loading, so clicking
# immediately produced a flaky run: no attempt was logged and the whole test
# failed for a reason unrelated to cancelling. Wait for the book first.
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 500
  $l = Read-NewLog
  if ($l -match "server book: \d+ servers" -and $l -match "render: servers=") { break }
}

# 1. start connecting
Invoke-ById "PowerButton" | Out-Null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 500
  if ((Read-NewLog) -match "attempt \d+/\d+: ") { $result.sawAttempt = $true; break }
}
Write-Host "attempt started: $($result.sawAttempt) after $($sw.ElapsedMilliseconds) ms"

# 2. cancel while it is still connecting (no tunnel up yet)
Start-Sleep -Seconds 1
Invoke-ById "PowerButton" | Out-Null
$result.cancelClicked = $true
$cancelAt = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 300
  $log = Read-NewLog
  if ($log -match "user cancelled the connection attempt") {
    $result.sawCancel = $true
    $result.msToIdle = $cancelAt.ElapsedMilliseconds
    break
  }
}
Write-Host "cancel seen: $($result.sawCancel), engine idle after $($result.msToIdle) ms"

# 3. a cancelled attempt must not block the next one
$before = (Read-NewLog)
Invoke-ById "PowerButton" | Out-Null
$reconnectable = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 500
  $delta = (Read-NewLog).Substring([Math]::Min($before.Length, (Read-NewLog).Length))
  if ($delta -match "attempt \d+/\d+: ") { $reconnectable = $true; break }
}
Write-Host "engine reusable after cancel: $reconnectable"
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Get-Process openvpn -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$result.ok = ($result.launched -and $result.sawAttempt -and $result.sawCancel -and $result.msToIdle -ge 0 -and $result.msToIdle -lt 8000 -and $reconnectable)
$result | Add-Member -NotePropertyName reconnectable -NotePropertyValue $reconnectable
$result | ConvertTo-Json | Set-Content $Report
Get-Content $Report
if (-not $result.ok) { exit 1 }
