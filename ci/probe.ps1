$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$out = "ci/reports"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$log = @()
function Say($m) { Write-Host $m; $script:log += $m }

Say "== system =="
Say ("OS: " + (Get-CimInstance Win32_OperatingSystem).Caption + " build " + [System.Environment]::OSVersion.Version)
Say ("Admin: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
Say ("PS: " + $PSVersionTable.PSVersion)
Say ("7z: " + (Get-Command 7z -ErrorAction SilentlyContinue).Source)
Say ("FreeDisk: " + ((Get-PSDrive C).Free/1GB) + " GB")

Say "== connectivity =="
$urls = @(
  'https://www.vpngate.net/api/iphone/',
  'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi',
  'https://build.openvpn.net/downloads/releases/tap-windows-9.24.7-I601-Win10.exe',
  'https://raw.githubusercontent.com/rainsstory/vpngate-mirror/main/api/iphone/index.html',
  'https://api.ipify.org?format=json',
  'http://ip-api.com/json/?fields=status,country,countryCode,city,query'
)
foreach ($u in $urls) {
  try {
    $r = Invoke-WebRequest -Uri $u -Method Head -MaximumRedirection 5 -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
    Say ("OK   " + $r.StatusCode + " " + $u)
  } catch {
    Say ("FAIL " + $u + " :: " + $_.Exception.Message)
  }
}

Say "== openvpn msi =="
$msi = "$PWD\ovpn.msi"
try {
  Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile $msi -UseBasicParsing -TimeoutSec 300
  Say ("downloaded " + ((Get-Item $msi).Length/1MB) + " MB")
  $ex = "$PWD\ovpnx"
  Start-Process msiexec.exe -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$ex`"" -Wait
  Say "extracted tree:"
  Get-ChildItem -Recurse $ex | Where-Object { -not $_.PSIsContainer } | ForEach-Object { Say ("  " + $_.FullName.Substring($ex.Length+1) + "  " + [math]::Round($_.Length/1KB,1) + "KB") }
} catch { Say ("MSI FAIL: " + $_.Exception.Message) }

Say "== tap windows driver =="
try {
  $tap = "$PWD\tap.exe"
  Invoke-WebRequest -Uri 'https://build.openvpn.net/downloads/releases/tap-windows-9.24.7-I601-Win10.exe' -OutFile $tap -UseBasicParsing -TimeoutSec 300
  Say ("tap installer " + ((Get-Item $tap).Length/1MB) + " MB")
} catch { Say ("tap download FAIL: " + $_.Exception.Message) }

Say "== vpngate api =="
try {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $resp = Invoke-WebRequest -Uri 'https://www.vpngate.net/api/iphone/' -UseBasicParsing -TimeoutSec 60
  $sw.Stop()
  $txt = $resp.Content
  Say ("bytes=" + $txt.Length + " in " + $sw.ElapsedMilliseconds + "ms")
  $lines = $txt -split "`r?`n"
  Say ("lines=" + $lines.Count)
  Say ("header=" + $lines[1])
  $countries = @{}
  for ($i=2; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l.StartsWith('*') -or $l.Trim().Length -eq 0) { continue }
    $f = $l -split ',', 16
    if ($f.Count -lt 16) { continue }
    $cc = $f[6]
    if ($countries.ContainsKey($cc)) { $countries[$cc]++ } else { $countries[$cc] = 1 }
  }
  Say ("countries=" + $countries.Count)
  Say ("country list=" + (($countries.GetEnumerator() | Sort-Object Name | ForEach-Object { $_.Name + ':' + $_.Value }) -join ' '))
  $first = ($lines | Where-Object { $_ -notmatch '^\*' -and $_ -notmatch '^#' -and $_.Length -gt 100 } | Select-Object -First 1)
  $ff = $first -split ',', 16
  Say ("sample fields: host=" + $ff[0] + " ip=" + $ff[1] + " score=" + $ff[2] + " ping=" + $ff[3] + " speed=" + $ff[4] + " country=" + $ff[5] + " cc=" + $ff[6] + " sessions=" + $ff[7])
  $cfg = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ff[15]))
  Set-Content -Path "$out/sample-server.ovpn" -Value $cfg -Encoding utf8
  Say ("decoded config len=" + $cfg.Length)
} catch { Say ("vpngate FAIL: " + $_.Exception.Message) }

Say "== mirror =="
try {
  $r2 = Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/rainsstory/vpngate-mirror/main/api/iphone/index.html' -UseBasicParsing -TimeoutSec 60
  Say ("mirror bytes=" + $r2.Content.Length)
} catch { Say ("mirror FAIL: " + $_.Exception.Message) }

Say "== tap install attempt =="
try {
  $tapInf = Get-ChildItem -Recurse "$PWD\ovpnx" -Filter 'tap0901.inf' -ErrorAction SilentlyContinue | Select-Object -First 1
  Say ("inf found: " + $tapInf.FullName)
  if ($tapInf) {
    $p = Start-Process pnputil.exe -ArgumentList "/add-driver `"$($tapInf.FullName)`" /install" -Wait -PassThru -NoNewWindow
    Say ("pnputil exit=" + $p.ExitCode)
    $tapctl = Get-ChildItem -Recurse "$PWD\ovpnx" -Filter 'tapctl.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    Say ("tapctl: " + $tapctl.FullName)
    if ($tapctl) {
      $p2 = Start-Process $tapctl.FullName -ArgumentList "create --name EndorVPN0 --hwid tap0901" -Wait -PassThru -NoNewWindow
      Say ("tapctl exit=" + $p2.ExitCode)
      $p3 = Start-Process $tapctl.FullName -ArgumentList "list" -Wait -PassThru -NoNewWindow
      Say ("tapctl list exit=" + $p3.ExitCode)
    }
  }
  Get-NetAdapter | ForEach-Object { Say ("adapter: " + $_.Name + " | " + $_.InterfaceDescription + " | " + $_.Status) }
} catch { Say ("tap install FAIL: " + $_.Exception.Message) }

