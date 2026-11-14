param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $PSScriptRoot '../../work/tools/inno/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 7\ISCC.exe'
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CompilerPath) { throw 'Compilateur Inno Setup 6.7.3 ou supérieur requis. Utilisez -CompilerPath.' }
& $CompilerPath (Join-Path $PSScriptRoot 'installer/RadioIN.iss')
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué : $LASTEXITCODE" }
& (Join-Path $PSScriptRoot 'installer/Normalize-ProductName.ps1') -InstallerPath (Join-Path $PSScriptRoot '../RadioIN-Beta-1.4.1-Setup.exe')
Get-Item (Join-Path $PSScriptRoot '../RadioIN-Beta-1.4.1-Setup.exe') | Select-Object FullName,Length
