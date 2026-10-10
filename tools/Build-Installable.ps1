[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseModZipPath,
    [string]$ConfiguratorZipPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'OutfitRuntime.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$baseZip = (Resolve-Path -LiteralPath $BaseModZipPath).Path
$baseHash = (Get-FileHash -LiteralPath $baseZip -Algorithm SHA256).Hash
if ($baseHash -cne '75334C06BBBB066FD80A3CEFA9ACF916B6B5F886AA34608C3B2EA3CAAFCEA5D5') {
    throw 'Use the unchanged published v0.2.0 Mod ZIP; unknown compatibility payloads are refused.'
}
$version = 'v0.2.1'
$build = Join-Path $root ('reports\releases\' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Assert-UniversalPath $root $build | Out-Null
if (Test-Path -LiteralPath $build) { throw 'Preserve existing release build.' }
$package = Join-Path $build 'package'
$product = Join-Path $package "EndfieldJiggleEFMI-$version"
$runtime = Join-Path $product 'Mods\EndfieldJiggleEFMI'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$originalFiles = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$archive = [IO.Compression.ZipFile]::OpenRead($baseZip)
try {
    $marker = 'EndfieldJiggleEFMI-v0.2.0/Mods/EndfieldJiggleEFMI/'
    foreach ($entry in $archive.Entries) {
        if (!$entry.FullName.StartsWith($marker, [StringComparison]::Ordinal) -or $entry.FullName.EndsWith('/')) { continue }
        $relative = $entry.FullName.Substring($marker.Length)
        if ([IO.Path]::IsPathRooted($relative) -or @($relative -split '[/\\]' | Where-Object { $_ -in @('..','.') }).Count) {
            throw 'Unsafe path in base ZIP.'
        }
        $target = Assert-UniversalPath $runtime (Join-Path $runtime $relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        $originalFiles.Add($relative.Replace('\','/'), (Get-FileHash -LiteralPath $target).Hash)
    }
} finally { $archive.Dispose() }
if ($originalFiles.Count -ne 13) { throw 'Unexpected base Mod file set.' }
$encoding = [Text.UTF8Encoding]::new($false)
$bridge = Enable-NativeOutfitBridge `
    -RuntimeText ([IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))) `
    -PassesText ([IO.File]::ReadAllText((Join-Path $runtime 'Passes.ini'))) `
    -OutfitTemplate ([IO.File]::ReadAllText((Join-Path $root 'runtime\Outfits.ini')))
[IO.File]::WriteAllText((Join-Path $runtime 'EndfieldJiggle.ini'), $bridge.RuntimeText, $encoding)
[IO.File]::WriteAllText((Join-Path $runtime 'Passes.ini'), $bridge.PassesText, $encoding)
[IO.File]::WriteAllText((Join-Path $runtime 'Outfits.ini'), $bridge.OutfitsText, $encoding)
foreach ($relative in $originalFiles.Keys) {
    if ($relative -in @('EndfieldJiggle.ini','Passes.ini')) { continue }
    if ((Get-FileHash -LiteralPath (Join-Path $runtime $relative)).Hash -cne $originalFiles[$relative]) {
        throw "Compatibility payload changed: $relative"
    }
}
foreach ($pair in @(
    @('docs\INSTALL-zh-CN.md','INSTALL-zh-CN.md'),
    @('docs\OUTFIT-COMPATIBILITY.md','OUTFIT-COMPATIBILITY.md'),
    @('DISCLAIMER.md','DISCLAIMER.md'), @('LICENSE','LICENSE')
)) {
    Copy-Item -LiteralPath (Join-Path $root $pair[0]) -Destination (Join-Path $product $pair[1])
}
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtime -Recurse -File | ForEach-Object {
    [ordered]@{
        path = [IO.Path]::GetRelativePath($runtime, $_.FullName)
        sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash
        lastWriteTimeUtc = $_.LastWriteTimeUtc.ToString('o')
    }
})
$modZip = Join-Path $build "EndfieldJiggleEFMI-$version-win64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($package, $modZip, [IO.Compression.CompressionLevel]::Optimal, $false)
$outputs = [Collections.Generic.List[object]]::new()
$outputs.Add([ordered]@{ file=[IO.Path]::GetFileName($modZip); sha256=(Get-FileHash $modZip).Hash; bytes=(Get-Item $modZip).Length })
if ($ConfiguratorZipPath) {
    $configZip = (Resolve-Path -LiteralPath $ConfiguratorZipPath).Path
    $configDir = Join-Path $runtime 'Configurator'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    $configArchive = [IO.Compression.ZipFile]::OpenRead($configZip)
    try {
        $allowed = @('Configurator/EndfieldJiggleConfigurator.exe','DOTNET-LICENSE.txt',
            'DOTNET-THIRD-PARTY-NOTICES.txt','LICENSE','README-zh-CN.md','SHA256SUMS.txt')
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $configArchive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ($name.EndsWith('/')) { continue }
            if ($name -cnotin $allowed -or !$seen.Add($name)) { throw "Unexpected configurator entry: $name" }
            $target = Join-Path $configDir ([IO.Path]::GetFileName($name))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
        if ($seen.Count -ne $allowed.Count) { throw 'Incomplete configurator package.' }
    } finally { $configArchive.Dispose() }
    [IO.File]::WriteAllText((Join-Path $product 'Install.cmd'),
        "@echo off`r`nstart `"`" `"%~dp0Mods\EndfieldJiggleEFMI\Configurator\EndfieldJiggleConfigurator.exe`" --install`r`n",
        [Text.ASCIIEncoding]::new())
    $bundleZip = Join-Path $build "EndfieldJiggleEFMI-$version-with-configurator-win64.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $bundleZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $outputs.Add([ordered]@{ file=[IO.Path]::GetFileName($bundleZip); sha256=(Get-FileHash $bundleZip).Hash; bytes=(Get-Item $bundleZip).Length })
}
$checksum = @($outputs | ForEach-Object { "$($_.sha256.ToLowerInvariant()) *$($_.file)" }) -join "`n"
[IO.File]::WriteAllText((Join-Path $build "SHA256SUMS-$version.txt"), $checksum + "`n", $encoding)
$report = [ordered]@{
    schema=1; version=$version; created=(Get-Date).ToString('o')
    buildKind='native-and-outfit-regression-repair'
    baseModZipSha256=$baseHash; packageDirectory=$runtime; files=$runtimeFiles; archives=$outputs.ToArray()
    outfitBridgeIncluded=$true; shaderPayloadUnchanged=$true
    configuratorBundled=[bool]$ConfiguratorZipPath
    liveInstallationModified=$false; inGameVerified=$false
    qaqmParserRepairIncluded=$false
    genericQaqmRecoveryToolIncluded=[bool]$ConfiguratorZipPath
    genericInPlaceOutfitAdapterIncluded=[bool]$ConfiguratorZipPath
    installEntryPoint=$(if ($ConfiguratorZipPath) { 'Install.cmd' } else { $null })
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $build 'build-report.json') -Encoding utf8
Write-Output "RELEASE_BUILD=$build"
Write-Output "MOD_ZIP=$modZip"
if ($ConfiguratorZipPath) { Write-Output "BUNDLE_ZIP=$bundleZip" }
