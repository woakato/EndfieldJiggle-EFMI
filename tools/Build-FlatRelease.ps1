[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeReportPath,
    [Parameter(Mandatory)][string]$DotnetPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version = 'v0.2.3'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$build = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $build) { throw 'Preserve previous release evidence.' }
$runtimeReport = Get-Content -Raw -LiteralPath $RuntimeReportPath | ConvertFrom-Json
if ($runtimeReport.buildKind -cne 'skin-pass-coverage-candidate' -or $runtimeReport.passes.Count -ne 13) {
    throw 'Use the reviewed thirteen-program skin-pass runtime.'
}
$mod = Join-Path $build 'mod'
New-Item -ItemType Directory -Path $mod | Out-Null
foreach ($file in $runtimeReport.files) {
    $source = Assert-UniversalPath $runtimeReport.packageDirectory (Join-Path $runtimeReport.packageDirectory $file.path)
    if ((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256) { throw "Runtime payload changed: $($file.path)" }
    $target = Assert-UniversalPath $mod (Join-Path $mod $file.path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    [IO.File]::SetLastWriteTimeUtc($target, [IO.File]::GetLastWriteTimeUtc($source))
}
foreach ($pair in @(
    @('LICENSE','LICENSE'), @('DISCLAIMER.md','DISCLAIMER.md'),
    @('THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.md'),
    @('docs\INSTALL-zh-CN.md','INSTALL-zh-CN.md'),
    @('docs\OUTFIT-COMPATIBILITY.md','OUTFIT-COMPATIBILITY.md')
)) { Copy-Item -LiteralPath (Join-Path $root $pair[0]) -Destination (Join-Path $mod $pair[1]) }
$modZip = Join-Path $build "EndfieldJiggleEFMI-$Version-win64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($mod, $modZip, [IO.Compression.CompressionLevel]::Optimal, $false)
& (Join-Path $PSScriptRoot 'Test-ReleaseRegression.ps1') -ModZipPath $modZip
foreach ($test in @('Configurator','Installation','Discovery')) {
    $testArguments = @($modZip)
    if ($test -ceq 'Configurator') { $testArguments += (Join-Path $build 'unit-tests') }
    & $DotnetPath run --project (Join-Path $root "tests\EndfieldJiggle.$test.Tests\EndfieldJiggle.$test.Tests.csproj") `
        --configuration Release -- @testArguments
    if ($LASTEXITCODE -ne 0) { throw "$test release checks failed." }
}
& $DotnetPath run --project (Join-Path $root 'tests\EndfieldJiggle.OutfitAdapter.Tests\EndfieldJiggle.OutfitAdapter.Tests.csproj') `
    --configuration Release -- $root $mod
if ($LASTEXITCODE -ne 0) { throw 'Outfit adapter checks failed.' }
$configLog = @(& (Join-Path $PSScriptRoot 'Build-Configurator.ps1') `
    -DotnetPath $DotnetPath -RuntimeZipPath $modZip -Version $Version)
$configLog | Write-Output
$markers = @($configLog | ForEach-Object { "$_" } | Where-Object { $_.StartsWith('CONFIGURATOR_ZIP=') })
if ($markers.Count -ne 1) { throw 'Missing configurator build output.' }
$configZip = $markers[0].Substring('CONFIGURATOR_ZIP='.Length)
$configBuild = Split-Path -Parent $configZip
$configExe = Join-Path $configBuild 'publish\EndfieldJiggleConfigurator.exe'
$smokePath = Join-Path $build 'configurator-smoke'
$start = [Diagnostics.ProcessStartInfo]::new($configExe)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('--smoke-test')
$start.ArgumentList.Add($smokePath)
$process = [Diagnostics.Process]::Start($start)
if (!$process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw 'Configurator smoke timed out.' }
$smoke = Get-Content -Raw -LiteralPath (Join-Path $smokePath 'smoke-report.json') | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or !$smoke.embeddedInstaller -or !$smoke.stateRecovery -or
    !$smoke.outfitApplyRestore -or !$smoke.installerRestore -or !$smoke.hotSettingsSaveRestore -or
    !$smoke.profiles -or !$smoke.modLibrary -or $smoke.screenshots -ne 10 -or $smoke.liveInstallationModified) {
    throw 'Configurator failed an isolated workflow gate.'
}
$dropin = Join-Path $build 'drop-in'
Copy-Item -LiteralPath $mod -Destination $dropin -Recurse
$configDirectory = Join-Path $dropin 'Configurator'
New-Item -ItemType Directory -Path $configDirectory | Out-Null
$configArchive = [IO.Compression.ZipFile]::OpenRead($configZip)
try {
    $allowed = @('Configurator/EndfieldJiggleConfigurator.exe','DOTNET-LICENSE.txt',
        'DOTNET-THIRD-PARTY-NOTICES.txt','LICENSE','README-zh-CN.md','SHA256SUMS.txt')
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $configArchive.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        if ($entry.FullName -cnotin $allowed -or !$seen.Add($entry.FullName)) { throw 'Unexpected configurator archive entry.' }
        $target = Join-Path $configDirectory ([IO.Path]::GetFileName($entry.FullName))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
    }
    if ($seen.Count -ne $allowed.Count) { throw 'Incomplete configurator archive.' }
} finally { $configArchive.Dispose() }
$dropinZip = Join-Path $build "EndfieldJiggleEFMI-$Version-drop-in-win64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($dropin, $dropinZip, [IO.Compression.CompressionLevel]::Optimal, $false)
foreach ($file in $runtimeReport.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $dropin $file.path)).Hash -cne $file.sha256) {
        throw 'Drop-in runtime differs from the reviewed skin-pass candidate.'
    }
}
$reportPath = Join-Path $build 'build-report.json'
[ordered]@{
    schema=1; version=$Version; buildKind='flat-mod-and-drop-in'
    runtimeReportPath=(Resolve-Path -LiteralPath $RuntimeReportPath).Path
    runtimeReportSha256=(Get-FileHash -LiteralPath $RuntimeReportPath).Hash
    modDirectory=$mod; dropInDirectory=$dropin
    modZip=$modZip; modSha256=(Get-FileHash -LiteralPath $modZip).Hash
    dropInZip=$dropinZip; dropInSha256=(Get-FileHash -LiteralPath $dropinZip).Hash
    configuratorZip=$configZip; configuratorSmoke=(Join-Path $smokePath 'smoke-report.json')
    runtimeFiles=$runtimeReport.files; runtimeBytesUnchanged=$true
    archiveLayout='shaders and runtime INIs directly at archive root'
    liveInstallationModified=$false; githubPublished=$false
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "FLAT_RELEASE_REPORT=$reportPath"
