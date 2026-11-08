param(
  [string]$Exe = "dist\EndorVPN\EndorVPN.exe",
  [string]$OutDir = "ci/reports"
)
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinApi {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
  public const uint MOUSEEVENTF_LEFTUP = 0x0004;
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); mouse_event(MOUSEEVENTF_LEFTDOWN,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(MOUSEEVENTF_LEFTUP,0,0,0,UIntPtr.Zero); }
}
"@
$full = (Resolve-Path $Exe).Path
Write-Host "launching $full"
$p = Start-Process $full -PassThru
$readyFile = Join-Path (Split-Path $full -Parent) "data\ui-ready.txt"
if (Test-Path $readyFile) { Remove-Item $readyFile -Force }
$deadline = (Get-Date).AddSeconds(75)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 700
  if (Test-Path $readyFile) { Write-Host "ui ready marker found"; break }
  if ($p.HasExited) { break }
}
Start-Sleep -Seconds 3
if ($p.HasExited) { Write-Host "EXITED code=$($p.HasExitCode)$($p.ExitCode)" }
$hwnd = $p.MainWindowHandle
Write-Host "hwnd=$hwnd title=$($p.MainWindowTitle)"
[WinApi]::ShowWindow($hwnd, 5) | Out-Null
[WinApi]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 2

$rect = New-Object WinApi+RECT
[WinApi]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
Write-Host "rect=$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)"
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
function Shot($name) {
  $bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
  $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Host "shot $name"
}
Shot "shot-1-main-ru.png"
# switch to EN (top-right language segmented control)
[WinApi]::Click(($rect.Right - 106), ($rect.Top + 27))
Start-Sleep -Milliseconds 900
Shot "shot-2-main-en.png"
# open settings
[WinApi]::Click(($rect.Right - 74), ($rect.Top + 27))
Start-Sleep -Milliseconds 1200
Shot "shot-3-settings.png"
# close settings
[WinApi]::Click(($rect.Right - 370), ($rect.Top + 150))
Start-Sleep -Milliseconds 700
# click a country row (first row ~ y + 300)
[WinApi]::Click(($rect.Left + 200), ($rect.Top + 330))
Start-Sleep -Milliseconds 800
Shot "shot-4-country-selected.png"
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
