[CmdletBinding()]
param(
    [string]$PackageDirectory = $PSScriptRoot,
    [string]$OutputDirectory
)
# ASCII source deliberately supports Windows PowerShell 5.1 without a BOM.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path.TrimEnd('\')
$efmi = Join-Path $package 'EFMI'
if (!(Test-Path -LiteralPath (Join-Path $efmi 'd3dx.ini'))) {
    throw 'Select the diagnostic package directory containing EFMI\d3dx.ini.'
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $package ('Diagnostics\diagnostic-' +
        (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; existing reports are preserved.' }
$zip = $output + '.zip'
if (Test-Path -LiteralPath $zip) { throw 'An output ZIP already exists.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$redactPaths = [Collections.Generic.List[string]]::new()
foreach ($path in @($package, $efmi, $env:USERPROFILE, $env:LOCALAPPDATA)) {
    if (![string]::IsNullOrWhiteSpace($path)) { $redactPaths.Add($path) }
}
$gameDirectory = $null
$selectionError = $null
$settingsPath = Join-Path $package 'launch-settings.json'
if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    try {
        if ((Get-Item -LiteralPath $settingsPath).Length -gt 64KB) { throw 'Launch settings too large.' }
        $saved = [IO.File]::ReadAllText($settingsPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        $gameExecutable = [IO.Path]::GetFullPath([string]$saved.gameExecutable)
        $gameDirectory = Split-Path -Parent $gameExecutable
        $redactPaths.Add($gameExecutable)
        $redactPaths.Add($gameDirectory)
    } catch { $selectionError = 'Unable to read the saved game selection.' }
}

function Redact([string]$Text) {
    if ($null -eq $Text) { return '' }
    $result = $Text
    foreach ($path in @($redactPaths | Select-Object -Unique | Sort-Object Length -Descending)) {
        foreach ($variant in @($path, $path.Replace('\', '/'))) {
            $result = [regex]::Replace($result, [regex]::Escape($variant),
                '<PATH>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        }
    }
    $result = [regex]::Replace($result, '(?i)(UID|user[_ ]?id|account[_ ]?id)\s*[:=]\s*[\w-]+', '$1=<REDACTED>')
    return [regex]::Replace($result, '(?i)[A-Z]:[\\/][^\r\n"<>|]*', '<PATH>')
}
function Write-Json([string]$Name, [object]$Value) {
    [IO.File]::WriteAllText((Join-Path $output $Name), ($Value | ConvertTo-Json -Depth 16) + "`n", $utf8)
}
function Assert-LocalFile([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or
        @($Relative -split '[\\/]' | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw 'Unsafe relative path in diagnostic checksum manifest.'
    }
    $path = [IO.Path]::GetFullPath((Join-Path $package $Relative))
    if (!$path.StartsWith($package + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside package.' }
    $current = $path
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Reparse points are not followed.'
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $path
}

$launchMode = 'unknown'
$manifestError = $null
try {
    $manifestPath = Assert-LocalFile 'standalone-manifest.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        if ((Get-Item -LiteralPath $manifestPath).Length -gt 1MB) { throw 'Standalone manifest too large.' }
        $manifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($null -ne $manifest -and $manifest.PSObject.Properties['launchWorkflow']) {
            $workflow = $manifest.launchWorkflow
            if ($null -ne $workflow -and $workflow.PSObject.Properties['mode']) {
                if ($workflow.mode -isnot [string] -or [string]::IsNullOrWhiteSpace($workflow.mode) -or
                    $workflow.mode.Length -gt 128) { throw 'Invalid launch workflow mode.' }
                $launchMode = Redact $workflow.mode
            }
        }
    }
} catch { $manifestError = 'Standalone manifest missing, unreadable or invalid; launch mode is unknown.' }

$baselinePath = Join-Path $package 'diagnostic-checksums.json'
$baseline = $null
$baselineError = $null
try {
    if ((Get-Item -LiteralPath $baselinePath).Length -gt 1MB) { throw 'Checksum manifest too large.' }
    $baseline = [IO.File]::ReadAllText($baselinePath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($baseline.schema -ne 1 -or @($baseline.files).Count -gt 256) { throw 'Invalid checksum manifest.' }
} catch { $baseline = $null; $baselineError = 'Checksum manifest missing or invalid; integrity cannot be established.' }
$records = [Collections.Generic.List[object]]::new()
if ($baseline) {
    foreach ($expected in $baseline.files) {
        $record = [ordered]@{ path = $expected.path; status = 'unreadable'; expectedSha256 = $expected.sha256 }
        try {
            $path = Assert-LocalFile $expected.path
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $record.status = 'missing' }
            else {
                $file = Get-Item -LiteralPath $path
                $record.bytes = $file.Length
                $record.fileTime = $file.LastWriteTimeUtc.ToFileTimeUtc()
                $record.lastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('o')
                if ($file.Length -ne $expected.bytes) {
                    $record.status = 'size-mismatch'
                } else {
                    $record.sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
                    $record.status = if ($record.sha256 -eq $expected.sha256) { 'ok' } else { 'hash-mismatch' }
                }
            }
        } catch { $record.error = Redact $_.Exception.Message }
        $records.Add($record)
    }
}
$shaderRoot = 'EFMI/Mods/EndfieldJiggleEFMI/shaders/'
$pairs = @(
    @('diagnostic_cs.hlsl', 'diagnostic_cs.cs_5_0.8000.bin'),
    @('step_cs.hlsl', 'step_cs.cs_5_0.8000.bin'),
    @('character_pick_ps.hlsl', 'character_pick_ps.ps_5_0.8000.bin'),
    @('pick_1479b2b594b9c91a.hlsl', 'pick_1479b2b594b9c91a.vs_5_0.8000.bin'),
    @('pick_c6e55aaa8f4b3218.hlsl', 'pick_c6e55aaa8f4b3218.vs_5_0.8000.bin')
) | ForEach-Object {
    $left = Join-Path $package ($shaderRoot + $_[0])
    $right = Join-Path $package ($shaderRoot + $_[1])
    $sourceTime = $null
    $cacheTime = $null
    if (Test-Path -LiteralPath $left -PathType Leaf) { $sourceTime = [IO.File]::GetLastWriteTimeUtc($left).ToFileTimeUtc() }
    if (Test-Path -LiteralPath $right -PathType Leaf) { $cacheTime = [IO.File]::GetLastWriteTimeUtc($right).ToFileTimeUtc() }
    [ordered]@{
        source = $shaderRoot + $_[0]; cache = $shaderRoot + $_[1]
        sameFileTime = $null -ne $sourceTime -and $null -ne $cacheTime -and $sourceTime -eq $cacheTime
        sourceFileTime = $sourceTime; cacheFileTime = $cacheTime
    }
}
Write-Json 'package-files.json' ([ordered]@{
    baselineError = $baselineError
    requiredFiles = $records.ToArray()
    shaderCachePairs = @($pairs)
    note = 'Modified INIs may be intentional settings changes. No files or timestamps were repaired.'
})
$pathRecords = [Collections.Generic.List[object]]::new()
$pathErrors = [Collections.Generic.List[string]]::new()
$longestCachePath = 0
if ($baseline) {
    foreach ($expected in $baseline.files) {
        if (!([string]$expected.path).EndsWith('.hlsl', [StringComparison]::OrdinalIgnoreCase)) { continue }
        try {
            $source = Assert-LocalFile $expected.path
            $cachePath = [IO.Path]::ChangeExtension($source, 'cs_5_0.ffffffff.bin')
            $longestCachePath = [Math]::Max($longestCachePath, $cachePath.Length)
            $pathRecords.Add([ordered]@{
                source = $expected.path; sourceLength = $source.Length
                maximumCacheLength = $cachePath.Length; exceedsLimit = $cachePath.Length -gt 259
            })
        } catch { $pathErrors.Add((Redact $_.Exception.Message)) }
    }
}
Write-Json 'path-budget.json' ([ordered]@{
    limit = 259; longestCacheLength = $longestCachePath
    verifiedBaseline = $null -ne $baseline
    shaders = $pathRecords.ToArray(); errors = $pathErrors.ToArray()
    note = 'Published HLSL paths only. Cache names reserve profile and eight hex flag digits. External custom includes are not covered. No files were moved.'
})

$systemErrors = [Collections.Generic.List[string]]::new()
$gpu = @()
$os = $null
try {
    $gpu = @(Get-CimInstance Win32_VideoController -OperationTimeoutSec 10 -ErrorAction Stop |
        Select-Object Name, DriverVersion, DriverDate, VideoModeDescription)
} catch { $systemErrors.Add('GPU information query failed.') }
try {
    $os = Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 10 -ErrorAction Stop |
        Select-Object Caption, Version, BuildNumber
} catch { $systemErrors.Add('Windows information query failed.') }
$system32 = if ([Environment]::Is64BitOperatingSystem -and ![Environment]::Is64BitProcess) {
    Join-Path $env:WINDIR 'Sysnative'
} else { Join-Path $env:WINDIR 'System32' }
$vc = @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll') | ForEach-Object {
    $path = Join-Path $system32 $_
    [ordered]@{
        name = $_; exists = Test-Path -LiteralPath $path -PathType Leaf
        version = if (Test-Path -LiteralPath $path -PathType Leaf) { (Get-Item -LiteralPath $path).VersionInfo.FileVersion } else { $null }
    }
}
$native = $null
try {
    $probeRecord = @($records | Where-Object { $_.path -eq 'DiagnosticEnvironment.cs' })
    $loaderRecord = @($records | Where-Object { $_.path -eq 'EFMI/3dmloader.dll' })
    if ($probeRecord.Count -ne 1 -or $probeRecord[0].status -ne 'ok' -or
        $loaderRecord.Count -ne 1 -or $loaderRecord[0].status -ne 'ok') {
        throw 'Native probe source or official loader did not pass integrity check.'
    }
    if (!('EndfieldDiagnosticEnvironment' -as [type])) {
        Add-Type -Path (Join-Path $package 'DiagnosticEnvironment.cs')
    }
    $native = [EndfieldDiagnosticEnvironment]::Inspect((Join-Path $efmi '3dmloader.dll'))
} catch { $systemErrors.Add('Native dependency/D3D11 probe failed: ' + (Redact $_.Exception.Message)) }
Write-Json 'system.json' ([ordered]@{
    collectedAt = [DateTimeOffset]::Now.ToString('o')
    os = $os; gpu = $gpu; vcRuntime = @($vc)
    is64BitProcess = [Environment]::Is64BitProcess
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    nativeProbe = $native; errors = $systemErrors.ToArray()
})

$configuration = [Collections.Generic.List[string]]::new()
foreach ($relative in @('EFMI/d3dx.ini', 'EFMI/Core/EFMI/KeyBindings.ini',
        'EFMI/Mods/EndfieldJiggleEFMI/EndfieldJiggle.ini',
        'EFMI/Mods/EndfieldJiggleEFMI/DiagnosticTelemetry.ini')) {
    $configuration.Add('--- ' + $relative + ' ---')
    try {
        $path = Assert-LocalFile $relative
        if ((Get-Item -LiteralPath $path).Length -gt 1MB) { throw 'Config too large.' }
        foreach ($line in ([IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) -split '\r?\n')) {
            if ($line -match '^\s*\[|^\s*(namespace|key|type|loader|target|module|include|include_recursive|exclude_recursive|hunting|analyse_frame|analyse_options|reload_config|log_level|calls|input|debug|unbuffered|show_warnings|crash|global.*)\s*=') {
                $configuration.Add((Redact $line))
            }
        }
    } catch { $configuration.Add('Unavailable: ' + (Redact $_.Exception.Message)) }
}
[IO.File]::WriteAllLines((Join-Path $output 'config-summary.txt'), $configuration, $utf8)

function Read-Sample([IO.FileStream]$Stream, [long]$Offset, [int]$Count, [bool]$SkipPartialFirstLine) {
    $Stream.Position = $Offset
    $buffer = New-Object byte[] $Count
    $read = 0
    while ($read -lt $Count) {
        $amount = $Stream.Read($buffer, $read, $Count - $read)
        if ($amount -le 0) { break }
        $read += $amount
    }
    $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $read)
    $lines = $text -split '\r?\n'
    if ($SkipPartialFirstLine -and $lines.Count -gt 1) { $lines = $lines[1..($lines.Count - 1)] }
    return ,$lines
}
function Get-LogSample([string]$Path, [string]$Label) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return [ordered]@{ label = $Label; exists = $false } }
    $errors = [Collections.Generic.List[string]]::new()
    $shader = [Collections.Generic.List[string]]::new()
    $input = [Collections.Generic.List[string]]::new()
    $header = [Collections.Generic.List[string]]::new()
    $phases = [Collections.Generic.List[string]]::new()
    $latestPhase = 'unknown'
    $phaseCount = 0
    $isLauncher = $Label -in @('package/Logs/launcher.log', 'package/Logs/launcher.log.previous')
    $errorCount = 0
    $stream = $null
    try {
        $file = Get-Item -LiteralPath $Path
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $length = $stream.Length
        $headSize = [int][Math]::Min($length, 16MB)
        $tailSize = [int][Math]::Min($length - $headSize, 4MB)
        $samples = [Collections.Generic.List[object]]::new()
        $samples.Add((Read-Sample $stream 0 $headSize $false))
        if ($tailSize -gt 0) { $samples.Add((Read-Sample $stream ($length - $tailSize) $tailSize $true)) }
        foreach ($sample in $samples) {
            foreach ($rawLine in $sample) {
                if ($header.Count -lt 20) { $header.Add((Redact $rawLine.Substring(0, [Math]::Min(2000, $rawLine.Length)))) }
                if ($isLauncher -and $rawLine -cmatch '(?<![\w-])phase=(waiting-for-official-launch|game-detected|loading-efmi|efmi-loaded|cancelled|failed|timeout)(?=$|[\s,;])') {
                    $latestPhase = $Matches[1]
                    $phaseCount++
                    $phases.Add((Redact $rawLine.Substring(0, [Math]::Min(2000, $rawLine.Length))))
                    if ($phases.Count -gt 100) { $phases.RemoveAt(0) }
                }
                if ($rawLine -notmatch '(?i)ERROR|WARNING|failed|stale|exception|unrecogni[sz]ed|duplicate section|rejected|EndfieldJiggle|CustomShader|pick_|step_cs|ShaderRegex|DiagnosticTelemetry|compil|cached shader|D3D11CreateDevice|feature.?level|adapter|input|press|release|drag|F6|F8|F9|shift|LBUTTON|EFMI loaded|elevat') { continue }
                $line = $rawLine.Substring(0, [Math]::Min(2000, $rawLine.Length))
                $line = Redact $line
                if ($line -match '(?i)\bERROR\b|\bWARNING\b|failed|stale|missing or stale|exception|unrecogni[sz]ed|duplicate section|rejected') {
                    $errorCount++
                    if ($errors.Count -lt 400) { $errors.Add($line) }
                    else { $errors.RemoveAt(200); $errors.Add($line) }
                }
                if ($line -match '(?i)EndfieldJiggle|CustomShader|pick_|step_cs|ShaderRegex|DiagnosticTelemetry|compil|cached shader|D3D11CreateDevice|feature.?level|adapter') {
                    if ($shader.Count -lt 400) { $shader.Add($line) }
                    else { $shader.RemoveAt(200); $shader.Add($line) }
                }
                if ($line -match '(?i)\binput\b|key.*(?:press|release)|drag|F6|F8|F9|shift|LBUTTON|EFMI loaded|elevat') {
                    $input.Add($line)
                    if ($input.Count -gt 200) { $input.RemoveAt(0) }
                }
            }
        }
        return [ordered]@{
            label = $Label; exists = $true; bytes = $length
            lastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('o')
            headBytes = $headSize; tailBytes = $tailSize; omittedMiddleBytes = $length - $headSize - $tailSize
            sampledErrorCount = $errorCount; header = $header.ToArray()
            errorLines = $errors.ToArray(); shaderLines = $shader.ToArray(); inputLines = $input.ToArray()
            sampledPhaseEventCount = $phaseCount; phaseEvents = $phases.ToArray(); latestPhase = $latestPhase
            note = 'Bounded samples, not a full log. No errors in samples does not prove the whole log is error-free.'
        }
    } catch {
        return [ordered]@{ label = $Label; exists = $true; error = Redact $_.Exception.Message; note = 'Exit the game before collecting.' }
    } finally { if ($stream) { $stream.Dispose() } }
}
$logs = [Collections.Generic.List[object]]::new()
$candidates = @(
    @((Join-Path $efmi 'd3d11_log.txt'), 'package/EFMI/d3d11_log.txt'),
    @((Join-Path $package 'd3d11_log.txt'), 'package/d3d11_log.txt'),
    @((Join-Path $package 'Logs\launcher.log'), 'package/Logs/launcher.log'),
    @((Join-Path $package 'Logs\launcher.log.previous'), 'package/Logs/launcher.log.previous')
)
if ($gameDirectory) { $candidates += ,@((Join-Path $gameDirectory 'd3d11_log.txt'), 'game-directory/d3d11_log.txt') }
$seen = @{}
foreach ($candidate in $candidates) {
    if (!$seen.ContainsKey($candidate[0])) {
        $seen[$candidate[0]] = $true
        $logs.Add((Get-LogSample $candidate[0] $candidate[1]))
    }
}
Write-Json 'log-summary.json' ([ordered]@{
    logs = $logs.ToArray(); selectionError = $selectionError
    note = 'Per log: first 16 MiB plus last 4 MiB maximum. Errors are kept separately from repetitive input lines.'
})
$launcherLogs = @($logs | Where-Object { $_.label -like 'package/Logs/launcher.log*' } | ForEach-Object {
    $entry = [ordered]@{ label = $_.label; exists = $_.exists; latestPhase = 'unknown'; phaseEvents = @() }
    if ($_.Contains('phaseEvents')) {
        $entry.latestPhase = $_.latestPhase
        $entry.phaseEvents = $_.phaseEvents
        $entry.sampledPhaseEventCount = $_.sampledPhaseEventCount
    }
    if ($_.Contains('error')) { $entry.error = $_.error }
    $entry
})
Write-Json 'launch-summary.json' ([ordered]@{
    schema = 1; mode = $launchMode; manifestError = $manifestError
    latestPhase = $launcherLogs[0].latestPhase
    logs = $launcherLogs
    gameApiDx11 = 'unverified'
    warnings = @(
        'Launcher phase success does not prove touch success or GPU picking/motion functionality.',
        'Game API DX11 is unverified. Launcher phases and the native D3D11 probe do not verify the game rendering API.'
    )
    note = 'Per launcher log: at most 100 recent phase event strings from bounded head/tail samples, each limited to 2000 characters. Current and previous logs are separate; events may span launch attempts.'
})
[IO.File]::WriteAllLines((Join-Path $output 'README.txt'), @(
    'EndfieldJiggle diagnostic collection',
    'No complete logs, FrameAnalysis dumps, game resources or launch-settings.json are included.',
    'Personal paths are redacted on a best-effort basis, not a guarantee. Review the ZIP before sharing.',
    'GPU picking/motion values are in the in-game Ctrl+Shift+F6 HUD. Send those screenshots separately.',
    'No configuration, shader cache timestamps or game files were modified by this collector.'
), $utf8)
[IO.Compression.ZipFile]::CreateFromDirectory($output, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Output "DIAGNOSTICS_DIRECTORY=$output"
Write-Output "DIAGNOSTICS_ZIP=$zip"
