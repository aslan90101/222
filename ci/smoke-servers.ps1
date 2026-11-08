# Extracts a handful of real VPN Gate entries into a small CSV for the offline smoke test.
try {
  $r = Invoke-WebRequest -Uri 'https://www.vpngate.net/api/iphone/' -UseBasicParsing -TimeoutSec 60
  $txt = $r.Content
  $start = $txt.IndexOf('*vpn_servers')
  if ($start -gt 0) { $txt = $txt.Substring($start) }
  $lines = $txt -split "`r?`n"
  $keep = New-Object System.Collections.Generic.List[string]
  $keep.Add('*vpn_servers')
  $keep.Add('#HostName,IP,Score,Ping,Speed,CountryLong,CountryShort,NumVpnSessions,Uptime,TotalUsers,TotalTraffic,LogType,Operator,Message,OpenVPN_ConfigData_Base64')
  $n = 0
  foreach ($l in $lines) {
    if ($n -ge 24) { break }
    if (-not $l -or $l.StartsWith('*') -or $l.StartsWith('#')) { continue }
    $f = $l.Split(',')
    if ($f.Count -lt 15) { continue }
    if ($f[$f.Count-1].Length -lt 500) { continue }
    $keep.Add($l)
    $n++
  }
  $keep.Add('*')
  Set-Content -Path 'engine/EndorVPN/smoke-servers.csv' -Value ($keep -join "`n") -Encoding ascii
  Write-Host "smoke csv servers: $n"
} catch {
  Write-Host "smoke csv fetch failed: $($_.Exception.Message)"
}
