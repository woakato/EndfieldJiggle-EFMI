[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$output = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $output) { throw 'Preserve existing input guard integration evidence.' }
$report = Get-Content -LiteralPath $BuildReportPath -Raw | ConvertFrom-Json
foreach ($file in $report.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $report.packageDirectory $file.path)).Hash -cne $file.sha256) {
        throw 'Input guard package changed since its build.'
    }
}
$native = Join-Path (Split-Path -Parent $BuildReportPath) 'native\Release'
$results = @()
foreach ($hide in @(0,1)) {
    $sandbox = Join-Path $output "official-cursor-hooks-$hide"
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'Mods'),(Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    foreach ($name in @('Core','d3d11.dll','d3dcompiler_47.dll')) {
        Copy-Item -LiteralPath (Join-Path $LoaderDirectory $name) -Destination $sandbox -Recurse
    }
    Copy-Item -LiteralPath $RuntimeDirectory -Destination (Join-Path $sandbox 'Mods\EndfieldJiggleEFMI') -Recurse
    foreach ($name in @('input_guard_tests.exe','input_cursor_probe.dll')) {
        Copy-Item -LiteralPath (Join-Path $native $name) -Destination $sandbox
    }
    $config = @"
[Include]
include = Core\EFMI\main.ini
include_recursive = Mods
[Logging]
log_level = debug
calls = 1
debug = 1
unbuffered = 1
[ClearRenderTargetView]
[ClearDepthStencilView]
[ClearUnorderedAccessViewUint]
[ClearUnorderedAccessViewFloat]
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
cache_shaders = 0
ini_params = 120
assemble_signature_comments = 1
disassemble_undecipherable_custom_data = 1
patch_assembly_cb_offsets = 1
recursive_include = 1
hide_cursor = $hide
[Hunting]
hunting = 0
[Stereo]
automatic_mode = 0
"@
    [IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'),$config,[Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $sandbox 'input_guard_tests.exe'))
    $start.WorkingDirectory = $sandbox
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add('--efmi')
    $start.ArgumentList.Add((Join-Path $sandbox 'd3d11.dll'))
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(45000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'Isolated input guard integration timed out.'
    }
    $text = $stdout.Result + $stderr.Result
    [IO.File]::WriteAllText((Join-Path $sandbox 'host-output.txt'),$text)
    if ($process.ExitCode -ne 0) { throw "Input guard integration failed: $text" }
    $log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
    $errors = @($log -split '\r?\n' | Where-Object {
        $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
    })
    if ($errors.Count) { throw "Official EFMI diagnostics: $($errors -join '; ')" }
    $results += [ordered]@{officialCursorHooks=$hide;passed=$true;output=$text;sandbox=$sandbox}
}
[ordered]@{
    passed=$true;results=$results;inGameVerified=$false;liveInstallationModified=$false
    limitations=@('No physical Alt key polling, real game camera, cutscene or GetCursorInfo hardware-motion acceptance.')
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'test-report.json') -Encoding utf8
Write-Output 'PASS: input guard and official EFMI execute together with cursor hooks disabled and enabled.'
