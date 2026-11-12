$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
$buildDir = Join-Path $sourceDir '../../work/lite-build'
$outDir = Join-Path $sourceDir '../RadioIN-Beta-1.3'
New-Item -ItemType Directory -Force -Path $buildDir,$outDir | Out-Null
$xaml = Get-Content (Join-Path $sourceDir 'MainWindow.xaml') -Raw -Encoding UTF8
$xml = [xml]$xaml
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$fields = @()
$assignments = @()
foreach ($node in $xml.SelectNodes('//*[@x:Name]', $ns)) {
    $name = $node.GetAttribute('Name','http://schemas.microsoft.com/winfx/2006/xaml')
    $fields += "private $($node.LocalName) $name;"
    $assignments += "$name = ($($node.LocalName))root.FindName(`"$name`");"
}
$xaml = $xaml -replace ' x:Class="[^"]+"','' -replace ' (Click|Checked|Unchecked|ValueChanged|KeyDown)="[^"]+"',''
[IO.File]::WriteAllText((Join-Path $buildDir 'Player.xaml'), $xaml)
$skin = (Get-Content (Join-Path $sourceDir 'App.xaml') -Raw -Encoding UTF8) -replace ' x:Class="[^"]+"','' -replace ' StartupUri="[^"]+"',''
[IO.File]::WriteAllText((Join-Path $buildDir 'Skin.xaml'), $skin)
$initialization = @'
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace RadioIN {
public partial class MainWindow {
FIELDS
void InitializeComponent() {
var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Player.xaml");
var root = (Window)System.Windows.Markup.XamlReader.Load(stream);
ASSIGNMENTS
Title=root.Title; Width=root.Width; SizeToContent=root.SizeToContent; ResizeMode=root.ResizeMode;
Background=root.Background; FontFamily=root.FontFamily; FontSize=root.FontSize; WindowStartupLocation=root.WindowStartupLocation;
var content=root.Content; root.Content=null; Content=content;
Play.Click += TogglePlay; Volume.ValueChanged += VolumeChanged; Pin.Checked += PinChanged; Pin.Unchecked += PinChanged;
HistoryButton.Click += ShowHistory; RequestButton.Click += ShowRequests; Search.KeyDown += SearchKey;
Send.Click += SendRequest; Previous.Click += PrevPage; Next.Click += NextPage;
foreach (var button in FindButtons((DependencyObject)Content)) {
 if ((string)button.Content == "Rechercher") button.Click += SearchClick;
 if ((string)button.Content == "Fermer") button.Click += CloseDrawer;
}
}
static System.Collections.Generic.IEnumerable<Button> FindButtons(DependencyObject parent) {
foreach(var item in LogicalTreeHelper.GetChildren(parent)) { var d=item as DependencyObject; if(d==null)continue; if(d is Button) yield return (Button)d; foreach(var b in FindButtons(d))yield return b; }
}
}
static class Entry {
[System.STAThread] static void Main() {
PublicRuntime.Run();
}
}
}
'@
$initialization = $initialization.Replace('FIELDS',($fields -join "`n")).Replace('ASSIGNMENTS',($assignments -join "`n"))
[IO.File]::WriteAllText((Join-Path $buildDir 'Init.cs'), $initialization)
$main = Get-Content (Join-Path $sourceDir 'MainWindow.xaml.cs') -Raw -Encoding UTF8
$main = $main.Replace('record Track(string Title, string Detail, string Url);','public sealed class Track { public string Title {get;set;} public string Detail {get;set;} public string Url {get;set;} public Track(string title,string detail,string url) {Title=title;Detail=detail;Url=url;} }')
[IO.File]::WriteAllText((Join-Path $buildDir 'Main.cs'), $main)
[IO.File]::WriteAllText((Join-Path $buildDir 'Global.cs'), "global using System;`nglobal using System.Linq;`nglobal using System.Collections.Generic;`nglobal using System.Threading.Tasks;")
$fx = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319'
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $fx $_) }
& dotnet 'C:/Program Files/dotnet/sdk/8.0.426/Roslyn/bincore/csc.dll' /nologo /target:winexe /platform:x64 /langversion:latest /nullable:enable /define:PORTABLE_HELPER /optimize+ "/out:$buildDir/RadioIN-Updater.exe" $refs "$sourceDir/PortableUpdate.cs"
if ($LASTEXITCODE -ne 0) { throw 'Updater compilation failed' }
& dotnet 'C:/Program Files/dotnet/sdk/8.0.426/Roslyn/bincore/csc.dll' /nologo /target:winexe /platform:x64 /langversion:latest /nullable:enable /nowarn:8618,8602 /optimize+ "/out:$outDir/RadioIN.exe" "/win32icon:$sourceDir/assets/app.ico" "/resource:$sourceDir/assets/app.ico,RadioIN.icon.ico" "/resource:$sourceDir/assets/app.png,RadioIN.icon.png" "/resource:$buildDir/Player.xaml,Player.xaml" "/resource:$buildDir/Skin.xaml,Skin.xaml" "/resource:$buildDir/RadioIN-Updater.exe,RadioIN.updater.exe" $refs "$buildDir/Init.cs" "$buildDir/Main.cs" "$buildDir/Global.cs" "$sourceDir/NativeAudio.cs" "$sourceDir/FrameworkJson.cs" "$sourceDir/Features.cs" "$sourceDir/Visuals.cs" "$sourceDir/PublicRuntime.cs" "$sourceDir/PublicFeatures.cs" "$sourceDir/VersionInfo.cs" "$sourceDir/Updater.cs" "$sourceDir/UpdaterTests.cs" "$sourceDir/PortableUpdate.cs" "$sourceDir/BetaFeatures.cs" "$sourceDir/BetaTests.cs" "$sourceDir/Upcoming.cs" "$sourceDir/Release13.cs" "$sourceDir/Release13Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
$vlc = 'C:/Program Files/VideoLAN/VLC'
Copy-Item "$vlc/libvlc.dll","$vlc/libvlccore.dll" -Destination $outDir
$modules = @{
 access=@('libhttp_plugin.dll','libhttps_plugin.dll','libtcp_plugin.dll');
 demux=@('libes_plugin.dll');
 codec=@('libmpg123_plugin.dll');
 packetizer=@('libpacketizer_mpegaudio_plugin.dll');
 audio_output=@('libmmdevice_plugin.dll','libwasapi_plugin.dll','libdirectsound_plugin.dll');
 audio_filter=@('libaudio_format_plugin.dll','libtrivial_channel_mixer_plugin.dll','libsimple_channel_mixer_plugin.dll','libugly_resampler_plugin.dll','libspeex_resampler_plugin.dll');
 audio_mixer=@('libfloat_mixer_plugin.dll','libinteger_mixer_plugin.dll');
 stream_filter=@('libprefetch_plugin.dll','libcache_read_plugin.dll','libskiptags_plugin.dll');
 misc=@('libgnutls_plugin.dll');
}
foreach($category in $modules.Keys) {
 $target = Join-Path $outDir "plugins/$category"
 New-Item -ItemType Directory -Force $target | Out-Null
 foreach($module in $modules[$category]) { Copy-Item "$vlc/plugins/$category/$module" -Destination $target }
}
Copy-Item "$vlc/COPYING.txt" -Destination "$outDir/VLC-COPYING.txt"

Copy-Item (Join-Path $sourceDir 'App.config') -Destination "$outDir/RadioIN.exe.config"
Copy-Item (Join-Path $sourceDir 'installer/LIRE-MOI-INSTALLATION.txt') -Destination "$outDir/LIRE-MOI.txt"
