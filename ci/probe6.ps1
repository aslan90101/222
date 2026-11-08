$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:log=@(); function Say($m){Write-Host $m; $script:log+=[string]$m}
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work6"; New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== dotnet =="
dotnet --version
dotnet --list-sdks
Say ("fw: " + (Get-ChildItem 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join ',')

Say "== vpnbook =="
$page = ""
try { $page = (Invoke-WebRequest -Uri 'https://www.vpnbook.com/freevpn' -UseBasicParsing -TimeoutSec 60).Content
  Say ("page bytes=" + $page.Length)
  [regex]::Matches($page, 'href="([^"]*(?:ovpn|zip))"') | ForEach-Object { Say ("  link: " + $_.Groups[1].Value) } | Select-Object -First 30
} catch { Say ("vpnbook page FAIL: " + $_.Exception.Message) }

Say "== download vpnbook configs =="
$cfgText = $null
foreach ($u in @(
  'https://www.vpnbook.com/free-openvpn-account/VPNBook.com-OpenVPN-US1.zip',
  'https://www.vpnbook.com/free-openvpn-account/VPNBook.com-OpenVPN-DE1.zip',
  'https://www.vpnbook.com/free-openvpn-account/VPNBook.com-OpenVPN-FR1.zip',
  'https://www.vpnbook.com/free-openvpn-account/VPNBook.com-OpenVPN-US2.zip'))
{
  try {
    $z = "$work\" + ($u -split '/' | Select-Object -Last 1)
    Invoke-WebRequest -Uri $u -OutFile $z -UseBasicParsing -TimeoutSec 120
    Expand-Archive -Path $z -DestinationPath "$work\x" -Force
    Say ("downloaded " + $u)
    Get-ChildItem "$work\x" | ForEach-Object { Say ("  " + $_.Name + " " + $_.Length) }
  } catch { Say ("dl FAIL " + $u + " :: " + $_.Exception.Message) }
}
$tcpCfg = Get-ChildItem "$work\x" -Filter '*tcp443*' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $tcpCfg) { $tcpCfg = Get-ChildItem "$work\x" -Filter '*.ovpn' -ErrorAction SilentlyContinue | Select-Object -First 1 }
if ($tcpCfg) {
  Say ("using cfg: " + $tcpCfg.Name)
  $cfgText = Get-Content $tcpCfg.FullName -Raw
  Say ("cfg has auth-user-pass: " + ($cfgText -match 'auth-user-pass'))
  Set-Content -Path "$out\vpnbook.ovpn" -Value $cfgText -Encoding ascii
} else { Say "no vpnbook cfg" }

Say "== openvpn setup =="
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile "$work\ovpn.msi" -UseBasicParsing -TimeoutSec 300
$ex="$work\ovpnx"; Start-Process msiexec.exe -ArgumentList "/a `"$work\ovpn.msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin="$ex\OpenVPN\bin"
Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
Say "ready"
function Get-Ip { try { return (Invoke-WebRequest -Uri 'http://ip-api.com/json/?fields=status,country,countryCode,city,query' -UseBasicParsing -TimeoutSec 20).Content } catch { return "ERR" } }
Say ("ip before: " + (Get-Ip))

if ($cfgText) {
  Set-Content -Path "$work\vb.ovpn" -Value $cfgText -Encoding ascii
  Set-Content -Path "$work\auth.txt" -Value "vpn`nvpn" -Encoding ascii
  $extra = @("--windows-driver","wintun","--auth-user-pass","$work\auth.txt","--connect-timeout","10","--connect-retry-max","1")
  $log="$work\vb.log"
  $p = Start-Process "$bin\openvpn.exe" -ArgumentList (@("--config","$work\vb.ovpn","--log",$log,"--verb","4") + $extra) -PassThru -WindowStyle Hidden
  $ok=$false
  for($i=0;$i -lt 30;$i++){ Start-Sleep -Seconds 1; if($p.HasExited){break}; if(Test-Path $log){ $t=Get-Content $log -Raw -EA SilentlyContinue; if($t -match 'Initialization Sequence Completed'){$ok=$true;break} } }
  Say ("vpnbook connected=" + $ok + " in " + $i + "s")
  if ($ok) {
    Say ("ip after: " + (Get-Ip))
    try { $sw=[Diagnostics.Stopwatch]::StartNew(); $dl=Invoke-WebRequest -Uri 'https://speed.cloudflare.com/__down?bytes=10000000' -UseBasicParsing -TimeoutSec 240; $sw.Stop(); Say ("speed: " + [math]::Round(($dl.RawContentLength*8/1e6)/($sw.ElapsedMilliseconds/1000),1) + " Mbit/s") } catch { Say ("speed FAIL: " + $_.Exception.Message) }
    try { Say ("httpbin: " + (Invoke-WebRequest -Uri 'https://httpbin.org/ip' -UseBasicParsing -TimeoutSec 30).Content) } catch { Say ("httpbin FAIL") }
  }
  Get-Content $log -Tail 20 -EA SilentlyContinue | ForEach-Object { Say ("  LOG " + $_) }
  if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force }
}
Set-Content -Path "$out\probe6-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "== done =="
