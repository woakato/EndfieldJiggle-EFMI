[CmdletBinding()]
param(
    [string]$DotnetPath,
    [string]$RuntimeLicenseDirectory,
    [string]$RuntimeZipPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

if (!$DotnetPath) {
    $DotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source
}
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
$sdkVersion = & $DotnetPath --version
if ($LASTEXITCODE -ne 0 -or ([version]$sdkVersion).Major -lt 8) {
    throw '.NET 8 SDK or newer is required.'
}

if (!$RuntimeLicenseDirectory) {
    $RuntimeLicenseDirectory = Split-Path -Parent $DotnetPath
}
$runtimeNotices = @{}
foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $path = Join-Path $RuntimeLicenseDirectory $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The .NET runtime license file is missing: $path"
    }
    $runtimeNotices[$name] = (Resolve-Path -LiteralPath $path).Path
}

$outputRoot = Join-Path $root 'reports\configurator'
$build = Join-Path $outputRoot ('build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $build) {
    throw "Preserve existing configurator build: $build"
}
$publish = Join-Path $build 'publish'
$package = Join-Path $build 'package'
$configurator = Join-Path $package 'Configurator'
$entryPoint = 'Configurator\EndfieldJiggleConfigurator.exe'
New-Item -ItemType Directory -Path $configurator -Force | Out-Null

$project = Join-Path $root 'src\EndfieldJiggle.Configurator\EndfieldJiggle.Configurator.csproj'
$resourceArguments = @()
if ($RuntimeZipPath) {
    $runtimeZip = (Resolve-Path -LiteralPath $RuntimeZipPath).Path
    & (Join-Path $PSScriptRoot 'Test-ReleaseRegression.ps1') -ModZipPath $runtimeZip
    $resources = Join-Path $build 'resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    Copy-Item -LiteralPath $runtimeZip -Destination (Join-Path $resources 'InstallableRuntime.zip')
    Copy-Item -LiteralPath $runtimeNotices['LICENSE.txt'] -Destination (Join-Path $resources 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath $runtimeNotices['ThirdPartyNotices.txt'] -Destination (Join-Path $resources 'DOTNET-THIRD-PARTY-NOTICES.txt')
    foreach ($relative in @('LICENSE','DISCLAIMER.md','docs\INSTALL-zh-CN.md')) {
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination (Join-Path $resources ([IO.Path]::GetFileName($relative)))
    }
    $resourceArguments = @("-p:InstallableResourceDirectory=$resources")
}
& $DotnetPath publish $project --configuration Release --runtime win-x64 `
    --self-contained true --nologo -o $publish `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false @resourceArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Configurator publish failed.'
}

$executable = Join-Path $publish 'EndfieldJiggleConfigurator.exe'
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Self-contained configurator executable was not produced.'
}
Copy-Item -LiteralPath $executable -Destination (Join-Path $configurator 'EndfieldJiggleConfigurator.exe')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $package 'LICENSE')
Copy-Item -LiteralPath $runtimeNotices['LICENSE.txt'] `
    -Destination (Join-Path $package 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath $runtimeNotices['ThirdPartyNotices.txt'] `
    -Destination (Join-Path $package 'DOTNET-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $root 'docs\CONFIGURATOR.md') `
    -Destination (Join-Path $package 'README-zh-CN.md')
$exeHash = (Get-FileHash -LiteralPath (Join-Path $package $entryPoint) -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS.txt'),
    "$exeHash *$entryPoint`n", [Text.UTF8Encoding]::new($false))

$zip = Join-Path $build 'EndfieldJiggleConfigurator-v0.2.1-win64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory(
    $package, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$entries = @(
    Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
        [pscustomobject]@{
            path = [IO.Path]::GetRelativePath($package, $_.FullName)
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
)
if (@($entries | Where-Object {
    [IO.Path]::GetExtension($_.path) -iin @('.ini', '.hlsl', '.bin', '.dll', '.pdb', '.shdr')
}).Count) {
    throw 'Configurator bundle must not contain runtime INIs, game shader payloads or loose binaries.'
}

$report = [ordered]@{
    schema = 1
    product = 'EndfieldJiggle v0.2.1 installer and configurator'
    builtAt = (Get-Date).ToString('o')
    entryPoint = $entryPoint
    executableSha256 = $exeHash
    archive = $zip
    archiveSha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    archiveBytes = (Get-Item -LiteralPath $zip).Length
    files = $entries
    containsGamePayload = [bool]$RuntimeZipPath
    embeddedRuntimeArchive = $RuntimeZipPath
    touchesGameOrGlobalFiles = $false
    liveInstallationModified = $false
    inGameVerified = $false
}
$reportPath = Join-Path $build 'build-report.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "CONFIGURATOR_ZIP=$zip"
Write-Output "CONFIGURATOR_REPORT=$reportPath"
