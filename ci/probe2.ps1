$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$out = "ci/reports"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$script:log = @()
function Say($m) { Write-Host $m; $script:log += [string]$m }

$work = "$PWD\work"
New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== download openvpn msi =="
$msi = "$work\ovpn.msi"
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile $msi -UseBasicParsing -TimeoutSec 300
$ex = "$work\ovpnx"
Start-Process msiexec.exe -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin = "$ex\OpenVPN\bin"
Say ("openvpn.exe exists: " + (Test-Path "$bin\openvpn.exe"))
Say ("--version: " + ((& "$bin\openvpn.exe" --version 2>&1) -join ' | '))

Say "== wintun =="
$wz = "$work\wintun.zip"
try {
  Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile $wz -UseBasicParsing -TimeoutSec 120
  Expand-Archive -Path $wz -DestinationPath "$work\wintun" -Force
  Get-ChildItem -Recurse "$work\wintun" | Where-Object { -not $_.PSIsContainer } | ForEach-Object { Say ("  " + $_.FullName) }
  Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
  Say "wintun.dll copied"
} catch { Say ("wintun FAIL: " + $_.Exception.Message) }

Say "== tap-windows6 installer =="
try {
  $tap = "$work\tap.exe"
  Invoke-WebRequest -Uri 'https://build.openvpn.net/downloads/releases/tap-windows-9.24.7-I601-Win10.exe' -OutFile $tap -UseBasicParsing -TimeoutSec 300
  $p = Start-Process $tap -ArgumentList "/S" -Wait -PassThru
  Say ("tap installer exit=" + $p.ExitCode)
  Start-Sleep -Seconds 3
  Say "adapters after tap install:"
  Get-NetAdapter | ForEach-Object { Say ("  " + $_.Name + " | " + $_.InterfaceDescription + " | " + $_.Status) }
} catch { Say ("tap FAIL: " + $_.Exception.Message) }

Say "== create tap adapter =="
try {
  $tapctl = "$bin\tapctl.exe"
  $p = Start-Process $tapctl -ArgumentList "create --name EndorVPN --hwid tap0901" -Wait -PassThru -NoNewWindow
  Say ("tapctl create exit=" + $p.ExitCode)
  Get-NetAdapter | Where-Object { $_.InterfaceDescription -like '*TAP*' } | ForEach-Object { Say ("  TAP: " + $_.Name + " | " + $_.InterfaceDescription + " | " + $_.Status) }
} catch { Say ("tapctl FAIL: " + $_.Exception.Message) }

Say "== vpngate parse =="
$cfgText = ""
try {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $resp = Invoke-WebRequest -Uri 'https://www.vpngate.net/api/iphone/' -UseBasicParsing -TimeoutSec 60
  $sw.Stop()
  $txt = $resp.Content
  Say ("bytes=" + $txt.Length + " in " + $sw.ElapsedMilliseconds + "ms")
  $s = $txt.IndexOf('*vpn_servers')
  $e = $txt.LastIndexOf("`n*")
  if ($s -ge 0 -and $e -gt $s) { $txt = $txt.Substring($s, $e - $s) }
  $lines = $txt -split "`r?`n"
  $servers = @()
  foreach ($l in $lines) {
    if (-not $l -or $l.StartsWith('*') -or $l.StartsWith('#')) { continue }
    $f = $l.Split(',')
    if ($f.Count -lt 16) { continue }
    $cfg = $f[$f.Count-1]
    if ($cfg.Length -lt 200) { continue }
    $servers += [pscustomobject]@{ host=$f[0]; ip=$f[1]; score=[int64]$f[2]; ping=[int]$f[3]; speed=[int64]$f[4]; country=$f[5]; cc=$f[6]; cfg=$cfg }
  }
  Say ("parsed servers=" + $servers.Count)
  $byCc = $servers | Group-Object cc | Sort-Object Name
  Say ("countries=" + $byCc.Count + " :: " + (($byCc | ForEach-Object { $_.Name + ':' + $_.Count }) -join ' '))
  $jp = $servers | Where-Object { $_.cc -eq 'JP' } | Sort-Object ping | Select-Object -First 1
  Say ("chosen: " + $jp.host + " " + $jp.ip + " ping=" + $jp.ping + " speed=" + $jp.speed)
  $cfgText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($jp.cfg))
  Set-Content -Path "$out\sample-server.ovpn" -Value $cfgText -Encoding ascii
  Say ("config len=" + $cfgText.Length)
  Say ("config: " + ($cfgText -split "`n" | Where-Object { $_ -notmatch '^#' -and $_.Trim() } | Select-Object -First 12 | ForEach-Object { $_.Trim() }) -join ' ; ')
} catch { Say ("vpngate FAIL: " + $_.Exception.Message) }

