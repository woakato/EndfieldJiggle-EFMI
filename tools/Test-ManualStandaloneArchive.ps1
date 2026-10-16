[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Diagnostic
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Preserve previous archive test results.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $Archive).Path, $OutputDirectory)
$children = @(Get-ChildItem -LiteralPath $OutputDirectory -Directory)
if ($children.Count -ne 1) { throw 'Expected exactly one product directory.' }
$product = $children[0].FullName
$manifest = Get-Content -LiteralPath (Join-Path $product 'standalone-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.launchWorkflow.mode -ne 'manual-official-launcher' -or
    $manifest.launchWorkflow.startsGameExecutable -ne $false) { throw 'Not a manual-launch archive.' }
$checksums = if ($Diagnostic) { 'SHA256SUMS-diagnostic.txt' } else { 'SHA256SUMS.txt' }
$verified = 0
foreach ($line in [IO.File]::ReadAllLines((Join-Path $product $checksums))) {
    if ($line -notmatch '^([0-9a-fA-F]{64}) \*(.+)$') { throw 'Invalid checksum entry.' }
    $expected = $Matches[1]
    $relative = $Matches[2]
    if ($relative -match '(^|[\\/])\.\.?([\\/]|$)|:|^[\\/]') { throw 'Unsafe checksum path.' }
    if ((Get-FileHash -LiteralPath (Join-Path $product $relative)).Hash -ine $expected) {
        throw "Archive hash mismatch: $relative"
    }
    $verified++
}
foreach ($item in Get-ChildItem -LiteralPath $product -Recurse -File) {
    if ($item.Name -in @('launch-settings.json', 'd3dx_user.ini', 'd3d11_log.txt', 'launcher.log') -or
        $item.FullName -match '[\\/]FrameAnalysis-[^\\/]+') { throw "Private runtime state in archive: $($item.Name)" }
}
$pairs = @(
    @('step_cs.hlsl','step_cs.cs_5_0.8000.bin'),
    @('character_pick_ps.hlsl','character_pick_ps.ps_5_0.8000.bin'),
    @('pick_1479b2b594b9c91a.hlsl','pick_1479b2b594b9c91a.vs_5_0.8000.bin'),
    @('pick_c6e55aaa8f4b3218.hlsl','pick_c6e55aaa8f4b3218.vs_5_0.8000.bin')
)
if ($Diagnostic) { $pairs += ,@('diagnostic_cs.hlsl','diagnostic_cs.cs_5_0.8000.bin') }
$shaders = Join-Path $product 'EFMI\Mods\EndfieldJiggleEFMI\shaders'
foreach ($pair in $pairs) {
    if ([IO.File]::GetLastWriteTimeUtc((Join-Path $shaders $pair[0])).ToFileTimeUtc() -ne
        [IO.File]::GetLastWriteTimeUtc((Join-Path $shaders $pair[1])).ToFileTimeUtc()) {
        throw "Archive shader/cache FILETIME mismatch: $($pair[0])"
    }
}
if ($Diagnostic) {
    & (Join-Path $PSScriptRoot 'Test-DiagnosticStandalone.ps1') -PackageDirectory $product
}
$report = [ordered]@{
    archive = [IO.Path]::GetFullPath($Archive); product = $product; verifiedFiles = $verified
    shaderCachePairs = $pairs.Count; launchMode = $manifest.launchWorkflow.mode
    privateStateIncluded = $false; gameStarted = $false; liveInstallationModified = $false
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'archive-test-report.json'),
    ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output "PASS: $verified archive file checksums, $($pairs.Count) shader/cache pairs, no private state."
