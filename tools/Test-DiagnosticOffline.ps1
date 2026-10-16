[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$LoaderHost,
    [Parameter(Mandatory)][string[]]$ShaderFixtures,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Preserve existing offline evidence.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$results = @()
foreach ($mode in @('default','info')) {
    $sandbox = Join-Path $OutputDirectory $mode
    New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
    foreach ($name in @('Core','Mods','d3d11.dll','d3dcompiler_47.dll')) {
        Copy-Item -LiteralPath (Join-Path $PackageDirectory "EFMI\$name") -Destination $sandbox -Recurse
    }
    Copy-Item -LiteralPath $LoaderHost -Destination (Join-Path $sandbox 'loader_host.exe')
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    $level = if ($mode -eq 'info') { 'log_level = info' } else { '' }
    $config = @"
[Include]
include = Core\EFMI\main.ini
include_recursive = Mods
exclude_recursive = DISABLED*
[Logging]
$level
calls = 0
input = 1
debug = 0
unbuffered = 1
show_warnings = 1
[System]
proxy_d3d11 = C:\Windows\System32\d3d11.dll
skip_early_includes_load = 0
allow_platform_update = 1
allow_check_interface = 1
[Rendering]
override_directory = ShaderFixes
cache_directory = ShaderCache
storage_directory = ShaderFromGame
shader_hash = 3dmigoto
track_region_hashes = 1
cache_shaders = 0
ini_params = 120
assemble_signature_comments = 1
disassemble_undecipherable_custom_data = 1
patch_assembly_cb_offsets = 1
recursive_include = 1
export_shaders = 0
[Hunting]
hunting = 0
[Stereo]
automatic_mode = 0
"@
    [IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'), $config, [Text.UTF8Encoding]::new($false))
    $telemetry = Join-Path $sandbox 'Mods\EndfieldJiggleEFMI\DiagnosticTelemetry.ini'
    $text = [IO.File]::ReadAllText($telemetry).Replace('global $diagnostic_gpu = 0','global $diagnostic_gpu = 1')
    [IO.File]::WriteAllText($telemetry, $text, [Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $sandbox 'loader_host.exe'))
    $start.WorkingDirectory = $sandbox
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add((Join-Path $sandbox 'd3d11.dll'))
    foreach ($shader in $ShaderFixtures) { $start.ArgumentList.Add($shader) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw 'Offline host timed out.' }
    $output = $stdout.Result + $stderr.Result
    [IO.File]::WriteAllText((Join-Path $sandbox 'host-output.txt'), $output)
    $logPath = Join-Path $sandbox 'd3d11_log.txt'
    $log = if (Test-Path $logPath) { [IO.File]::ReadAllText($logPath) } else { '' }
    $failures = @($log -split '\r?\n' | Where-Object {
        $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
    })
    $results += [ordered]@{
        mode = $mode; exit = $process.ExitCode
        hasLog = Test-Path $logPath; logBytes = if ($log) { (Get-Item $logPath).Length } else { 0 }
        failures = $failures
        srvRestores = ([regex]::Matches($output,'srv-restored')).Count
        telemetryReferenced = $log.Contains('diagnostic_cs')
    }
    if ($process.ExitCode -ne 0 -or $failures.Count) {
        throw "Offline $mode host failed: $output $($failures -join '`n')"
    }
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'test-report.json'),
    ([ordered]@{ results = $results; gameStarted = $false } | ConvertTo-Json -Depth 10))
$results | Format-Table
