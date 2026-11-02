[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CmakePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$MinHookSourceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$output = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $output) { throw 'Preserve existing input guard evidence.' }
$native = Join-Path $output 'native'
$dependencyArguments = @()
if ($MinHookSourceDirectory) {
    $MinHookSourceDirectory = (Resolve-Path -LiteralPath $MinHookSourceDirectory).Path
    $dependencyArguments = @("-DFETCHCONTENT_SOURCE_DIR_MINHOOK=$MinHookSourceDirectory")
}
& $CmakePath -S (Join-Path $root 'src\EndfieldJiggle.InputGuard') -B $native -A x64 @dependencyArguments
if ($LASTEXITCODE -ne 0) { throw 'Input guard configuration failed.' }
& $CmakePath --build $native --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw 'Input guard build failed.' }
$test = Join-Path $native 'Release\input_guard_tests.exe'
$testOutput = @(& $test)
if ($LASTEXITCODE -ne 0) { throw 'Input guard tests failed.' }
$testOutput | Write-Output
$package = Join-Path $output 'InputGuard'
New-Item -ItemType Directory -Path $package | Out-Null
foreach ($name in @('EndfieldJiggleInputGuard.exe','EndfieldJiggleInputGuard.dll')) {
    Copy-Item -LiteralPath (Join-Path $native "Release\$name") -Destination $package
}
$dependency = if ($MinHookSourceDirectory) { $MinHookSourceDirectory } else { Join-Path $native '_deps\minhook-src' }
Copy-Item -LiteralPath (Join-Path $dependency 'LICENSE.txt') `
    -Destination (Join-Path $package 'MINHOOK-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'docs\ALT-INPUT-GUARD.md') `
    -Destination (Join-Path $package 'README-zh-CN.md')
$files = @(Get-ChildItem -LiteralPath $package -File | ForEach-Object {
    [ordered]@{path=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;size=$_.Length}
})
$reportPath = Join-Path $output 'build-report.json'
[ordered]@{
    schema=1;kind='alt-input-guard';packageDirectory=$package;files=$files
    testOutput=$testOutput;minhookCommit='c3fcafdc10146beb5919319d0683e44e3c30d537'
    inputMode='hold-alt';changesCameraMatrices=$false;changesGameTime=$false
    usesOfficialLoader=$true;liveInstallationModified=$false;inGameVerified=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "INPUT_GUARD_REPORT=$reportPath"
