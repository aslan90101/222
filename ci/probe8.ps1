$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:log=@(); function Say($m){Write-Host $m; $script:log+=[string]$m}
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work8"; New-Item -ItemType Directory -Force -Path $work | Out-Null
$page = (Invoke-WebRequest -Uri 'https://www.vpnbook.com/freevpn/openvpn' -UseBasicParsing -TimeoutSec 60).Content
$n = 0
foreach ($m in [regex]::Matches($page, 'src="([^"]+\.js[^"]*)"')) {
  $j = $m.Groups[1].Value
  $ju = if ($j.StartsWith('http')) { $j } else { 'https://www.vpnbook.com' + $j }
  try {
    $jc = (Invoke-WebRequest -Uri $ju -UseBasicParsing -TimeoutSec 30).Content
    $hits = [regex]::Matches($jc, '.{0,120}(?:ovpn|openvpn-account|api/[a-z-]+|/download).{0,200}') | ForEach-Object { $_.Value }
    if ($hits.Count -gt 0) {
      $n++
      Say ("=== " + $ju + " ===")
      $hits | Select-Object -First 6 | ForEach-Object { Say ("   " + $_) }
    }
  } catch {}
}
Say ("chunks with hits: " + $n)
Set-Content -Path "$out\probe8-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "done"
