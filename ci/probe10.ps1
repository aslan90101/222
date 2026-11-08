$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:lines=New-Object System.Collections.Generic.List[string]
function Say($m){ Write-Host $m; $script:lines.Add([string]$m) }
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work10"; New-Item -ItemType Directory -Force -Path $work | Out-Null

$page = (Invoke-WebRequest -Uri 'https://www.vpnbook.com/freevpn/openvpn' -UseBasicParsing -TimeoutSec 60).Content
$user='vpnbook'
$pass='eetk7pw'
$m2 = [regex]::Match($page, 'Password[^`]{0,80}?`([^`]+)`'); if ($m2.Success) { $pass = $m2.Groups[1].Value }
Say ("cred: u=" + $user + " p=" + $pass)

Say "== download configs =="
$got = @()
foreach ($srv in @('us16.vpnbook.com','de20.vpnbook.com','fr200.vpnbook.com')) {
  foreach ($proto in @('tcp443','udp25000','tcp80','udp53')) {
    $u = "https://www.vpnbook.com/api/openvpn?hostname=$srv&protocol=$proto"
    try {
      $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 40
      $txt = $r.Content
      if ($txt -is [byte[]]) { $txt = [Text.Encoding]::UTF8.GetString($txt) }
      Say ("  OK " + $proto + " " + $srv + " bytes=" + $txt.Length)
      if ($txt.Length -gt 300) {
        $f = "$work\$($srv.Split('.')[0])-$proto.ovpn"
        [IO.File]::WriteAllText($f, $txt)
        $got += [pscustomobject]@{ server=$srv; proto=$proto; path=$f }
      }
    } catch { Say ("  ERR " + $proto + " " + $srv + " " + ($_.Exception.Message)) }
  }
}
Say ("configs=" + $got.Count)
if ($got.Count -gt 0) {
  Say "--- sample config ---"
  (Get-Content $got[0].path -Raw) -split "`n" | Select-Object -First 25 | ForEach-Object { Say ("   " + $_) }
}

Say "== openvpn =="
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile "$work\ovpn.msi" -UseBasicParsing -TimeoutSec 300
$ex="$work\ovpnx"; Start-Process msiexec.exe -ArgumentList "/a `"$work\ovpn.msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin="$ex\OpenVPN\bin"
Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
Set-Content "$work\auth.txt" -Value ($user + "`n" + $pass) -Encoding ascii
function Get-Ip { try { return (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 20).Content } catch { return "ERR" } }
Say ("ip before: " + (Get-Ip))

$best=$null
foreach ($c in $got) {
  $log = "$work\$([IO.Path]::GetFileName($c.path)).log"
  $args = @("--config", $c.path, "--log", $log, "--verb", "4", "--windows-driver", "wintun", "--auth-user-pass", "$work\auth.txt", "--connect-timeout", "10", "--connect-retry-max", "1")
  $p = Start-Process "$bin\openvpn.exe" -ArgumentList $args -PassThru -WindowStyle Hidden
  $ok=$false
  for ($i=0; $i -lt 25; $i++) { Start-Sleep -Seconds 1; if ($p.HasExited) { break }; if (Test-Path $log) { $t = Get-Content $log -Raw -EA SilentlyContinue; if ($t -match 'Initialization Sequence Completed') { $ok=$true; break } } }
  Say ("  " + $c.server + " " + $c.proto + " => " + $ok + " (" + $i + "s)")
  if ($ok) { $best=$c; break }
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  Start-Sleep -Milliseconds 500
  $t = Get-Content $log -Raw -EA SilentlyContinue
  [regex]::Matches($t, '(?m)^.*(AUTH_FAILED|TLS Error|Cannot open|FATAL|error|Connection reset|timeout).*$') | Select-Object -First 2 | ForEach-Object { Say ("     " + $_.Value.Trim()) }
}
if ($best) {
  Say ("WORKING " + $best.server + " " + $best.proto)
  Say ("ip after: " + (Get-Ip))
  try { $sw=[Diagnostics.Stopwatch]::StartNew(); $dl=Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=10000000' -UseBasicParsing -TimeoutSec 240; $sw.Stop(); Say ("speed: " + [math]::Round(($dl.RawContentLength*8/1e6)/($sw.ElapsedMilliseconds/1000),1) + " Mbit/s") } catch { Say ("speed FAIL: " + $_.Exception.Message) }
  Say ("adapters: " + ((Get-NetAdapter | ForEach-Object { $_.Name + ':' + $_.InterfaceDescription + ':' + $_.Status }) -join ' | '))
  Get-Process openvpn -EA SilentlyContinue | Stop-Process -Force
} else { Say "NO WORKING CONFIG" }
Set-Content -Path "$out\probe10-report.md" -Value (@($script:lines) -join "`n") -Encoding utf8
Say "done"
