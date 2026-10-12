[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DropInZipPath,
    [Parameter(Mandatory)][string]$EfmiZipPath,
    [Parameter(Mandatory)][string]$LibrariesZipPath,
    [Parameter(Mandatory)][string]$DotnetPath,
    [string]$RuntimeLicenseDirectory,
    [string]$NoticeCacheDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$version = 'v0.2.2'
$encoding = [Text.UTF8Encoding]::new($false)
$inputs = @(
    @{ path=$DropInZipPath; hash='88433A0402C9E85C1D3790632C83D020DCF4699A90A7358D11E47B9789FD57E3' },
    @{ path=$EfmiZipPath; hash='1B33A7D92DBA01189E8B35C01048C2EACA780F9AD84C7C43F049EC5ED2C7B33F' },
    @{ path=$LibrariesZipPath; hash='13034531D4855E05273D9FBAF18539EC390432950EBD5986A115E5D3E5D8E275' }
)
foreach ($inputFile in $inputs) {
    $inputFile.path = (Resolve-Path -LiteralPath $inputFile.path).Path
    if ((Get-FileHash -LiteralPath $inputFile.path -Algorithm SHA256).Hash -cne $inputFile.hash) {
        throw "Unexpected standalone input bytes: $($inputFile.path)"
    }
}
$sdkVersion = & $DotnetPath --version
if ($LASTEXITCODE -ne 0 -or ([version]$sdkVersion).Major -lt 8) { throw 'Use .NET SDK 8 or newer.' }
if (!$RuntimeLicenseDirectory) { $RuntimeLicenseDirectory = Split-Path -Parent $DotnetPath }
foreach ($name in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $RuntimeLicenseDirectory $name))) {
        throw "Missing .NET runtime notice: $name"
    }
}
$build = Join-Path $root ('reports\standalone\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Assert-UniversalPath $root $build | Out-Null
if (Test-Path -LiteralPath $build) { throw 'Preserve existing standalone build.' }
$product = Join-Path $build "package\EndfieldJiggleEFMI-$version-standalone"
$efmi = Join-Path $product 'EFMI'
New-Item -ItemType Directory -Path $efmi -Force | Out-Null

function Expand-CheckedArchive([string]$ZipPath, [string]$Destination, [string]$Prefix = '') {
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ($name.EndsWith('/') -or !$name.StartsWith($Prefix, [StringComparison]::Ordinal)) { continue }
            $relative = $name.Substring($Prefix.Length)
            if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or
                @($relative -split '/' | Where-Object { $_ -in @('.','..','') }).Count) {
                throw "Unsafe archive entry: $name"
            }
            $target = Assert-UniversalPath $Destination (Join-Path $Destination $relative)
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $archive.Dispose() }
}

Expand-CheckedArchive $inputs[1].path $efmi
Expand-CheckedArchive $inputs[2].path $efmi
Expand-CheckedArchive $inputs[0].path (Join-Path $efmi 'Mods\EndfieldJiggleEFMI') 'EndfieldJiggleEFMI/'
$sections = @(Read-OrderedIniSections ([IO.File]::ReadAllText((Join-Path $efmi 'd3dx.ini'))))
function Set-BundleEntry([string]$Section, [string]$Key, [string]$Value) {
    $matches = @($sections | Where-Object name -IEQ $Section)
    if ($matches.Count -ne 1) { throw "Expected one INI section: $Section" }
    $lines = $matches[0].lines
    $indices = @(
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index].Split(';',2)[0].Trim()
            $separator = $line.IndexOf('=')
            if ($separator -gt 0 -and $line.Substring(0,$separator).Trim().Equals($Key,[StringComparison]::OrdinalIgnoreCase)) {
                $index
            }
        }
    )
    if ($indices.Count -ne 1) { throw "Expected one INI setting: $Section/$Key" }
    $lines[$indices[0]] = "$Key = $Value"
}
Set-BundleEntry 'Loader' 'loader' 'EndfieldJiggleLauncher.exe'
Set-BundleEntry 'Loader' 'launch' ''
Set-BundleEntry 'Hunting' 'hunting' '0'
$ini = ($sections | ForEach-Object { Format-OrderedIniSection $_ }) -join "`n"
[IO.File]::WriteAllText((Join-Path $efmi 'd3dx.ini'), $ini, $encoding)
foreach ($name in @('Mods','ShaderFixes','ShaderCache')) {
    New-Item -ItemType Directory -Path (Join-Path $efmi $name) -Force | Out-Null
}

