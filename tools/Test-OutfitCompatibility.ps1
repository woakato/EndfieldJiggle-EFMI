[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModZipPath,
    [Parameter(Mandatory)][string[]]$ReadOnlyOutfitPaths,
    [Parameter(Mandatory)][string]$EfmiPath,
    [Parameter(Mandatory)][string]$DependencyModPath,
    [Parameter(Mandatory)][string]$LoaderHostPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DotnetPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$output = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (!$DotnetPath) { $DotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source }
$arguments = @(
    (Resolve-Path -LiteralPath $ModZipPath).Path,
    (Resolve-Path -LiteralPath $EfmiPath).Path,
    (Resolve-Path -LiteralPath $DependencyModPath).Path,
    (Resolve-Path -LiteralPath $LoaderHostPath).Path,
    $output
) + @($ReadOnlyOutfitPaths | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
& $DotnetPath run --project (Join-Path $root 'tests\EndfieldJiggle.OutfitCompatibility.Tests\EndfieldJiggle.OutfitCompatibility.Tests.csproj') `
    --configuration Release -- @arguments
if ($LASTEXITCODE -ne 0) { throw 'Isolated outfit compatibility checks failed.' }
