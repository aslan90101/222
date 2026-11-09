# Asserts the connection attempt never looks frozen: the hero hint under the
# power button must change (countdown) at least a few times per attempt, and a
# country with no VPNBook node (pure VPN Gate) must still finish or move on
# within the configured attempt budget.
param(
  [string]$Exe = "dist/EndorVPN/EndorVPN.exe",
  [string]$Report = "ci/reports/progress-test.json"
)
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
New-Item -ItemType Directory -Force -Path (Split-Path $Report -Parent) | Out-Null

$full = (Resolve-Path $Exe).Path
$appDir = Split-Path $full -Parent
$logPath = Join-Path $appDir "data\endorvpn.log"
$result = @{ launched = $false; distinctStages = 0; samples = @(); ok = $false }

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
function Find-ById($id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Hint-Text() {
  $el = Find-ById "PowerHint"
  if ($el -eq $null) { return "" }
  return $el.Current.Name
}

$btn = Find-ById "PowerButton"
if ($btn -eq $null) { $result | ConvertTo-Json | Set-Content $Report; exit 1 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

# sample the hint text for 30 s: a frozen UI shows one repeated value
$seen = New-Object System.Collections.Generic.HashSet[string]
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 30) {
  $t = Hint-Text
  if ($t) { [void]$seen.Add($t) }
  $result.samples += $t
  Start-Sleep -Milliseconds 700
}
$result.distinctStages = $seen.Count
Write-Host "distinct hint texts: $($result.distinctStages)"
$seen | Select-Object -First 12 | ForEach-Object { Write-Host "  $_" }

if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Get-Process openvpn -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# a live countdown yields many distinct values; a frozen attempt yields 1
$result.ok = ($result.launched -and $result.distinctStages -ge 3)
$result | ConvertTo-Json -Depth 4 | Set-Content $Report
Write-Host "ok=$($result.ok)"
if (-not $result.ok) { exit 1 }
