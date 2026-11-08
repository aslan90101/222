$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:log=@(); function Say($m){Write-Host $m; $script:log+=[string]$m}
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work9"; New-Item -ItemType Directory -Force -Path $work | Out-Null

$page = (Invoke-WebRequest -Uri 'https://www.vpnbook.com/freevpn/openvpn' -UseBasicParsing -TimeoutSec 60).Content
$user = [regex]::Match($page, 'Username\s*:?\s*</?[^>]*>?\s*<?[^>]*>?\s*`?([a-z0-9]+)`?').Groups[1].Value
$pass = [regex]::Match($page, 'Password\s*:?\s*</?[^>]*>?\s*<?[^>]*>?\s*`?([A-Za-z0-9]+)`?').Groups[1].Value
Say ("cred guess: u=" + $user + " p=" + $pass)
$m1 = [regex]::Match($page, 'Username[^`]{0,80}?`([^`]+)`'); if ($m1.Success) { $user = $m1.Groups[1].Value }
$m2 = [regex]::Match($page, 'Password[^`]{0,80}?`([^`]+)`'); if ($m2.Success) { $pass = $m2.Groups[1].Value }
Say ("cred final: u=" + $user + " p=" + $pass)

Say "== download configs =="
$got = @()
foreach ($srv in @('us16.vpnbook.com','de20.vpnbook.com','fr200.vpnbook.com','uk205.vpnbook.com','ca149.vpnbook.com')) {
  foreach ($proto in @('tcp443','tcp80','udp53','udp25000')) {
    $u = "https://www.vpnbook.com/api/openvpn?hostname=$srv&protocol=$proto"
    try {
      $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 40
      $len = $r.Content.Length
      Say ("  OK " + $proto + " " + $srv + " bytes=" + $len + " type=" + $r.Headers['Content-Type'])
      if ($len -gt 300) {
        $f = "$work\$($srv.Split('.')[0])-$proto.ovpn"
        [IO.File]::WriteAllText($f, $r.Content)
        $got += [pscustomobject]@{ server=$srv; proto=$proto; path=$f; size=$len }
      }
    } catch { Say ("  ERR " + $proto + " " + $srv + " " + ($_.Exception.Response.StatusCode.value__)) }
  }
}
Say ("configs: " + $got.Count)
if ($got.Count -gt 0) {
  $c = $got[0]
  Say ("--- sample " + $c.server + " " + $c.proto + " ---")
  (Get-Content $c.path -Raw) -split "`n" | Where-Object { $_ -match '^(remote|proto|port|dev|auth|data-ciphers|cipher|route)' } | ForEach-Object { Say ("   " + $_.Trim()) }
  Copy-Item $c.path "$out\vpnbook-sample.ovpn" -Force
}

Say "== openvpn setup + connect =="
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile "$work\ovpn.msi" -UseBasicParsing -TimeoutSec 300
$ex="$work\ovpnx"; Start-Process msiexec.exe -ArgumentList "/a `"$work\ovpn.msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin="$ex\OpenVPN\bin"
Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
Set-Content "$work\auth.txt" -Value ($user + "`n" + $pass) -Encoding ascii
function Get-Ip { try { return (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 20).Content } catch { return "ERR" } }
Say ("ip before: " + (Get-Ip))

$best = $null
foreach ($c in $got) {
  $log = "$work\$(Split-Path $c.path -Leaf).log"
  $args = @("--config", $c.path, "--log", $log, "--verb", "4", "--windows-driver", "wintun", "--auth-user-pass", "$work\auth.txt", "--connect-timeout", "12", "--connect-retry-max", "1")
  $p = Start-Process "$bin\openvpn.exe" -ArgumentList $args -PassThru -WindowStyle Hidden
  $ok = $false
  for ($i=0; $i -lt 25; $i++) { Start-Sleep -Seconds 1; if ($p.HasExited) { break }; if (Test-Path $log) { $t = Get-Content $log -Raw -EA SilentlyContinue; if ($t -match 'Initialization Sequence Completed') { $ok=$true; break } } }
  Say ("  " + $c.server + " " + $c.proto + " => " + $ok + " (" + $i + "s)")
  if ($ok) { $best = $c; break }
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $t = Get-Content $log -Raw -EA SilentlyContinue
  $err = [regex]::Match($t, '(?m)^.*(AUTH_FAILED|TLS Error|Cannot open|FATAL|error).*$')
  if ($err.Success) { Say ("     " + $err.Value.Trim()) }
}
if ($best) {
  Say ("WORKING: " + $best.server + " " + $best.proto)
  Say ("ip after: " + (Get-Ip))
  try { $sw=[Diagnostics.Stopwatch]::StartNew(); $dl=Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=10000000' -UseBasicParsing -TimeoutSec 240; $sw.Stop(); Say ("speed: " + [math]::Round(($dl.RawContentLength*8/1e6)/($sw.ElapsedMilliseconds/1000),1) + " Mbit/s") } catch { Say ("speed FAIL: " + $_.Exception.Message) }
  Say ("route: " + ((Get-NetRoute -DestinationPrefix '0.0.0.0/0' | ForEach-Object { $_.NextHop + '@' + $_.InterfaceAlias }) -join ' | '))
  Say ("adapters: " + ((Get-NetAdapter | ForEach-Object { $_.Name + ':' + $_.InterfaceDescription + ':' + $_.Status }) -join ' | '))
  Say ("dns: " + ((Get-DnsClientServerAddress -AddressFamily IPv4 | ForEach-Object { $_.InterfaceAlias + '=' + ($_.ServerAddresses -join ',') }) -join ' | '))
  try { Say ("dnscheck: " + (Resolve-DnsName www.google.com -Type A -ErrorAction Stop | Select-Object -First 2 | ForEach-Object { $_.IPAddress }) -join ',') } catch { Say ("dns fail: " + $_.Exception.Message) }
  Get-Process openvpn -EA SilentlyContinue | Stop-Process -Force
} else { Say "no working config" }
Set-Content -Path "$out\probe9-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "done"
