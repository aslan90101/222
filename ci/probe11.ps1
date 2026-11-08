$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:lines=New-Object System.Collections.Generic.List[string]
function Say($m){ Write-Host $m; $script:lines.Add([string]$m) }
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work11"; New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== tcp reachability to vpnbook =="
foreach ($h in @('us16.vpnbook.com','de20.vpnbook.com')) {
  foreach ($port in @(443,80)) {
    $t = Test-NetConnection -ComputerName $h -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
    Say ("  " + $h + ":" + $port + " => " + $t)
  }
}
$r = Invoke-WebRequest 'https://www.vpnbook.com/api/openvpn?hostname=us16.vpnbook.com&protocol=tcp443' -UseBasicParsing -TimeoutSec 40
$txt = $r.Content; if ($txt -is [byte[]]) { $txt=[Text.Encoding]::UTF8.GetString($txt) }
[IO.File]::WriteAllText("$work\us16.ovpn", $txt)
Say ("cfg bytes=" + $txt.Length)
Say ("-- resolved ips --")
foreach ($h in @('us16.vpnbook.com','de20.vpnbook.com')) { try { (Resolve-DnsName $h -Type A) | Where-Object {$_.IPAddress} | ForEach-Object { Say ("   " + $h + " -> " + $_.IPAddress) } } catch { Say ("   dns fail " + $h) } }

Say "== openvpn setup =="
Invoke-WebRequest -Uri 'https://swupdate.openvpn.org/community/releases/OpenVPN-2.6.12-I001-amd64.msi' -OutFile "$work\ovpn.msi" -UseBasicParsing -TimeoutSec 300
$ex="$work\ovpnx"; Start-Process msiexec.exe -ArgumentList "/a `"$work\ovpn.msi`" /qn TARGETDIR=`"$ex`"" -Wait
$bin="$ex\OpenVPN\bin"
Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile "$work\wintun.zip" -UseBasicParsing -TimeoutSec 120
Expand-Archive -Path "$work\wintun.zip" -DestinationPath "$work\wintun" -Force
Copy-Item "$work\wintun\wintun\bin\amd64\wintun.dll" "$bin\wintun.dll" -Force
Set-Content "$work\auth.txt" -Value "vpnbook`neetk7pw" -Encoding ascii

Say "== attempt us16 tcp443 verbose =="
$log = "$work\us16.log"
$p = Start-Process "$bin\openvpn.exe" -ArgumentList @("--config","$work\us16.ovpn","--log",$log,"--verb","6","--windows-driver","wintun","--auth-user-pass","$work\auth.txt") -PassThru -WindowStyle Hidden -RedirectStandardOutput "$work\o.txt" -RedirectStandardError "$work\e.txt"
for ($i=0; $i -lt 20; $i++) { Start-Sleep -Seconds 1; if ($p.HasExited) { break } }
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Say ("exit=" + $p.HasExited + " code=" + $p.ExitCode)
Get-Content $log -Tail 60 -EA SilentlyContinue | ForEach-Object { Say ("  " + $_) }
Say "--- stderr ---"
Get-Content "$work\e.txt" -Tail 20 -EA SilentlyContinue | ForEach-Object { Say ("  " + $_) }
Set-Content -Path "$out\probe11-report.md" -Value (@($script:lines) -join "`n") -Encoding utf8
Say done
