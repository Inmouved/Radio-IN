param(
    [string]$JdkPath = 'C:\Program Files\Java\jdk-21',
    [string]$SdkPath = (Join-Path $PSScriptRoot '../../work/android-tools/sdk'),
    [string]$GradlePath = (Join-Path $PSScriptRoot '../../work/android-tools/gradle-8.13/bin/gradle.bat'),
    [string]$KeyStore = (Join-Path $PSScriptRoot '../../private/RadioIN-Android-signature/radioin-beta.jks'),
    [string]$PasswordFile = (Join-Path $PSScriptRoot '../../private/RadioIN-Android-signature/store-password.txt')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $KeyStore)) { throw 'Clé de signature absente. Restaurez la clé originale pour conserver la compatibilité des mises à jour.' }
$env:JAVA_HOME = (Resolve-Path -LiteralPath $JdkPath).Path
$env:ANDROID_HOME = (Resolve-Path -LiteralPath $SdkPath).Path
$env:RADIOIN_KEYSTORE = (Resolve-Path -LiteralPath $KeyStore).Path
$env:RADIOIN_STORE_PASSWORD = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $PasswordFile).Path).Trim()
try {
    & $GradlePath -p $PSScriptRoot :app:assembleRelease :app:testDebugUnitTest :app:lintRelease
    if ($LASTEXITCODE -ne 0) { throw "Échec de compilation ou de vérification : $LASTEXITCODE" }
    $output = Join-Path $PSScriptRoot '../RadioIN-Beta-1-Android.apk'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app/build/outputs/apk/release/app-release.apk') -Destination $output -Force
    & (Join-Path $SdkPath 'build-tools/36.0.0/apksigner.bat') verify --verbose $output
    if ($LASTEXITCODE -ne 0) { throw 'Signature APK invalide.' }
    Get-Item -LiteralPath $output | Select-Object FullName,Length
} finally { Remove-Item Env:RADIOIN_STORE_PASSWORD -ErrorAction SilentlyContinue }