$ipBefore = ""
try { $ipBefore = (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 30).Content } catch { $ipBefore = "ERR" }
Say ("ip before: " + $ipBefore)

function TryOpenVpn($name, $cfgPath, $extraArgs) {
  Say ("---- attempt: " + $name + " ----")
  $log = "$work\ovpn-$name.log"
  $args = @("--config", $cfgPath, "--log", $log, "--verb", "4") + $extraArgs
  $proc = Start-Process "$bin\openvpn.exe" -ArgumentList $args -PassThru -WindowStyle Hidden
  Say ("pid=" + $proc.Id)
  $ok = $false
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    if ($proc.HasExited) { Say ("exited code=" + $proc.ExitCode); break }
    if (Test-Path $log) {
      $t = Get-Content $log -Raw -ErrorAction SilentlyContinue
      if ($t -match 'Initialization Sequence Completed') { $ok = $true; break }
      if ($t -match 'FATAL|ERROR') { Say ("log error seen") }
    }
  }
  Say ("connected=" + $ok + " after " + ($i*2) + "s")
  if ($ok) {
    $ipAfter = ""
    try { $ipAfter = (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 30).Content } catch { $ipAfter = "ERR " + $_.Exception.Message }
    Say ("ip after: " + $ipAfter)
    try {
      $sw2 = [Diagnostics.Stopwatch]::StartNew()
      $dl = Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=8000000' -UseBasicParsing -TimeoutSec 180
      $sw2.Stop()
      $mbps = [math]::Round(($dl.RawContentLength * 8 / 1e6) / ($sw2.ElapsedMilliseconds/1000), 1)
      Say ("speedtest: " + $mbps + " Mbit/s (" + $dl.RawContentLength + " bytes)")
    } catch { Say ("speedtest FAIL: " + $_.Exception.Message) }
    Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { Say ("  iface up: " + $_.Name + " | " + $_.InterfaceDescription) }
  }
  if (Test-Path $log) { Get-Content $log -Tail 40 | ForEach-Object { Say ("  LOG " + $_) } }
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Start-Sleep -Seconds 3 }
  return $ok
}

if ($cfgText) {
  $cfgSan = "$work\test.ovpn"
  Set-Content -Path $cfgSan -Value $cfgText -Encoding ascii

  $ok1 = TryOpenVpn "wintun" $cfgSan @("--windows-driver", "wintun")
  if (-not $ok1) {
    $ok2 = TryOpenVpn "tap" $cfgSan @("--windows-driver", "tap-windows6", "--dev-node", "EndorVPN")
  }
  if (-not $ok1 -and -not $ok2) {
    $ok3 = TryOpenVpn "autodriver" $cfgSan @()
  }
}

Say "== firewall test =="
try {
  & netsh advfirewall firewall add rule name="EndorVPN-KillSwitch" dir=out action=block enable=yes profile=any 2>&1 | ForEach-Object { Say ("  " + $_) }
  & netsh advfirewall firewall delete rule name="EndorVPN-KillSwitch" 2>&1 | ForEach-Object { Say ("  " + $_) }
} catch { Say ("firewall FAIL: " + $_.Exception.Message) }

Set-Content -Path "$out\probe2-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "== done =="