$publish = Join-Path $build 'publish'
& $DotnetPath publish (Join-Path $root 'src\EndfieldJiggle.Launcher\EndfieldJiggle.Launcher.csproj') `
    --configuration Release --runtime win-x64 --self-contained true --nologo -o $publish `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Standalone launcher publish failed.' }
Copy-Item -LiteralPath (Join-Path $publish 'EndfieldJiggleLauncher.exe') -Destination $product
Copy-Item -LiteralPath (Join-Path $RuntimeLicenseDirectory 'LICENSE.txt') -Destination (Join-Path $product 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $RuntimeLicenseDirectory 'ThirdPartyNotices.txt') `
    -Destination (Join-Path $product 'DOTNET-THIRD-PARTY-NOTICES.txt')
foreach ($pair in @(
    @('docs\STANDALONE.md','INSTALL-STANDALONE-zh-CN.md'),
    @('docs\OUTFIT-COMPATIBILITY.md','OUTFIT-COMPATIBILITY.md'),
    @('docs\V022-STANDALONE-VALIDATION.md','VALIDATION-STANDALONE-zh-CN.md'),
    @('DISCLAIMER.md','DISCLAIMER.md'), @('LICENSE','LICENSE'),
    @('THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.md')
)) {
    Copy-Item -LiteralPath (Join-Path $root $pair[0]) -Destination (Join-Path $product $pair[1])
}
$noticeDirectory = Join-Path $product 'Licenses'
New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
$upstreamNotices = @(
    @('SpectrumQT/EFMI-Package','3d40c54ba86fdfd0e26d8ccbbd3848632f063b06','LICENSE','EFMI-LICENSE.txt'),
    @('SpectrumQT/XXMI-Libs-Package','a7847eae82aaac32175c9a7d199673d5c9323bcd','LICENSE.GPL.txt','XXMI-LIBS-LICENSE.GPL.txt'),
    @('SpectrumQT/XXMI-Libs-Package','a7847eae82aaac32175c9a7d199673d5c9323bcd','COPYING.txt','XXMI-LIBS-COPYING.txt'),
    @('SpectrumQT/XXMI-Libs-Package','a7847eae82aaac32175c9a7d199673d5c9323bcd','DirectXTK/LICENSE','DirectXTK-LICENSE.txt'),
    @('SpectrumQT/XXMI-Libs-Package','a7847eae82aaac32175c9a7d199673d5c9323bcd','pcre2-10.30/LICENCE','PCRE2-LICENSE.txt')
)
foreach ($notice in $upstreamNotices) {
    $target = Join-Path $noticeDirectory $notice[3]
    $cached = if ($NoticeCacheDirectory) { Join-Path $NoticeCacheDirectory $notice[3] } else { $null }
    if ($cached -and (Test-Path -LiteralPath $cached)) { Copy-Item -LiteralPath $cached -Destination $target }
    else {
        $response = (& gh api "repos/$($notice[0])/contents/$($notice[2])?ref=$($notice[1])" | Out-String)
        if ($LASTEXITCODE -ne 0) { throw "Could not retrieve pinned upstream notice: $($notice[3])" }
        $content = $response | ConvertFrom-Json
        [IO.File]::WriteAllBytes($target,[Convert]::FromBase64String($content.content))
    }
}
$manifest = [ordered]@{
    schema=1; version=$version; efmiVersion='1.4.8'; librariesVersion='1.2.2'
    efmiCommit='3d40c54ba86fdfd0e26d8ccbbd3848632f063b06'
    librariesCommit='a7847eae82aaac32175c9a7d199673d5c9323bcd'
    libraries=@(foreach ($name in @('3dmloader.dll','d3d11.dll','d3dcompiler_47.dll')) {
        [ordered]@{ path="EFMI/$name"; sha256=(Get-FileHash -LiteralPath (Join-Path $efmi $name)).Hash.ToLowerInvariant() }
    })
    sourceArchives=@(
        'https://github.com/SpectrumQT/EFMI-Package/archive/3d40c54ba86fdfd0e26d8ccbbd3848632f063b06.zip',
        'https://github.com/SpectrumQT/XXMI-Libs-Package/archive/a7847eae82aaac32175c9a7d199673d5c9323bcd.zip'
    )
    inputArchives=@($inputs | ForEach-Object { [ordered]@{ file=[IO.Path]::GetFileName($_.path); sha256=$_.hash.ToLowerInvariant() } })
}
[IO.File]::WriteAllText((Join-Path $product 'standalone-manifest.json'),
    ($manifest | ConvertTo-Json -Depth 8) + "`n", $encoding)
