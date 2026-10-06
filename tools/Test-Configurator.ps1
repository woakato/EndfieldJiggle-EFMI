[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModZipPath,
    [string]$DotnetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$zipPath = (Resolve-Path -LiteralPath $ModZipPath).Path
if (!$DotnetPath) {
    $DotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source
}
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path

$testProject = Join-Path $root 'tests\EndfieldJiggle.Configurator.Tests\EndfieldJiggle.Configurator.Tests.csproj'
& $DotnetPath run --project $testProject --configuration Release -- $zipPath
if ($LASTEXITCODE -ne 0) {
    throw 'Configurator runtime acceptance fixture failed.'
}
