$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$out = "ci/reports"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$script:log = @()
function Say($m) { Write-Host $m; $script:log += [string]$m }
$work = "$PWD\work5"
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Num($v) { if ($v -match '^\d+$') { return [int64]$v } else { return 0 } }

Say "== fetch vpngate =="
$servers = @()
$raw = $null
foreach ($u in @('https://www.vpngate.net/api/iphone/','https://raw.githubusercontent.com/rainsstory/vpngate-mirror/main/api/iphone/index.html')) {
  try {
    $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 60
    $raw = $r.Content
    Say ("fetched " + $u + " bytes=" + $raw.Length)
    break
  } catch { Say ("fetch FAIL " + $u + " :: " + $_.Exception.Message) }
}
$files = @()
if ($raw) {
  foreach ($l in ($raw -split "`r?`n")) {
    if (-not $l -or $l.StartsWith('*') -or $l.StartsWith('#')) { continue }
    $f = $l.Split(',')
    if ($f.Count -lt 15) { continue }
    $cfgB64 = $f[$f.Count-1]
    if ($cfgB64.Length -lt 200) { continue }
    $cfg = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cfgB64))
    $files += [pscustomobject]@{ host=$f[0]; ip=$f[1]; score=(Num $f[2]); ping=(Num $f[3]); speed=(Num $f[4]); country=$f[5]; cc=$f[6]; cfg=$cfg }
  }
}
Say ("parsed=" + $files.Count)
if ($files.Count -gt 0) {
  $g = $files | Group-Object cc | Sort-Object Name
  Say ("countries=" + $g.Count + " :: " + (($g | ForEach-Object { $_.Name + '(' + $_.Count + ')' }) -join ' '))
  # protocol stats
  $proto = @{}
  foreach ($s in $files) {
    $m = [regex]::Match($s.cfg, '(?m)^proto\s+(\S+)')
    $rm = [regex]::Match($s.cfg, '(?m)^remote\s+(\S+)\s+(\d+)')
    $key = $(if ($m.Success) { $m.Groups[1].Value } else { '?' }) + ':' + $(if ($rm.Success) { $rm.Groups[2].Value } else { '?' })
    if ($proto.ContainsKey($key)) { $proto[$key]++ } else { $proto[$key] = 1 }
  }
  Say ("proto:port -> " + (($proto.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join ' '))
}

Say "== setup openvpn =="
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile "$work\ovpn.msi" -UseBasicParsing -TimeoutSec 300
$ex = "$work\ovpnx"
Start-Process msiexec.exe -ArgumentList "/a `"$work\ovpn.msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin = "$ex\OpenVPN\bin"
Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
Say "openvpn+ wintun ready"

function Get-Ip($tag) {
  try { return (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 20).Content } catch { return ("ERR") }
}
Say ("ip before: " + (Get-Ip 'before'))

function Kill-OpenVpn {
  Get-Process openvpn -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 2
}

function Try-Cfg($name, $cfgText, $extra, $waitSec) {
  Kill-OpenVpn
  $cfgPath = "$work\cfg-$name.ovpn"
  Set-Content -Path $cfgPath -Value $cfgText -Encoding ascii
  $log = "$work\log-$name.txt"
  Remove-Item $log -ErrorAction SilentlyContinue
  $args = @("--config", $cfgPath, "--log", $log, "--verb", "4") + $extra
  $proc = Start-Process "$bin\openvpn.exe" -ArgumentList $args -PassThru -WindowStyle Hidden
  $ok = $false
  for ($i = 0; $i -lt $waitSec; $i++) {
    Start-Sleep -Seconds 1
    if ($proc.HasExited) { break }
    if (Test-Path $log) {
      $t = Get-Content $log -Raw -ErrorAction SilentlyContinue
      if ($t -match 'Initialization Sequence Completed') { $ok = $true; break }
    }
  }
  $reason = ""
  if (-not $ok -and (Test-Path $log)) {
    $t = Get-Content $log -Raw
    $m = [regex]::Match($t, '(?m)^.*(FATAL|ERROR|error|Connection reset|TLS Error|timeout).*$')
    if ($m.Success) { $reason = $m.Value.Trim() }
  }
  Say ("  " + $name + " => connected=" + $ok + " (" + $i + "s) " + $reason)
  return $ok
}

$working = $null
if ($files.Count -gt 0) {
  $cands = $files | Sort-Object @{e={$_.ping}}, @{e={-$_.speed}} | Select-Object -First 12
  Say "== try candidates =="
  foreach ($c in $cands) {
    if ($working) { break }
    $remote = [regex]::Match($c.cfg, '(?m)^remote\s+(\S+)\s+(\d+)')
    $hostPort = $remote.Groups[1].Value + ':' + $remote.Groups[2].Value
    $proto = [regex]::Match($c.cfg, '(?m)^proto\s+(\S+)').Groups[1].Value
    Say ("candidate " + $c.host + " " + $c.cc + " " + $proto + " " + $hostPort + " ping=" + $c.ping)
    # force tcp 443 variant first (most compatible), then udp 443, then original
    $base = ($c.cfg -split "(?m)^remote ")[0].TrimEnd()
    $rest = ""
    if (($c.cfg -split "(?m)^remote ").Count -gt 1) { $rest = ((($c.cfg -split "(?m)^remote ")[1] -split "`n",2)[1]) }
    $bodies = @(
      @{ n='tcp443'; p='proto tcp' + "`n" + 'remote ' + $hostPort.Split(':')[0] + ' 443' },
      @{ n='udp443'; p='proto udp' + "`n" + 'remote ' + $hostPort.Split(':')[0] + ' 443' },
      @{ n='udp1194'; p='proto udp' + "`n" + 'remote ' + $hostPort.Split(':')[0] + ' 1194' },
      @{ n='orig'; p=$null }
    )
    foreach ($b in $bodies) {
      $cfgText = if ($b.p) { $base + "`n" + $b.p + "`n" + $rest } else { $c.cfg }
      if (Try-Cfg ($c.host + '-' + $b.n) $cfgText @("--windows-driver","wintun","--connect-timeout","8","--connect-retry-max","1") 25) {
        $working = @{ server=$c; name=$b.n; cfg=$cfgText }
        break
      }
    }
  }
}

if ($working) {
  Say ("== WORKING: " + $working.server.host + " (" + $working.server.cc + ") via " + $working.name + " ==")
  Say ("ip after: " + (Get-Ip 'after'))
  Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { Say ("  iface: " + $_.Name + " | " + $_.InterfaceDescription) }
  try {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $dl = Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=10000000' -UseBasicParsing -TimeoutSec 240
    $sw.Stop()
    Say ("download: " + [math]::Round(($dl.RawContentLength*8/1e6)/($sw.ElapsedMilliseconds/1000),1) + " Mbit/s")
  } catch { Say ("speed FAIL: " + $_.Exception.Message) }
  try {
    $r = Invoke-WebRequest -Uri 'https://www.youtube.com/' -UseBasicParsing -TimeoutSec 30
    Say ("youtube: " + $r.StatusCode)
  } catch { Say ("youtube FAIL: " + $_.Exception.Message) }
  # save working config for reference (no secrets beyond vpngate public cfg)
  Set-Content -Path "$out\working.ovpn" -Value $working.cfg -Encoding ascii
} else {
  Say "== no working server found =="
}

Kill-OpenVpn
Say "== openvpn log dump (last working) =="
Get-ChildItem "$work\log-*.txt" -ErrorAction SilentlyContinue | Select-Object -First 2 | ForEach-Object {
  Say ("--- " + $_.Name + " ---")
  Get-Content $_.FullName -Tail 30 | ForEach-Object { Say ("  " + $_) }
}
Set-Content -Path "$out\probe5-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "== done =="
