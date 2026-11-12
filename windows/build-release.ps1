$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-lite.ps1')
& (Join-Path $PSScriptRoot 'build-installer.ps1')
$release = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../RadioIN-Beta-1.3'))
$zip = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../RadioIN-Beta-1.3-Windows.zip'))
$files = @('RadioIN.exe','RadioIN.exe.config','libvlc.dll','libvlccore.dll','VLC-COPYING.txt','LIRE-MOI.txt','plugins') | ForEach-Object { Join-Path $release $_ }
Compress-Archive -LiteralPath $files -DestinationPath $zip -CompressionLevel Optimal -Force
foreach ($path in @($zip, [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../RadioIN-Beta-1.3-Setup.exe')))) {
    $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($path + '.sha256'), ($digest + '  ' + [IO.Path]::GetFileName($path) + "`r`n"))
    Get-Item -LiteralPath $path | Select-Object FullName,Length
}
