$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:log=@(); function Say($m){Write-Host $m; $script:log+=[string]$m}
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
Say "== build wpf with dotnet sdk =="
Push-Location ci/wpfprobe
& dotnet build -c Release -v m 2>&1 | Select-Object -Last 12 | ForEach-Object { Say ("  " + $_) }
Say ("exe exists: " + (Test-Path "bin/Release/net48/WpfProbe.exe"))
Pop-Location
Say "== launch and screenshot =="
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$exe = (Resolve-Path "ci/wpfprobe/bin/Release/net48/WpfProbe.exe").Path
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 6
Say ("running: " + (-not $p.HasExited))
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Say ("screen: " + $bounds.Width + "x" + $bounds.Height)
$bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bmp.Save("$out\wpf-screen.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Say ("screenshot bytes: " + (Get-Item "$out\wpf-screen.png").Length)
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Say "== also test window-scoped capture =="
$p2 = Start-Process $exe -PassThru
Start-Sleep -Seconds 5
$bmp2 = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bmp2.Save("$out\wpf-screen2.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g2.Dispose(); $bmp2.Dispose()
if (-not $p2.HasExited) { Stop-Process -Id $p2.Id -Force }
Set-Content -Path "$out\probe-wpf-report.md" -Value ($script:log -join "`n") -Encoding utf8
Say "done"
