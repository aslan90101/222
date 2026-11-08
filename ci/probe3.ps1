$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$out = "ci/reports"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$script:log = @()
function Say($m) { Write-Host $m; $script:log += [string]$m }
$work = "$PWD\work3"
New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== parse vpngate =="
$servers = @()
try {
  $resp = Invoke-WebRequest -Uri 'https://www.vpngate.net/api/iphone/' -UseBasicParsing -TimeoutSec 60
  $txt = $resp.Content
  Say ("bytes=" + $txt.Length)
  $lines = $txt -split "`r?`n"
  foreach ($l in $lines) {
    if (-not $l -or $l.StartsWith('*') -or $l.StartsWith('#')) { continue }
    $f = $l -split ',', 15
    if ($f.Count -lt 15) { continue }
    if ($f[14].Length -lt 200) { continue }
    $servers += [pscustomobject]@{ host=$f[0]; ip=$f[1]; score=[int64]$f[2]; ping=[int]$f[3]; speed=[int64]$f[4]; country=$f[5]; cc=$f[6]; sessions=[int]$f[7]; cfg=$f[14] }
  }
  Say ("parsed=" + $servers.Count)
  $g = $servers | Group-Object cc | Sort-Object Name
  Say ("countries=" + $g.Count)
  Say (($g | ForEach-Object { $_.Name + '(' + $_.Count + ')' }) -join ' ')
} catch { Say ("parse FAIL: " + $_.Exception.Message) }

Say "== extract openvpn =="
$msi = "$work\ovpn.msi"
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile $msi -UseBasicParsing -TimeoutSec 300
$ex = "$work\ovpnx"
Start-Process msiexec.exe -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin = "$ex\OpenVPN\bin"

Say "== wintun dll =="
try {
  Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
  Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
  Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
  Say "wintun ok"
} catch { Say ("wintun FAIL: " + $_.Exception.Message) }

Say "== tap driver =="
try {
  Invoke-WebRequest -Uri 'https://build.openvpn.net/downloads/releases/tap-windows-9.24.7-I601-Win10.exe' -OutFile "$work\tap.exe" -UseBasicParsing -TimeoutSec 300
  $p = Start-Process "$work\tap.exe" -ArgumentList "/S" -Wait -PassThru
  Say ("tap install exit=" + $p.ExitCode)
} catch { Say ("tap FAIL: " + $_.Exception.Message) }

function Get-Ip($tag) {
  try { return (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 25).Content } catch { return ("ERR " + $_.Exception.Message) }
}
Say ("ip before: " + (Get-Ip 'before'))

function Try-OpenVpn($name, $cfgPath, $extra, $waitSec) {
  Say ("---- openvpn attempt: " + $name + " ----")
  $log = "$work\ovpn-$name.log"
  $args = @("--config", $cfgPath, "--log", $log, "--verb", "5") + $extra
  $proc = Start-Process "$bin\openvpn.exe" -ArgumentList $args -PassThru -WindowStyle Hidden
  Say ("pid=" + $proc.Id)
  $ok = $false
  $i = 0
  for ($i = 0; $i -lt $waitSec; $i++) {
    Start-Sleep -Seconds 2
    if ($proc.HasExited) { Say ("exited code=" + $proc.ExitCode); break }
    if (Test-Path $log) {
      $t = Get-Content $log -Raw -ErrorAction SilentlyContinue
      if ($t -match 'Initialization Sequence Completed') { $ok = $true; break }
      if ($t -match 'FATAL') { Say "fatal seen"; }
    }
  }
  Say ("connected=" + $ok + " after ~" + ($i*2) + "s")
  if ($ok) {
    Say ("ip after: " + (Get-Ip 'after'))
    try {
      $sw = [Diagnostics.Stopwatch]::StartNew()
      $dl = Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=10000000' -UseBasicParsing -TimeoutSec 240
      $sw.Stop()
      $mbps = [math]::Round(($dl.RawContentLength * 8 / 1e6) / ($sw.ElapsedMilliseconds/1000), 1)
      Say ("download speed: " + $mbps + " Mbit/s (" + $dl.RawContentLength + " bytes in " + $sw.ElapsedMilliseconds + "ms)")
    } catch { Say ("speedtest FAIL: " + $_.Exception.Message) }
    Say ("routes: " + ((Get-NetRoute -DestinationPrefix '0.0.0.0/0' | ForEach-Object { $_.NextHop + '@' + $_.InterfaceAlias + ':' + $_.RouteMetric }) -join ' | '))
  }
  if (Test-Path $log) { Get-Content $log -Tail 25 | ForEach-Object { Say ("  LOG " + $_) } }
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Start-Sleep -Seconds 4 }
  return $ok
}

if ($servers.Count -gt 0) {
  $pick = $servers | Where-Object { $_.cc -eq 'JP' } | Sort-Object ping | Select-Object -First 1
  if (-not $pick) { $pick = $servers | Sort-Object ping | Select-Object -First 1 }
  Say ("pick: " + $pick.host + " " + $pick.ip + " " + $pick.country + " ping=" + $pick.ping + " speed=" + [math]::Round($pick.speed/1e6, 1) + "Mbps")
  Set-Content -Path "$out\sample-server.ovpn" -Value ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($pick.cfg))) -Encoding ascii
  $cfg = "$work\t.ovpn"
  Copy-Item "$out\sample-server.ovpn" $cfg

  $a = Try-OpenVpn "wintun" $cfg @("--windows-driver", "wintun") 40
  if (-not $a) {
    $b = Try-OpenVpn "tap" $cfg @("--windows-driver", "tap-windows6") 40
  }
  if (-not $a -and -not $b) {
    $c = Try-OpenVpn "default" $cfg @() 40
  }
}

Say "== killswitch rules =="
try {
  & netsh advfirewall firewall add rule name="EndorVPN-Block-Out" dir=out action=block enable=yes profile=any description="EndorVPN kill-switch" 2>&1 | ForEach-Object { Say ("  " + $_) }
  $r = Get-NetFirewallRule -DisplayName "EndorVPN-Block-Out" -ErrorAction SilentlyContinue
  Say ("rule present: " + [bool]$r + " enabled=" + ($r.Enabled -join ','))
  Say ("ip now (should fail): " + (Get-Ip 'blocked'))
  & netsh advfirewall firewall delete rule name="EndorVPN-Block-Out" 2>&1 | ForEach-Object { Say ("  " + $_) }
  Say ("ip after restore: " + (Get-Ip 'restored'))
} catch { Say ("killswitch FAIL: " + $_.Exception.Message) }

Set-Content -Path "$out\probe3-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "== done =="
