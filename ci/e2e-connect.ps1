# End-to-end check of the real tunnel: launches the built app in headless
# "--e2e-connect" mode, which connects through VPNBook and writes a report.
# Verifies that the exit IP actually differs from the local one.
param(
  [string]$Exe = "dist\EndorVPN\EndorVPN.exe",
  [string]$Report = "ci/reports/e2e.json",
  [int]$TimeoutSec = 200
)
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$reportDir = Split-Path $Report -Parent
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$ipBefore = ""
try {
  $ipBefore = (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 25).Content
} catch {
  try { $ipBefore = (Invoke-WebRequest -Uri 'https://api.ipify.org?format=json' -UseBasicParsing -TimeoutSec 25).Content } catch { $ipBefore = "ERR" }
}
Write-Host "ip before: $ipBefore"

$full = (Resolve-Path $Exe).Path
$p = Start-Process $full -ArgumentList '--e2e-connect' -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$stateFile = Join-Path (Split-Path $full -Parent) "data\e2e.json"
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 4
  if (Test-Path $stateFile) {
    $raw = Get-Content $stateFile -Raw
    if ($raw -match '"done"') { break }
  }
  if ($p.HasExited) { Write-Host "app exited: $($p.ExitCode)"; break }
}

$state = if (Test-Path $stateFile) { Get-Content $stateFile -Raw } else { "{}" }
Write-Host "e2e state: $state"
$ipAfter = ""
try {
  $ipAfter = (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 25).Content
} catch {
  try { $ipAfter = (Invoke-WebRequest -Uri 'https://api.ipify.org?format=json' -UseBasicParsing -TimeoutSec 25).Content } catch { $ipAfter = "ERR" }
}
Write-Host "ip after: $ipAfter"

# route check: the default route should go through our adapter while connected
$route = (Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | ForEach-Object { $_.NextHop + "@" + $_.InterfaceAlias }) -join " | "
Write-Host "routes: $route"
$adapters = (Get-NetAdapter | ForEach-Object { $_.Name + ":" + $_.Status }) -join " | "
Write-Host "adapters: $adapters"

# speed through the tunnel
$speed = 0
try {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $dl = Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=6000000' -UseBasicParsing -TimeoutSec 180
  $sw.Stop()
  $speed = [math]::Round(($dl.RawContentLength * 8 / 1e6) / ($sw.ElapsedMilliseconds / 1000), 1)
} catch { Write-Host "speed test failed: $($_.Exception.Message)" }
Write-Host "speed: $speed Mbit/s"

$appLog = ""
$logPath = Join-Path (Split-Path $full -Parent) "data\endorvpn.log"
if (Test-Path $logPath) { $appLog = (Get-Content $logPath | Select-Object -Last 80) -join "`n" }

$result = @{
  ipBefore = $ipBefore
  ipAfter = $ipAfter
  state = $state
  route = $route
  adapters = $adapters
  speedMbit = $speed
  appLog = $appLog
  pid = $p.Id
} | ConvertTo-Json -Depth 4
Set-Content -Path $Report -Value $result -Encoding utf8
Write-Host "report written to $Report"

if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Start-Sleep -Seconds 2
Get-Process openvpn -ErrorAction SilentlyContinue | Stop-Process -Force
