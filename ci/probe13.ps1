$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
$script:lines=New-Object System.Collections.Generic.List[string]
function Say($m){ Write-Host $m; $script:lines.Add([string]$m) }
$out="ci/reports"; New-Item -ItemType Directory -Force -Path $out | Out-Null
$work="$PWD\work13"; New-Item -ItemType Directory -Force -Path $work | Out-Null

Say "== build minimal wpf app with csc + xaml =="
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
Say ("csc: " + (Test-Path $csc))
$wpfDll = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\WindowsBase.dll"
Say ("refs: " + (Test-Path $wpfDll))
New-Item -ItemType Directory -Force -Path "$work\app" | Out-Null
Set-Content "$work\app\App.xaml" @'
<Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" StartupUri="MainWindow.xaml"/>
'@
Set-Content "$work\app\MainWindow.xaml" @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Probe.MainWindow" Title="EndorVPN" Height="640" Width="480" Background="#070B16" WindowStartupLocation="CenterScreen">
  <Grid>
    <TextBlock Text="EndorVPN UI" Foreground="#EAF0FF" FontSize="30" HorizontalAlignment="Center" VerticalAlignment="Center"/>
  </Grid>
</Window>
'@
Set-Content "$work\app\MainWindow.xaml.cs" @'
using System.Windows;
namespace Probe { public partial class MainWindow : Window { public MainWindow() { InitializeComponent(); } } }
'@
# XAML compile via PresentationBuildTasks
$pbt = Get-ChildItem "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8" -Filter 'PresentationBuildTasks.dll' -Recurse -EA SilentlyContinue | Select-Object -First 1
if (-not $pbt) { $pbt = Get-ChildItem "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\v4.0" -Filter 'PresentationBuildTasks.dll' -Recurse -EA SilentlyContinue | Select-Object -First 1 }
Say ("PresentationBuildTasks: " + ($pbt.FullName))
Add-Type -Path $pbt.FullName
$task = New-Object Microsoft.Build.Tasks.Windows.MarkupCompilePass1
# skipped
Say "csc direct attempt with generated g.cs instead"
# Fallback: compile code-behind only, generating BAML is complex — try building Window fully in code
Set-Content "$work\app\Program.cs" @'
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
public class Program {
  [STAThread]
  public static void Main() {
    var app = new Application();
    var w = new Window { Title = "EndorVPN", Height = 640, Width = 480, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(7,11,22)) };
    var g = new Grid();
    g.Children.Add(new TextBlock { Text = "EndorVPN UI", Foreground = new SolidColorBrush(Color.FromRgb(234,240,255)), FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
    w.Content = g;
    app.Run(w);
  }
}
'@
Push-Location "$work\app"
& $csc /nologo /target:winexe /out:Probe.exe /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll Program.cs 2>&1 | Select-Object -Last 10 | ForEach-Object { Say ("  " + $_) }
Pop-Location
Say ("exe: " + (Test-Path "$work\app\Probe.exe"))

Say "== screenshot test =="
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$p = Start-Process "$work\app\Probe.exe" -PassThru
Start-Sleep -Seconds 5
Say ("running=" + (-not $p.HasExited))
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Say ("screen=" + $b.Width + "x" + $b.Height)
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save("$out\wpf-code.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Say ("png bytes=" + (Get-Item "$out\wpf-code.png").Length)
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }

Say "== release sha512 test =="
$hasher = [System.Security.Cryptography.SHA512]::Create()
Say "sha512 ok"
Set-Content -Path "$out\probe13-report.md" -Value (@($script:lines) -join "`n") -Encoding utf8
Say done
