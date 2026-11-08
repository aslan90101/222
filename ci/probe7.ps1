$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:log=@(); function Say($m){Write-Host $m; $script:log+=[string]$m}
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work7"; New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== vpnbook page links =="
$page = (Invoke-WebRequest -Uri 'https://www.vpnbook.com/freevpn/openvpn' -UseBasicParsing -TimeoutSec 60).Content
Set-Content "$work\page.html" -Value $page -Encoding utf8
Say ("bytes=" + $page.Length)
[regex]::Matches($page, 'https?://[^"''\s<>]+') | ForEach-Object { $_.Value } | Sort-Object -Unique | Where-Object { $_ -match 'ovpn|zip|download|account|conf' } | ForEach-Object { Say ("  url: " + $_) }
[regex]::Matches($page, '(?:src|href)="([^"]+\.js[^"]*)"') | ForEach-Object { Say ("  js: " + $_.Groups[1].Value) }

Say "== try guessed urls =="
$tried = @()
$cands = @(
 'https://www.vpnbook.com/freevpn/openvpn/us16/tcp443',
 'https://www.vpnbook.com/freevpn/openvpn/download/us16-tcp443',
 'https://www.vpnbook.com/free-openvpn-account/VPNBook.com-OpenVPN-US16.zip',
 'https://www.vpnbook.com/free-openvpn-account/VPNBook.com-OpenVPN-US1.zip',
 'https://www.vpnbook.com/freevpn/openvpn/us16.zip',
 'https://www.vpnbook.com/freevpn/openvpn/download?server=us16&proto=tcp443',
 'https://www.vpnbook.com/api/openvpn/download?server=us16&proto=tcp443'
)
foreach ($u in $cands) {
  try { $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 30 -MaximumRedirection 3; Say ("  " + $r.StatusCode + " " + $u + " len=" + $r.RawContentLength) } catch { Say ("  ERR " + ($_.Exception.Response.StatusCode.value__) + " " + $u) }
}
Say "== js inspect =="
$js = [regex]::Matches($page, '(?:src)="([^"]+\.js[^"]*)"') | ForEach-Object { $_.Groups[1].Value }
foreach ($j in $js) {
  $ju = if ($j.StartsWith('http')) { $j } else { 'https://www.vpnbook.com' + $j }
  try {
    $jc = (Invoke-WebRequest -Uri $ju -UseBasicParsing -TimeoutSec 30).Content
    Say ("js " + $ju + " bytes=" + $jc.Length)
    [regex]::Matches($jc, '["'']([^"''\s]*(?:ovpn|zip|download|api)[^"''\s]*)["'']') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique | Select-Object -First 25 | ForEach-Object { Say ("   js-url: " + $_) }
    [regex]::Matches($jc, '(fetch|axios\.get|\.open)\s*\(\s*[`"'']([^`"'']+)') | ForEach-Object { Say ("   js-call: " + $_.Groups[2].Value) }
  } catch { Say ("js FAIL " + $ju + " :: " + $_.Exception.Message) }
}
Set-Content -Path "$out\probe7-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "== done =="
