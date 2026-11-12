param([Parameter(Mandatory=$true)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($installer)
if ($info.ProductName -eq 'Radio IN') { return }
if ($info.ProductName.TrimEnd() -cne 'Radio IN') { throw 'Nom de produit inattendu.' }
if ((Get-AuthenticodeSignature -LiteralPath $installer).SignerCertificate) { throw 'Normaliser les métadonnées AVANT la signature Authenticode.' }

# Inno reserves a fixed-width string in its loader's VERSIONINFO resource.
# Older Radio IN clients compare this string without Trim(). Replace only its
# trailing UTF-16 spaces with NUL padding, in place. No PE section, executable
# code, compressed payload, overlay offset or file size is changed.
$bytes = [IO.File]::ReadAllBytes($installer)
$pe = [BitConverter]::ToInt32($bytes, 60)
if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550) { throw 'En-tête PE invalide.' }
$sections = [BitConverter]::ToUInt16($bytes, $pe + 6)
$sectionTable = $pe + 24 + [BitConverter]::ToUInt16($bytes, $pe + 20)
$resourceStart = 0; $resourceEnd = 0
for ($i=0; $i -lt $sections; $i++) {
    $entry = $sectionTable + 40 * $i
    $name = [Text.Encoding]::ASCII.GetString($bytes, $entry, 8).TrimEnd([char]0)
    if ($name -eq '.rsrc') {
        $resourceStart = [BitConverter]::ToInt32($bytes, $entry + 20)
        $resourceEnd = $resourceStart + [BitConverter]::ToInt32($bytes, $entry + 16)
    }
}
if ($resourceStart -le 0 -or $resourceEnd -gt $bytes.Length) { throw 'Section de ressources invalide.' }
$pattern = [Text.Encoding]::Unicode.GetBytes($info.ProductName + [char]0)
$keyPattern = [Text.Encoding]::Unicode.GetBytes('ProductName' + [char]0)
$matchesFound = [Collections.Generic.List[int]]::new()
for ($offset=$resourceStart; $offset -le $resourceEnd-$keyPattern.Length; $offset++) {
    if ($bytes[$offset] -ne $keyPattern[0]) { continue }
    $same = $true
    for ($j=1; $j -lt $keyPattern.Length; $j++) { if ($bytes[$offset+$j] -ne $keyPattern[$j]) { $same=$false; break } }
    if (-not $same) { continue }
    $value = ($offset + $keyPattern.Length + 3) -band -4
    if ($value + $pattern.Length -gt $resourceEnd) { continue }
    for ($j=0; $j -lt $pattern.Length; $j++) { if ($bytes[$value+$j] -ne $pattern[$j]) { $same=$false; break } }
    if ($same) { $matchesFound.Add($value) }
}
if ($matchesFound.Count -ne 1) { throw 'Chaîne de produit non unique dans les ressources.' }
$valueOffset = $matchesFound[0]
for ($i=('Radio IN'.Length * 2); $i -lt $pattern.Length; $i++) { $bytes[$valueOffset + $i] = 0 }
[IO.File]::WriteAllBytes($installer, $bytes)
$verified = [Diagnostics.FileVersionInfo]::GetVersionInfo($installer)
if ($verified.ProductName -cne 'Radio IN' -or $verified.FileVersion -ne $info.FileVersion) { throw 'Vérification des métadonnées normalisées échouée.' }
