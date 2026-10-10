[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseModZipPath,
    [string]$DotnetPath,
    [string]$RuntimeLicenseDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Read-Marker([object[]]$Lines, [string]$Marker) {
    $matches = @($Lines | ForEach-Object { "$_" } | Where-Object { $_.StartsWith($Marker,[StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) { throw "Missing build output marker: $Marker" }
    $matches[0].Substring($Marker.Length)
}
$modLog = @(& (Join-Path $PSScriptRoot 'Build-Installable.ps1') -BaseModZipPath $BaseModZipPath)
$modLog | Write-Output
$modZip = Read-Marker $modLog 'MOD_ZIP='
$configParameters = @{ RuntimeZipPath=$modZip }
if ($DotnetPath) { $configParameters.DotnetPath=$DotnetPath }
if ($RuntimeLicenseDirectory) { $configParameters.RuntimeLicenseDirectory=$RuntimeLicenseDirectory }
$configLog = @(& (Join-Path $PSScriptRoot 'Build-Configurator.ps1') @configParameters)
$configLog | Write-Output
$configZip = Read-Marker $configLog 'CONFIGURATOR_ZIP='
$configBuild = Split-Path -Parent $configZip
$smokeOutput = Join-Path $configBuild 'smoke'
$executable = Join-Path $configBuild 'publish\EndfieldJiggleConfigurator.exe'
$smokeProcess = Start-Process -FilePath $executable `
    -ArgumentList '--smoke-test',('"' + $smokeOutput + '"') -WindowStyle Hidden -PassThru
if (!$smokeProcess.WaitForExit(120000)) {
    $smokeProcess.Kill()
    $smokeProcess.WaitForExit()
    throw 'The isolated embedded-installer smoke test timed out.'
}
$smokeReport = Join-Path $smokeOutput 'smoke-report.json'
if (!(Test-Path -LiteralPath $smokeReport)) {
    throw "The embedded EXE failed its isolated test. Check $smokeOutput."
}
$smoke = Get-Content -LiteralPath $smokeReport -Raw | ConvertFrom-Json
if (!$smoke.embeddedInstaller -or !$smoke.stateRecovery -or !$smoke.outfitApplyRestore -or
    !$smoke.installerRestore -or $smoke.screenshots -ne 6 -or $smoke.liveInstallationModified) {
    throw 'The embedded EXE did not pass all complete workflow smoke gates.'
}
Write-Output "SMOKE_REPORT=$smokeReport"
$bundleLog = @(& (Join-Path $PSScriptRoot 'Build-Installable.ps1') -BaseModZipPath $BaseModZipPath -ConfiguratorZipPath $configZip)
$bundleLog | Write-Output
$build = Read-Marker $bundleLog 'RELEASE_BUILD='
& (Join-Path $PSScriptRoot 'Export-Release.ps1') -ReleaseBuildDirectory $build `
    -ConfiguratorZipPath $configZip -OutputDirectory $OutputDirectory
