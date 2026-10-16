[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostPath,
    [string]$FxcPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\fxc.exe',
    [string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $root ('reports\shading-maintenance\field-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$output = Assert-UniversalPath (Join-Path $root 'reports') $output
if (Test-Path -LiteralPath $output) { throw 'Preserve existing field-test evidence.' }
$hostExe = (Resolve-Path -LiteralPath $HostPath).Path
New-Item -ItemType Directory -Path $output -Force | Out-Null
$results = @()
foreach ($variant in @(
    @{ name='frame'; defines=@('SURFACE_FRAME=1') },
    @{ name='normal'; defines=@('SURFACE_FRAME=1','NORMAL_ONLY=1') },
    @{ name='current'; defines=@() },
    @{ name='previous'; defines=@('CAMERA_INDEX=81','OFFSET_INDEX=2') }
)) {
    $binary = Join-Path $output ($variant.name + '.bin')
    $arguments = @('/nologo','/T','vs_5_0','/E','main','/O3',
        '/I',(Join-Path $root 'third_party\JiggleForge'),'/Fo',$binary)
    foreach ($define in $variant.defines) { $arguments += @('/D',$define) }
    $compile = & $FxcPath @arguments (Join-Path $root 'shaders\field_vs.hlsl') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Field compilation failed: $compile" }
    $lines = @(& $hostExe $binary $variant.name 2>&1 | ForEach-Object { "$_" })
    $exitCode = $LASTEXITCODE
    $lines | Set-Content -LiteralPath (Join-Path $output ($variant.name + '.txt')) -Encoding utf8
    $results += [ordered]@{ mode=$variant.name; exit=$exitCode; output=$lines }
    Write-Output ($lines -join "`n")
}
$passed = @($results | Where-Object exit -NE 0).Count -eq 0
[ordered]@{
    passed=$passed; backend='D3D11 WARP stream output'; results=$results
    fieldSourceSha256=(Get-FileHash -LiteralPath (Join-Path $root 'shaders\field_vs.hlsl')).Hash
    gameStarted=$false; liveInstallationModified=$false; inGameVerified=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'test-report.json') -Encoding utf8
if (!$passed) { throw "Field regression failed; see $output" }
Write-Output "FIELD_REPORT=$(Join-Path $output 'test-report.json')"
