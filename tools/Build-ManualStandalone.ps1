# Historical manual-game-launch workflow; not the v0.2.7b XXMI Launcher standalone.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DropInZipPath,
    [Parameter(Mandatory)][string]$EfmiZipPath,
    [Parameter(Mandatory)][string]$LibrariesZipPath,
    [Parameter(Mandatory)][string]$DotnetPath,
    [string]$OutputDirectory,
    [string]$RuntimeLicenseDirectory,
    [string]$NoticeCacheDirectory,
    [ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
    [ValidatePattern('^v\d+\.\d+\.\d+[ab]?$')][string]$Version = 'v0.2.7b',
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedDropInSha256,
    [AllowEmptyString()][string]$DropInRuntimePrefix = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$encoding = [Text.UTF8Encoding]::new($false)
$efmiVersion = '1.5.1'
$efmiCommit = '45e0ca07af9db81b0907c537c413d281db4f235b'
$libsVersion = '1.3.3'
$libsCommit = '8bd31f74727f033b3d9049509e19da3563c8b98c'
$inputs = @(
    @{ path=$DropInZipPath; hash=$ExpectedDropInSha256.ToUpperInvariant() },
    @{ path=$EfmiZipPath; hash='10B350C620054A3A3D46D704789ABBDD83EF43AFDFD3B70932A036236E426C0A' },
    @{ path=$LibrariesZipPath; hash='168561008D456E32F3B0CC4A2505DBD68AD6E2BE0019CD40FF79C8E9769B37AF' }
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
$build = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $root ('reports\standalone\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
if (Test-Path -LiteralPath $build) { throw 'Preserve existing standalone build.' }
$product = Join-Path $build 'package\EndfieldJiggle'
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
Expand-CheckedArchive $inputs[0].path (Join-Path $efmi 'Mods\EndfieldJiggleEFMI') $DropInRuntimePrefix
$upstreamIni = [IO.File]::ReadAllText((Join-Path $efmi 'd3dx.ini'))
$sections = @(Read-OrderedIniSections $upstreamIni)
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
$preamble = $upstreamIni.Substring(0, $upstreamIni.IndexOf("[$($sections[0].name)]", [StringComparison]::Ordinal))
$ini = ($sections | ForEach-Object { Format-OrderedIniSection $_ }) -join "`n"
if ($upstreamIni.Contains("`r`n")) { $ini = $ini.Replace("`n","`r`n") }
$ini = $preamble + $ini
[IO.File]::WriteAllText((Join-Path $efmi 'd3dx.ini'), $ini, $encoding)
foreach ($name in @('Mods','ShaderFixes','ShaderCache')) {
    New-Item -ItemType Directory -Path (Join-Path $efmi $name) -Force | Out-Null
}

$publish = Join-Path $build 'publish'
& $DotnetPath publish (Join-Path $root 'src\EndfieldJiggle.Launcher\EndfieldJiggle.Launcher.csproj') `
    --configuration Release --runtime win-x64 --self-contained true --nologo -o $publish `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$($Version.TrimStart('v') -replace '[ab]$','')" `
    "-p:InformationalVersion=$($Version.TrimStart('v'))" -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw 'Standalone launcher publish failed.' }
Copy-Item -LiteralPath (Join-Path $publish 'EndfieldJiggleLauncher.exe') -Destination $product
$noticeDirectory = Join-Path $product 'Licenses'
New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
$mod = Join-Path $efmi 'Mods\EndfieldJiggleEFMI'
$modNotices = Assert-UniversalPath $product (Join-Path $mod 'Licenses')
if (!(Test-Path -LiteralPath $modNotices -PathType Container)) {
    throw 'Use the current drop-in package with consolidated Licenses.'
}
foreach ($notice in Get-ChildItem -LiteralPath $modNotices -File) {
    Move-Item -LiteralPath $notice.FullName -Destination $noticeDirectory
}
Remove-Item -LiteralPath $modNotices
$modReadme = Assert-UniversalPath $product (Join-Path $mod 'README.txt')
Remove-Item -LiteralPath $modReadme
Copy-Item -LiteralPath (Join-Path $RuntimeLicenseDirectory 'LICENSE.txt') `
    -Destination (Join-Path $noticeDirectory 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $RuntimeLicenseDirectory 'ThirdPartyNotices.txt') `
    -Destination (Join-Path $noticeDirectory 'DOTNET-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $root 'docs\package\README-Standalone-zh-CN.txt') `
    -Destination (Join-Path $product 'README.txt')
$upstreamNotices = @(
    @('SpectrumQT/EFMI-Package',$efmiCommit,'LICENSE','EFMI-LICENSE.txt'),
    @('SpectrumQT/XXMI-Libs-Package',$libsCommit,'LICENSE.GPL.txt','XXMI-LIBS-LICENSE.GPL.txt'),
    @('SpectrumQT/XXMI-Libs-Package',$libsCommit,'COPYING.txt','XXMI-LIBS-COPYING.txt'),
    @('SpectrumQT/XXMI-Libs-Package',$libsCommit,'DirectXTK/LICENSE','DirectXTK-LICENSE.txt'),
    @('SpectrumQT/XXMI-Libs-Package',$libsCommit,'pcre2-10.30/LICENCE','PCRE2-LICENSE.txt')
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
    schema=1; version=$version; efmiVersion=$efmiVersion; librariesVersion=$libsVersion
    launchWorkflow=[ordered]@{
        mode='manual-official-launcher'; revision=3; startsGameExecutable=$false
        processImageQuery='QueryFullProcessImageNameW'; processAccess='PROCESS_QUERY_LIMITED_INFORMATION'
        shaderCachePathPreflight=$true; maximumShaderCachePathLength=259
        injection='unchanged-upstream-direct'; processTimeoutSeconds=300
        directX11='Select DirectX 11 in the official launcher; this bundle does not supply game launch arguments.'
    }
    efmiCommit=$efmiCommit
    librariesCommit=$libsCommit
    libraries=@(foreach ($name in @('3dmloader.dll','d3d11.dll','d3dcompiler_47.dll')) {
        [ordered]@{ path="EFMI/$name"; sha256=(Get-FileHash -LiteralPath (Join-Path $efmi $name)).Hash.ToLowerInvariant() }
    })
    sourceArchives=@(
        "https://github.com/SpectrumQT/EFMI-Package/archive/$efmiCommit.zip",
        "https://github.com/SpectrumQT/XXMI-Libs-Package/archive/$libsCommit.zip"
    )
    inputArchives=@($inputs | ForEach-Object { [ordered]@{ file=[IO.Path]::GetFileName($_.path); sha256=$_.hash.ToLowerInvariant() } })
}
[IO.File]::WriteAllText((Join-Path $product 'standalone-manifest.json'),
    ($manifest | ConvertTo-Json -Depth 8) + "`n", $encoding)
[IO.File]::WriteAllText((Join-Path $product 'Start.cmd'),
    "@echo off`r`nstart `"`" `"%~dp0EndfieldJiggleLauncher.exe`" --launch`r`n", [Text.ASCIIEncoding]::new())
$sourceDirectory = Join-Path $product 'Sources'
New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
$sourceArchive = Join-Path $sourceDirectory "EndfieldJiggleEFMI-$version-source.zip"
if ($SourceCommit) {
    Push-Location $root
    try {
        $sourcePaths = @(& git ls-tree -r --name-only $SourceCommit)
        if ($LASTEXITCODE -ne 0 -or !$sourcePaths.Count) { throw 'Cannot enumerate the release source commit.' }
        if (@($sourcePaths | Where-Object {
            $_ -match '^(reports|build|dist|\.git)/|\.(dll|exe|bin|pdb|shdr|zip)$|(^|/)(\.env|AGENTS\.md)'
        }).Count) { throw 'The source commit includes private or binary material.' }
        & git archive --format=zip "--output=$sourceArchive" $SourceCommit
        if ($LASTEXITCODE -ne 0) { throw 'Release source export failed.' }
    } finally { Pop-Location }
} else {
    $sources = Join-Path $build 'project-source'
    New-Item -ItemType Directory -Path $sources -Force | Out-Null
    # Use the tracked public file list, with current working-tree bytes, never build directories.
    $sourcePaths = @(& git -C $root ls-files)
    if ($LASTEXITCODE -ne 0 -or !$sourcePaths.Count) { throw 'Cannot enumerate public source.' }
    foreach ($relative in $sourcePaths) {
        if ($relative -match '(^|/)(reports|build|dist|\.git|bin|obj)/|\.(dll|exe|bin|pdb|shdr|zip)$|(^|/)(\.env|AGENTS\.md)') {
            throw "Unexpected private or generated source entry: $relative"
        }
        $target = Assert-UniversalPath $sources (Join-Path $sources $relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($sources, $sourceArchive,
        [IO.Compression.CompressionLevel]::Optimal, $false)
}
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
if (!$smoke.officialExportsResolved -or $smoke.gameStarted -or $smoke.injectorCalled -or $smoke.liveInstallationModified -or
    $smoke.launchMode -ne 'manual-official-launcher' -or $smoke.startsGameExecutable -ne $false) {
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
    launchWorkflow=$manifest.launchWorkflow
    archiveSha256=(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
    files=$files; dependencies=$manifest; runtimeAndConfiguratorBytesUnchanged=$true; sourceCommit=$SourceCommit
    documentationLayout='README.txt and Licenses/'
    smokeReport=(Join-Path $smokeOutput 'smoke-report.json')
    liveInstallationModified=$false; inGameVerified=$false
}
[IO.File]::WriteAllText((Join-Path $build 'build-report.json'), ($report | ConvertTo-Json -Depth 12), $encoding)
Write-Output "STANDALONE_PACKAGE=$product"
Write-Output "STANDALONE_ZIP=$zip"
Write-Output "STANDALONE_REPORT=$(Join-Path $build 'build-report.json')"