Say "== real vpn test =="
try {
  $ipBefore = (Invoke-WebRequest -Uri 'https://api.ipify.org?format=json' -UseBasicParsing -TimeoutSec 30).Content
  Say ("ip before: " + $ipBefore)
  $cfgPath = "$out/sample-server.ovpn"
  $ovpnExe = Get-ChildItem -Recurse "$PWD\ovpnx" -Filter 'openvpn.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
  Say ("openvpn exe: " + $ovpnExe.FullName)
  $rep = ($cfgPath -replace '\\','/')
  $loglevel = "$PWD\ovpn-test.log"
  $proc = Start-Process $ovpnExe.FullName -ArgumentList "--config `"$cfgPath`" --log `"$loglevel`" --verb 4" -PassThru -NoNewWindow -RedirectStandardError "$PWD\ovpn-err.log" -RedirectStandardOutput "$PWD\ovpn-out.log"
  Say ("openvpn pid=" + $proc.Id)
  Start-Sleep -Seconds 30
  $ipAfter = ""
  try { $ipAfter = (Invoke-WebRequest -Uri 'https://api.ipify.org?format=json' -UseBasicParsing -TimeoutSec 30).Content } catch { $ipAfter = "ERR " + $_.Exception.Message }
  Say ("ip after: " + $ipAfter)
  Say ("--- routes ---")
  Get-NetRoute | Where-Object { $_.InterfaceAlias -like '*EndorVPN*' -or $_.NextHop -eq '0.0.0.0' } | Select-Object -First 20 | ForEach-Object { Say ("  " + $_.DestinationPrefix + " via " + $_.NextHop + " if " + $_.InterfaceAlias + " metric " + $_.RouteMetric) }
  Say ("--- openvpn log tail ---")
  if (Test-Path $loglevel) { Get-Content $loglevel -Tail 60 | ForEach-Object { Say ("  " + $_) } }
  if (Test-Path "$PWD\ovpn-err.log") { Get-Content "$PWD\ovpn-err.log" -Tail 20 | ForEach-Object { Say ("  ERR " + $_) } }
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
} catch { Say ("vpn test FAIL: " + $_.Exception.Message) }

Set-Content -Path "$out/probe-report.md" -Value ($log -join "`n") -Encoding utf8
Say "== done =="
