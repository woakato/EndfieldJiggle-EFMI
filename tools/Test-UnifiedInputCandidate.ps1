[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$LoaderHost,
    [Parameter(Mandatory)][string[]]$ShaderFixtures
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$reportPath = Assert-UniversalPath (Join-Path $root 'reports') $BuildReportPath
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$package = Assert-UniversalPath (Split-Path -Parent $reportPath) $report.packageDirectory
$output = Join-Path (Split-Path -Parent $reportPath) ('loader-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path (Join-Path $output 'Mods'),(Join-Path $output 'ShaderFixes') -Force | Out-Null
foreach ($file in $report.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $file.path)).Hash -cne $file.sha256) {
        throw "Candidate identity changed: $($file.path)"
    }
}
foreach ($name in @('Core','d3d11.dll','d3dcompiler_47.dll')) {
    Copy-Item -LiteralPath (Join-Path $LoaderDirectory $name) -Destination $output -Recurse
}
Copy-Item -LiteralPath $LoaderHost -Destination (Join-Path $output 'loader_host.exe')
$target = Join-Path $output 'Mods\EndfieldJiggleEFMI'
Copy-Item -LiteralPath $package -Destination $target -Recurse
$scenarios = @'
namespace = EndfieldJiggleEFMI
[Constants]
global $test_failures = 0
global $test_result = -1
[Present]
run = CommandListInputTest
[CommandListInputTest]
if $enabled != 1 || $diagnostic != 0 || $notice_until != 0 || $cursor_flip_y != 1
    $test_failures = $test_failures + 1
endif
$enabled = 0
$diagnostic = 1
run = CommandListToggleTouch
if $enabled != 1 || $diagnostic != 0 || $notice_until != 0
    $test_failures = $test_failures + 1
endif
run = CommandListToggleTouch
if $enabled != 0
    $test_failures = $test_failures + 1
endif
run = CommandListToggleTouch
if $enabled != 1 || $diagnostic != 0 || $notice_until != 0
    $test_failures = $test_failures + 1
endif
$pick_started = 0
$pick_program = 0
$drag = 1
run = CommandListPickCursor
if $pick_drag != 1 || $pick_started != 1
    $test_failures = $test_failures + 1
endif
$drag = 0
run = CommandListPickCursor
if $pick_drag != 1
    $test_failures = $test_failures + 1
endif
$pick_cursor_x = 240
$pick_cursor_y = 180
$pick_width = 1280
$pick_height = 720
$cursor_x = 1000
$cursor_y = 650
$width = 1920
$height = 1080
$pick_drag = 0
$drag = 1
run = CommandListStep
if $cursor_x != 240 || $cursor_y != 180 || $width != 1280 || $height != 720
    $test_failures = $test_failures + 1
endif
$pick_drag = 1
run = CommandListStep
$drag = 0
run = CommandListStep
$test_result = $test_failures
'@
[IO.File]::WriteAllText((Join-Path $target 'InputTest.ini'),$scenarios)
$config = @'
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
[Hunting]
hunting = 0
[Stereo]
automatic_mode = 0
'@
[IO.File]::WriteAllText((Join-Path $output 'd3dx.ini'),$config)
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $output 'loader_host.exe'))
$start.WorkingDirectory=$output
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.RedirectStandardOutput=$true
$start.RedirectStandardError=$true
$start.ArgumentList.Add((Join-Path $output 'd3d11.dll'))
$start.ArgumentList.Add('--eligibility')
foreach ($shader in $ShaderFixtures) { $start.ArgumentList.Add((Resolve-Path -LiteralPath $shader).Path) }
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
if (!$process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw 'Isolated loader timed out.' }
$text = $stdout.Result+$stderr.Result
[IO.File]::WriteAllText((Join-Path $output 'host-output.txt'),$text)
$log = [IO.File]::ReadAllText((Join-Path $output 'd3d11_log.txt'))
$errors = @($log -split '\r?\n' | Where-Object {
    $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
})
if ($process.ExitCode -ne 0 -or $errors.Count) { throw "Loader failed: $($errors -join '; ')" }
$result = [regex]::Matches($log,
    '(?m)^FrameAnalysisContext[^\r\n]*\$test_result = \$test_failures\r?\n[^\r\n]*= ([0-9.]+)')
if ($result.Count -ne 1 -or [double]$result[0].Groups[1].Value -ne 0) { throw 'Input command-list assertions failed.' }
$heldValues = @([regex]::Matches($log,
    '(?m)^FrameAnalysisContext[^\r\n]*x241 = \$drag && \$pick_drag\r?\n[^\r\n]*= ([0-9.]+)') |
    ForEach-Object { [double]$_.Groups[1].Value })
if ($heldValues.Count -lt 3 -or ($heldValues[-3..-1] -join ',') -cne '0,1,0') {
    throw "The shader did not receive delayed press / active hold / immediate release: $heldValues"
}
if ([regex]::Matches($text,'srv-restored').Count -ne $ShaderFixtures.Count) { throw 'Foreign SRV restoration failed.' }
$receipt = Join-Path $output 'test-report.json'
[ordered]@{
    passed=$true; loaderExit=$process.ExitCode; diagnostics=$errors; programs=$ShaderFixtures.Count
    commandAssertions=7; shaderHeldSequence=$heldValues[-3..-1]
    startupSilent=$true; toggleOnOffOn=$true; frozenCursorAndViewport=$true
    delayedPress=$true; immediateRelease=$true
    physicalKeyPollingTested=$false; inGameVerified=$false; liveInstallationModified=$false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receipt -Encoding utf8
Write-Output "PASS: Alt-plus-mouse defaults, toggle cycle, cursor snapshot and press/release transitions."
Write-Output "UNIFIED_INPUT_TEST_REPORT=$receipt"
