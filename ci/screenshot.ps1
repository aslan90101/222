param(
  [string]$Exe = "dist/EndorVPN/EndorVPN.exe",
  [string]$OutDir = "ci/reports"
)
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinApi {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$full = (Resolve-Path $Exe).Path
$appDir = Split-Path $full -Parent
Write-Host "launching $full"
$p = Start-Process $full -PassThru

# wait for the app to signal that the country list has rendered
$readyFile = Join-Path $appDir "data\ui-ready.txt"
if (Test-Path $readyFile) { Remove-Item $readyFile -Force }
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 700
  if (Test-Path $readyFile) { Write-Host "ui ready marker found"; break }
  if ($p.HasExited) { break }
}
Start-Sleep -Seconds 2
if ($p.HasExited) { Write-Host "EXITED code=$($p.ExitCode)" }

$hwnd = $p.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) {
  for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 500; $p.Refresh(); $hwnd = $p.MainWindowHandle; if ($hwnd -ne [IntPtr]::Zero) { break } }
}
Write-Host "hwnd=$hwnd title=$($p.MainWindowTitle)"
[WinApi]::ShowWindow($hwnd, 5) | Out-Null
[WinApi]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 2

$rect = New-Object WinApi+RECT
[WinApi]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Host "rect=$($rect.Left),$($rect.Top) size=${w}x${h}"

$bounds = New-Object System.Drawing.Rectangle($rect.Left, $rect.Top, $w, $h)
function Shot($name) {
  $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
  $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Host "shot $name"
}

# ---- UI Automation helpers (independent of window size / DPI) ----
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
function Find-ById($id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  for ($i = 0; $i -lt 20; $i++) {
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($el -ne $null) { return $el }
    Start-Sleep -Milliseconds 400
  }
  return $null
}
function Invoke-ById($id) {
  $el = Find-ById $id
  if ($el -eq $null) { Write-Host "UIA: element '$id' not found"; return $false }
  try {
    $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pat.Invoke()
    Write-Host "UIA: invoked '$id'"
    return $true
  } catch {
    Write-Host "UIA: invoke failed for '$id': $($_.Exception.Message)"
    return $false
  }
}

Shot "shot-1-main-ru.png"
Write-Host "---- app log after boot ----"
$logPath = Join-Path $appDir "data\endorvpn.log"
if (Test-Path $logPath) { Get-Content $logPath | Select-Object -Last 25 | ForEach-Object { Write-Host $_ } }

# language switch -> EN
Invoke-ById "BtnEn" | Out-Null
Start-Sleep -Milliseconds 1200
Shot "shot-2-main-en.png"

# settings sheet
Invoke-ById "BtnSettings" | Out-Null
Start-Sleep -Milliseconds 1200
Shot "shot-3-settings.png"
Invoke-ById "BtnCloseSettings" | Out-Null
Start-Sleep -Milliseconds 900

# pick the first country row by clicking its centre
$rowCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$row = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $rowCond)
if ($row -ne $null) {
  $r = $row.Current.BoundingRectangle
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($r.X + 40), [int]($r.Y + $r.Height / 2))
  Start-Sleep -Milliseconds 200
  Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);' -Name M -Namespace W | Out-Null
  [W.M]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [W.M]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
  Write-Host "clicked country row at $($r.X + 40),$($r.Y + $r.Height / 2)"
} else {
  Write-Host "UIA: no country row found"
}
Start-Sleep -Milliseconds 900
Shot "shot-4-country-selected.png"

# connect via the big power button
$logStart = 0
if (Test-Path $logPath) { $logStart = (Get-Item $logPath).Length }
Invoke-ById "PowerButton" | Out-Null
Start-Sleep -Seconds 4
Shot "shot-5-connecting.png"
# wait for the tunnel: poll the app log instead of guessing
$connected = $false
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 2
  if (Test-Path $logPath) {
    $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
    try {
      if ($fs.Length -gt $logStart) {
        $fs.Seek($logStart, 'Begin') | Out-Null
        $sr = New-Object System.IO.StreamReader($fs)
        $tail = $sr.ReadToEnd()
        if ($tail -match "tunnel up via") { $connected = $true }
      }
    } finally { $fs.Dispose() }
    if ($connected) { break }
  }
}
Write-Host "connected=$connected"
Start-Sleep -Seconds 3
Shot "shot-6-connected.png"

Write-Host "---- app log after connect ----"
if (Test-Path $logPath) {
  Copy-Item $logPath (Join-Path $OutDir "ui-app.log") -Force
  Get-Content $logPath | Select-Object -Last 45 | ForEach-Object { Write-Host $_ }
}
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
