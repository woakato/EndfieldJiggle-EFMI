[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModZipPath,
    [string]$DotnetPath,
    [string]$ReadOnlyOutfitPath,
    [string]$EfmiPath,
    [string]$DependencyModPath,
    [string]$LoaderHostPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$DotnetPath) { $DotnetPath=(Get-Command dotnet.exe -ErrorAction Stop).Source }
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'Test-PublicSource.ps1')
    & (Join-Path $PSScriptRoot 'Test-ReleaseRegression.ps1') -ModZipPath $ModZipPath
    foreach ($name in @('Configurator','Installation','OutfitAdapter','Qaqm')) {
        [string[]]$arguments = @(
            if ($name -in @('Configurator','Installation')) { $ModZipPath }
        )
        & $DotnetPath run --project (Join-Path $root "tests\EndfieldJiggle.$name.Tests\EndfieldJiggle.$name.Tests.csproj") `
            --configuration Release -- @arguments
        if ($LASTEXITCODE -ne 0) { throw "$name test suite failed." }
    }
    if ($ReadOnlyOutfitPath) {
        foreach ($path in @($EfmiPath,$DependencyModPath,$LoaderHostPath)) {
            if (!$path) { throw 'Read-only outfit integration requires EFMI, dependency and loader-host paths.' }
        }
        & $DotnetPath run --project (Join-Path $root 'tests\EndfieldJiggle.Complete.Tests\EndfieldJiggle.Complete.Tests.csproj') `
            --configuration Release -- $ModZipPath $ReadOnlyOutfitPath $EfmiPath $DependencyModPath $LoaderHostPath
        if ($LASTEXITCODE -ne 0) { throw 'Actual isolated EFMI complete workflow test failed.' }
    }
} finally { Pop-Location }
Write-Output 'Complete package checks passed. F10 in a running game remains a separate user verification.'
