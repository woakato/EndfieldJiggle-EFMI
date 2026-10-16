[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseStandaloneDirectory,
    [string]$OutputDirectory,
    [string]$FxcPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\fxc.exe'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$base = (Resolve-Path -LiteralPath $BaseStandaloneDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $base 'standalone-manifest.json'))) {
    throw 'BaseStandaloneDirectory must contain standalone-manifest.json.'
}
if (!(Test-Path -LiteralPath (Join-Path $base 'EFMI\Mods\EndfieldJiggleEFMI'))) {
    throw 'BaseStandaloneDirectory does not contain the published touch Mod.'
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $root ('reports\diagnostic-standalone\build-' +
        (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$product = Join-Path $output 'package\EndfieldJiggleDiagnostic'
if (Test-Path -LiteralPath $output) { throw "Preserve existing diagnostic build: $output" }
if ($output.StartsWith($base.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build outputs may not be placed inside the reference package.'
}
if (!(Test-Path -LiteralPath $FxcPath -PathType Leaf)) { throw 'Supply a Windows SDK fxc.exe.' }
New-Item -ItemType Directory -Path $product -Force | Out-Null
$baseChecksums = Join-Path $base 'SHA256SUMS.txt'
# Only copy verified original release entries, never arbitrary files from a used install.
foreach ($line in [IO.File]::ReadAllLines($baseChecksums)) {
    if ($line -notmatch '^([0-9a-fA-F]{64}) \*(.+)$') { throw 'Invalid base checksum line.' }
    $hash = $Matches[1]
    $relative = $Matches[2]
    if ($relative -match '(^|/)\.\.?(/|$)|:|^[\\/]' ) { throw 'Unsafe base checksum path.' }
    $source = Join-Path $base $relative
    $target = Join-Path $product $relative
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $hash) {
        throw "Reference release bytes differ: $relative"
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
New-Item -ItemType Directory -Path (Join-Path $product 'EFMI\ShaderCache') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $product 'EFMI\ShaderFixes') -Force | Out-Null

function Set-IniValue([string]$Path, [string]$Section, [string]$Key, [string]$Value, [bool]$AllowNew = $false) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($Path)) { $lines.Add($line) }
    $inSection = $false
    $found = $false
    $sectionStart = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $trimmed = $lines[$index].Trim()
        if ($trimmed.StartsWith('[') -and $trimmed.EndsWith(']')) {
            $inSection = $trimmed.Substring(1, $trimmed.Length - 2).
                Equals($Section, [StringComparison]::OrdinalIgnoreCase)
            if ($inSection) { $sectionStart = $index }
            continue
        }
        if (!$inSection) { continue }
        $active = $trimmed.Split(';', 2)[0].Trim()
        $separator = $active.IndexOf('=')
        if ($separator -lt 1 -or !$active.Substring(0, $separator).Trim().
                Equals($Key, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($found) { throw "Duplicate INI key: [$Section] $Key" }
        $lines[$index] = "$Key = $Value"
        $found = $true
    }
    if (!$found) {
        if (!$AllowNew -or $sectionStart -lt 0) { throw "Missing INI key: [$Section] $Key" }
        $lines.Insert($sectionStart + 1, "$Key = $Value")
    }
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

$d3dx = Join-Path $product 'EFMI\d3dx.ini'
Set-IniValue $d3dx 'Logging' 'calls' '0'
Set-IniValue $d3dx 'Logging' 'log_level' 'info' $true
Set-IniValue $d3dx 'Logging' 'input' '1'
Set-IniValue $d3dx 'Logging' 'debug' '0'
Set-IniValue $d3dx 'Logging' 'unbuffered' '1'
Set-IniValue $d3dx 'Logging' 'show_warnings' '1'
Set-IniValue $d3dx 'Logging' 'crash' '0'
Set-IniValue $d3dx 'Hunting' 'hunting' '0'
Set-IniValue $d3dx 'Hunting' 'analyse_frame' ''

$runtime = Join-Path $product 'EFMI\Mods\EndfieldJiggleEFMI'
$runtimeIni = Join-Path $runtime 'EndfieldJiggle.ini'
$text = [IO.File]::ReadAllText($runtimeIni)
if ([regex]::Matches($text, '(?m)^run = CommandListStep\r?$').Count -ne 1) {
    throw 'Unknown Present hook: cannot attach diagnostic telemetry.'
}
$text = [regex]::Replace($text, '(?m)^run = CommandListStep\r?$',
    "run = CommandListStep`nrun = CommandListDiagnosticTelemetry")
[IO.File]::WriteAllText($runtimeIni, $text, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $root 'runtime\DiagnosticTelemetry.ini') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $root 'shaders\diagnostic_cs.hlsl') -Destination (Join-Path $runtime 'shaders')
$telemetrySource = Join-Path $runtime 'shaders\diagnostic_cs.hlsl'
$telemetryCache = Join-Path $runtime 'shaders\diagnostic_cs.cs_5_0.8000.bin'
& $FxcPath /nologo /T cs_5_0 /E main /Fo $telemetryCache $telemetrySource
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic compute shader compilation failed.' }
[IO.File]::SetLastWriteTimeUtc($telemetryCache, [IO.File]::GetLastWriteTimeUtc($telemetrySource))
# Compile all HUD stages offline; the official source is left untouched in the package.
foreach ($stage in @('VERTEX','GEOMETRY','PIXEL')) {
    $profile = @{ VERTEX = 'vs_5_0'; GEOMETRY = 'gs_5_0'; PIXEL = 'ps_5_0' }[$stage]
    & $FxcPath /nologo /Od /T $profile /E main /D "$($stage)_SHADER=1" `
        /Fo (Join-Path $output "hud-$stage.bin") (Join-Path $product 'EFMI\Core\Debugger\Shaders\debug_cb.hlsl')
    if ($LASTEXITCODE -ne 0) { throw "Diagnostic HUD $stage shader compilation failed." }
}

foreach ($file in @(
        @{ Source = 'tools\Collect-Diagnostics.ps1'; Name = 'Collect-Diagnostics.ps1' },
        @{ Source = 'tools\Collect-Diagnostics.cmd'; Name = 'Collect-Diagnostics.cmd' },
        @{ Source = 'tools\DiagnosticEnvironment.cs'; Name = 'DiagnosticEnvironment.cs' },
        @{ Source = 'docs\DIAGNOSTIC-STANDALONE.md'; Name = 'DIAGNOSTIC-README-zh-CN.md' })) {
    Copy-Item -LiteralPath (Join-Path $root $file.Source) `
        -Destination (Join-Path $product $file.Name)
}

$manifestPath = Join-Path $product 'standalone-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (!$manifest.PSObject.Properties['launchWorkflow'] -or
    $manifest.launchWorkflow.mode -ne 'manual-official-launcher' -or
    $manifest.launchWorkflow.startsGameExecutable -ne $false) {
    throw 'Rebuild the base standalone with the manual official-launcher workflow first.'
}
$manifest | Add-Member -NotePropertyName diagnostic -NotePropertyValue ([ordered]@{
    logging = [ordered]@{ level = 'info'; calls = 0; input = 1; debug = 0; unbuffered = 1; showWarnings = 1; crash = 0 }
    revision = 3
    telemetryRevision = 2
    frameAnalysisEnabled = $false
    gpuTelemetryKey = 'Ctrl+Shift+F6'
    fullD3d11LogIncluded = $false
    collector = 'Collect-Diagnostics.cmd'
}) -Force
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12) + "`n",
    [Text.UTF8Encoding]::new($false))

