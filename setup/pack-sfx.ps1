# Builds the single-file EndorVPN.exe installer.
#
# Layout produced by this script:
#   [stub EndorVPN-Setup.exe][zip payload][ASCII marker][int64 payload length]
# The stub finds the tail, unpacks the payload once into
# %LOCALAPPDATA%\EndorVPN and launches the real UI. No external tools: the zip
# is created with System.IO.Compression and appended with plain file I/O.
param(
  [string]$Stub = "setup/bin/x64/Release/net48/EndorVPN-Setup.exe",
  [string]$AppDir = "dist/EndorVPN",
  [string]$Out = "dist/EndorVPN.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$stubFull = (Resolve-Path $Stub).Path
$appFull = (Resolve-Path $AppDir).Path
$outFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out))
New-Item -ItemType Directory -Force -Path (Split-Path $outFull -Parent) | Out-Null

# runtime state must never ship inside the installer
foreach ($stale in @("data\work\openvpn.log", "data\work\active.ovpn", "data\work\auth.txt", "data\endorvpn.log", "data\ui-ready.txt", "payload.version")) {
  $p = Join-Path $appFull $stale
  if (Test-Path $p) { try { Remove-Item $p -Force -ErrorAction Stop } catch { Write-Host "skip locked $stale" } }
}

$zipPath = [System.IO.Path]::GetTempFileName() + ".zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($appFull, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$stubBytes = [System.IO.File]::ReadAllBytes($stubFull)
$zipBytes = [System.IO.File]::ReadAllBytes($zipPath)
$marker = [System.Text.Encoding]::ASCII.GetBytes("ENDORVPN_PAYLOAD_V1")
$length = [System.BitConverter]::GetBytes([int64]$zipBytes.Length)

$fs = [System.IO.File]::Create($outFull)
try {
  $fs.Write($stubBytes, 0, $stubBytes.Length)
  $fs.Write($zipBytes, 0, $zipBytes.Length)
  $fs.Write($marker, 0, $marker.Length)
  $fs.Write($length, 0, $length.Length)
} finally { $fs.Dispose() }
Remove-Item $zipPath -Force

# verify the exact layout the stub expects: [zip][marker][int64 length]
$all = [System.IO.File]::ReadAllBytes($outFull)
$zipLen = [System.BitConverter]::ToInt64($all, $all.Length - 8)
$markerAt = $all.Length - 8 - $marker.Length
$zipStart = $markerAt - $zipLen
$markerOk = ([System.Text.Encoding]::ASCII.GetString($all, $markerAt, $marker.Length) -eq "ENDORVPN_PAYLOAD_V1")
$zipOk = ($all[$zipStart] -eq 0x50 -and $all[$zipStart + 1] -eq 0x4B)
if (-not $markerOk) { throw "SFX self-check failed: marker mismatch" }
if (-not $zipOk) { throw "SFX self-check failed: payload is not a zip" }
if ($zipLen -ne $zipBytes.Length) { throw "SFX self-check failed: length mismatch" }
Write-Host "self-check: marker ok, zip ok, payload $([math]::Round($zipLen/1MB,2)) MB"

Write-Host "stub   : $([math]::Round($stubBytes.Length/1MB,2)) MB"
Write-Host "payload: $([math]::Round($zipBytes.Length/1MB,2)) MB"
Write-Host "single : $([math]::Round((Get-Item $outFull).Length/1MB,2)) MB -> $outFull"