[IO.File]::WriteAllText((Join-Path $product 'Start.cmd'),
    "@echo off`r`nstart `"`" `"%~dp0EndfieldJiggleLauncher.exe`" --launch`r`n", [Text.ASCIIEncoding]::new())
$sources = Join-Path $build 'project-source'
New-Item -ItemType Directory -Path $sources -Force | Out-Null
foreach ($name in @('src','tools','runtime','shaders','tests','docs','third_party')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $sources -Recurse
}
foreach ($name in @('LICENSE','README.md','CHANGELOG.md','DISCLAIMER.md','THIRD-PARTY-NOTICES.md',
        'Directory.Build.props','Directory.Build.targets','.gitignore')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $sources
}
if (@(Get-ChildItem -LiteralPath $sources -Recurse -File | Where-Object Extension -In @('.exe','.dll','.bin','.pdb','.shdr')).Count) {
    throw 'The project source snapshot must not contain binary dependencies or shader captures.'
}
$sourceDirectory = Join-Path $product 'Sources'
New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($sources,
    (Join-Path $sourceDirectory "EndfieldJiggleEFMI-$version-source.zip"),
    [IO.Compression.CompressionLevel]::Optimal, $false)
$smokeOutput = Join-Path $build 'smoke'
$process = Start-Process -FilePath (Join-Path $product 'EndfieldJiggleLauncher.exe') `
    -ArgumentList '--smoke-test',('"' + $smokeOutput + '"') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) {
    $process.Kill()
    $process.WaitForExit()
    throw 'Isolated standalone launcher smoke timed out.'
}
if ($process.ExitCode -ne 0) { throw "Standalone smoke failed; check $smokeOutput." }
$smoke = Get-Content -LiteralPath (Join-Path $smokeOutput 'smoke-report.json') -Raw | ConvertFrom-Json
if (!$smoke.officialExportsResolved -or $smoke.gameStarted -or $smoke.injectorCalled -or $smoke.liveInstallationModified) {
    throw 'Standalone smoke violated its isolated boundary.'
}
$files = @(Get-ChildItem -LiteralPath $product -Recurse -File | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($product,$_.FullName).Replace('\','/');
        bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
[IO.File]::WriteAllText((Join-Path $product 'SHA256SUMS.txt'),
    (($files | ForEach-Object { "$($_.sha256) *$($_.path)" }) -join "`n") + "`n", $encoding)
$zip = Join-Path $build "EndfieldJiggleEFMI-$version-standalone-win64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $build 'package'), $zip,
    [IO.Compression.CompressionLevel]::Optimal, $false)
$report = [ordered]@{
    schema=1; version=$version; packageDirectory=$product; archive=$zip
    archiveSha256=(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
    files=$files; dependencies=$manifest; originalModAndConfiguratorUnchanged=$true
    smokeReport=(Join-Path $smokeOutput 'smoke-report.json')
    liveInstallationModified=$false; inGameVerified=$false
}
[IO.File]::WriteAllText((Join-Path $build 'build-report.json'), ($report | ConvertTo-Json -Depth 12), $encoding)
Write-Output "STANDALONE_PACKAGE=$product"
Write-Output "STANDALONE_ZIP=$zip"
Write-Output "STANDALONE_REPORT=$(Join-Path $build 'build-report.json')"
