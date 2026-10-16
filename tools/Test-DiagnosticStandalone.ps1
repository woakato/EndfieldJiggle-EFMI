[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$product = (Resolve-Path -LiteralPath $PackageDirectory).Path
$required = @(
    'Collect-Diagnostics.ps1',
    'Collect-Diagnostics.cmd',
    'DIAGNOSTIC-README-zh-CN.md',
    'DiagnosticEnvironment.cs',
    'diagnostic-checksums.json',
    'EFMI\Mods\EndfieldJiggleEFMI\DiagnosticTelemetry.ini',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\diagnostic_cs.hlsl',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\diagnostic_cs.cs_5_0.8000.bin',
    'EFMI\d3dx.ini',
    'EFMI\Mods\EndfieldJiggleEFMI\EndfieldJiggle.ini',
    'EFMI\Mods\EndfieldJiggleEFMI\Passes.ini',
    'EFMI\Mods\EndfieldJiggleEFMI\Outfits.ini',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\step_cs.hlsl',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\step_cs.cs_5_0.8000.bin',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\character_pick_ps.hlsl',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\character_pick_ps.ps_5_0.8000.bin',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\pick_1479b2b594b9c91a.hlsl',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\pick_1479b2b594b9c91a.vs_5_0.8000.bin',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\pick_c6e55aaa8f4b3218.hlsl',
    'EFMI\Mods\EndfieldJiggleEFMI\shaders\pick_c6e55aaa8f4b3218.vs_5_0.8000.bin'
)
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $product $relative) -PathType Leaf)) {
        throw "Missing diagnostic package file: $relative"
    }
}
$text = Get-Content -LiteralPath (Join-Path $product 'EFMI\d3dx.ini') -Raw
foreach ($expected in @('calls = 0', 'input = 1', 'debug = 0',
        'log_level = info', 'unbuffered = 1', 'show_warnings = 1', 'crash = 0', 'hunting = 0')) {
    if ($text -notmatch "(?m)^\s*$([regex]::Escape($expected))\s*$") {
        throw "Diagnostic logging setting missing: $expected"
    }
}
if ($text -match '(?m)^\s*debug\s*=\s*1\s*$') {
    throw 'Diagnostic package must not enable massive debug logging.'
}
$pairs = @(
    @('diagnostic_cs.hlsl', 'diagnostic_cs.cs_5_0.8000.bin'),
    @('step_cs.hlsl', 'step_cs.cs_5_0.8000.bin'),
    @('character_pick_ps.hlsl', 'character_pick_ps.ps_5_0.8000.bin'),
    @('pick_1479b2b594b9c91a.hlsl', 'pick_1479b2b594b9c91a.vs_5_0.8000.bin'),
    @('pick_c6e55aaa8f4b3218.hlsl', 'pick_c6e55aaa8f4b3218.vs_5_0.8000.bin')
)
$shaderDirectory = Join-Path $product 'EFMI\Mods\EndfieldJiggleEFMI\shaders'
foreach ($pair in $pairs) {
    $left = Get-Item (Join-Path $shaderDirectory $pair[0])
    $right = Get-Item (Join-Path $shaderDirectory $pair[1])
    if ($left.LastWriteTimeUtc.ToFileTimeUtc() -ne $right.LastWriteTimeUtc.ToFileTimeUtc()) {
        throw "Shader/cache FILETIME mismatch: $($pair[0])"
    }
}
if (Test-Path -LiteralPath (Join-Path $product 'EFMI\d3d11_log.txt')) {
    throw 'Diagnostic build must start without a copied d3d11_log.txt.'
}
if (Test-Path -LiteralPath (Join-Path $product 'launch-settings.json')) {
    throw 'Diagnostic build must not carry a private launch-settings.json.'
}
$manifest = Get-Content -LiteralPath (Join-Path $product 'standalone-manifest.json') -Raw |
    ConvertFrom-Json
if (!$manifest.diagnostic -or $manifest.diagnostic.frameAnalysisEnabled -ne $false -or
    $manifest.diagnostic.fullD3d11LogIncluded -ne $false) {
    throw 'Diagnostic manifest is missing its bounded-logging declaration.'
}
if ($manifest.launchWorkflow.mode -ne 'manual-official-launcher' -or
    $manifest.launchWorkflow.startsGameExecutable -ne $false -or
    $manifest.diagnostic.revision -ne 3) {
    throw 'Diagnostic revision 3 must share the manual official-launcher workflow.'
}
$outputRoot = Split-Path -Parent (Split-Path -Parent $product)
$output = Join-Path $outputRoot ('collector-test-' + [guid]::NewGuid().ToString('N'))
& "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $product 'Collect-Diagnostics.ps1') -PackageDirectory $product -OutputDirectory $output
if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell 5.1 collector failed.' }
foreach ($name in @('system.json', 'package-files.json', 'config-summary.txt',
        'log-summary.json', 'launch-summary.json', 'path-budget.json', 'README.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $output $name) -PathType Leaf)) {
        throw "Diagnostic collector did not produce $name."
    }
}
$summary = Get-Content -LiteralPath (Join-Path $output 'package-files.json') -Raw |
    ConvertFrom-Json
if (@($summary.shaderCachePairs | Where-Object { !$_.sameFileTime }).Count) {
    throw 'Diagnostic collector reports shader/cache timestamp mismatches in a clean package.'
}
if ($summary.baselineError -or @($summary.requiredFiles | Where-Object status -NE 'ok').Count) {
    throw 'Checksum comparison failed for the clean diagnostic package.'
}
$paths = Get-Content -LiteralPath (Join-Path $output 'path-budget.json') -Raw | ConvertFrom-Json
if (!$paths.verifiedBaseline -or !$paths.shaders.Count -or $paths.errors.Count -or
    @($paths.shaders | Where-Object exceedsLimit).Count) {
    throw 'Diagnostic package has an incomplete or overlong shader-cache path budget.'
}
$system = Get-Content (Join-Path $output 'system.json') -Raw | ConvertFrom-Json
if (!$system.nativeProbe.loaderLibraryLoads -or !$system.nativeProbe.hardwareD3d11.created -or
    !$system.nativeProbe.warpD3d11.created) { throw 'Native runtime or D3D11 probe failed on the test machine.' }
if (Test-Path -LiteralPath (Join-Path $output 'launch-settings.json')) {
    throw 'Diagnostic collector must not export private launch settings.'
}
Write-Output 'PASS: diagnostic package files, logging controls, clean state, collector and shader/cache FILETIMEs.'
