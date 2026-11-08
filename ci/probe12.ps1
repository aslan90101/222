$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:lines=New-Object System.Collections.Generic.List[string]
function Say($m){ Write-Host $m; $script:lines.Add([string]$m) }
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work12"; New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== setup openvpn =="
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile "$work\ovpn.msi" -UseBasicParsing -TimeoutSec 300
$ex="$work\ovpnx"; Start-Process msiexec.exe -ArgumentList "/a `"$work\ovpn.msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin="$ex\OpenVPN\bin"
Say ("bin: " + $bin)
Say ("tapctl exists: " + (Test-Path "$bin\tapctl.exe"))
Say "== tap driver install (silent) =="
try {
  Invoke-WebRequest -Uri 'https://build.openvpn.net/downloads/releases/tap-windows-9.24.7-I601-Win10.exe' -OutFile "$work\tap.exe" -UseBasicParsing -TimeoutSec 300
  $p = Start-Process "$work\tap.exe" -ArgumentList "/S" -Wait -PassThru
  Say ("installer exit=" + $p.ExitCode)
} catch { Say ("tap dl fail: " + $_.Exception.Message) }
Start-Sleep -Seconds 3
Say "== try tapctl =="
$env:PATH = "$bin;" + $env:PATH
$p = Start-Process "$bin\tapctl.exe" -ArgumentList "create --name EndorVPN" -Wait -PassThru -NoNewWindow
Say ("tapctl create exit=" + $p.ExitCode)
$p = Start-Process "$bin\tapctl.exe" -ArgumentList "list" -Wait -PassThru -NoNewWindow
Say ("tapctl list exit=" + $p.ExitCode)
Get-NetAdapter | ForEach-Object { Say ("  adapter: " + $_.Name + " | " + $_.InterfaceDescription + " | " + $_.Status) }

Say "== wintun =="
Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
$p = Start-Process "$bin\tapctl.exe" -ArgumentList "create --name EndorVPN2 --hwid wintun" -Wait -PassThru -NoNewWindow
Say ("tapctl wintun hwid exit=" + $p.ExitCode)
Get-NetAdapter | ForEach-Object { Say ("  adapter2: " + $_.Name + " | " + $_.InterfaceDescription + " | " + $_.Status) }

Say "== vpnbook config =="
$r = Invoke-WebRequest 'https://www.vpnbook.com/api/openvpn?hostname=us16.vpnbook.com&protocol=tcp443' -UseBasicParsing -TimeoutSec 40
$txt = $r.Content; if ($txt -is [byte[]]) { $txt=[Text.Encoding]::UTF8.GetString($txt) }
[IO.File]::WriteAllText("$work\us16.ovpn", $txt)
Set-Content "$work\auth.txt" -Value "vpnbook`neetk7pw" -Encoding ascii
function Get-Ip { try { return (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 20).Content } catch { return "ERR " } }
Say ("ip before: " + (Get-Ip))

function Try-Conn($name, $extraArgs) {
  $log = "$work\$name.log"
  $args = @("--config","$work\us16.ovpn","--log",$log,"--verb","4","--auth-user-pass","$work\auth.txt","--connect-timeout","10","--connect-retry-max","1") + $extraArgs
  Say ("--- " + $name + " : " + ($args -join ' '))
  $p = Start-Process "$bin\openvpn.exe" -ArgumentList $args -PassThru -WindowStyle Hidden
  $ok = $false
  for ($i=0; $i -lt 30; $i++) { Start-Sleep -Seconds 1; if ($p.HasExited) { break }; $t = Get-Content $log -Raw -EA SilentlyContinue; if ($t -match 'Initialization Sequence Completed') { $ok=$true; break } }
  Say ("  => " + $ok + " (" + $i + "s) exit=" + $p.HasExited)
  if ($ok) {
    Say ("  ip after: " + (Get-Ip))
    try { $sw=[Diagnostics.Stopwatch]::StartNew(); $dl=Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=10000000' -UseBasicParsing -TimeoutSec 240; $sw.Stop(); Say ("  speed: " + [math]::Round(($dl.RawContentLength*8/1e6)/($sw.ElapsedMilliseconds/1000),1) + " Mbit/s") } catch { Say ("  speed FAIL: " + $_.Exception.Message) }
    Say ("  dns: " + ((Resolve-DnsName www.youtube.com -Type A -EA SilentlyContinue | Select-Object -First 2 | ForEach-Object { $_.IPAddress }) -join ','))
    Say ("  exit-ip-id: " + (try { (Invoke-WebRequest 'https://ifconfig.me/ip' -UseBasicParsing -TimeoutSec 20).Content } catch { 'ERR' }))
  } else {
    $t = Get-Content $log -Tail 8 -EA SilentlyContinue
    $t | ForEach-Object { Say ("   LOG " + $_) }
  }
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  Start-Sleep -Seconds 2
  return $ok
}
$a = Try-Conn "wintun" @("--windows-driver","wintun")
if (-not $a) { $b = Try-Conn "tap" @("--windows-driver","tap-windows6") }
if (-not $a -and -not $b) { $c = Try-Conn "default" @() }
Set-Content -Path "$out\probe12-report.md" -Value (@($script:lines) -join "`n") -Encoding utf8
Say done