$sourceZip = Join-Path $product 'Sources\diagnostic-tools-source.zip'
$sourceArchive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in @('tools\Build-DiagnosticStandalone.ps1','tools\Collect-Diagnostics.ps1',
        'tools\Collect-Diagnostics.cmd','tools\DiagnosticEnvironment.cs','tools\Test-DiagnosticStandalone.ps1',
        'tools\Test-DiagnosticOffline.ps1','tools\Test-DiagnosticCollector.ps1',
        'runtime\DiagnosticTelemetry.ini','shaders\diagnostic_cs.hlsl','docs\DIAGNOSTIC-STANDALONE.md','LICENSE')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive,
            (Join-Path $root $relative), $relative.Replace('\','/')) | Out-Null
    }
} finally { $sourceArchive.Dispose() }
$files = @(Get-ChildItem -LiteralPath $product -Recurse -File | ForEach-Object {
    [ordered]@{
        path = [IO.Path]::GetRelativePath($product, $_.FullName).Replace('\', '/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
[IO.File]::WriteAllText((Join-Path $product 'diagnostic-checksums.json'),
    ([ordered]@{ schema = 1; revision = 3; files = $files } | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
$files += [ordered]@{
    path = 'diagnostic-checksums.json'
    bytes = (Get-Item (Join-Path $product 'diagnostic-checksums.json')).Length
    sha256 = (Get-FileHash (Join-Path $product 'diagnostic-checksums.json')).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText((Join-Path $product 'SHA256SUMS-diagnostic.txt'),
    (($files | ForEach-Object { "$($_.sha256) *$($_.path)" }) -join "`n") + "`n",
    [Text.UTF8Encoding]::new($false))

$zip = Join-Path $output 'EndfieldJiggleEFMI-v0.2.2-standalone-diagnostic-win64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $output 'package'), $zip,
    [IO.Compression.CompressionLevel]::Optimal, $false)
$report = [ordered]@{
    schema = 1
    version = 'v0.2.2-diagnostic'
    revision = 3
    launchWorkflow = $manifest.launchWorkflow
    packageDirectory = $product
    archive = $zip
    archiveSha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    baseStandaloneDirectory = $base
    liveInstallationModified = $false
    gameStarted = $false
    frameAnalysisEnabled = $false
    fullD3d11LogIncluded = $false
    logging = $manifest.diagnostic.logging
    files = $files
}
[IO.File]::WriteAllText((Join-Path $output 'build-report.json'),
    ($report | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "DIAGNOSTIC_PACKAGE=$product"
Write-Output "DIAGNOSTIC_ZIP=$zip"
Write-Output "DIAGNOSTIC_REPORT=$(Join-Path $output 'build-report.json')"
