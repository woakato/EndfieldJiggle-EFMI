[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$AcceptedDirectory,
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
$output = Join-Path (Split-Path -Parent $reportPath) ('verification-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
foreach ($file in $report.files) {
    $path = Join-Path $package $file.path
    Check ((Get-FileHash -LiteralPath $path).Hash -ceq $file.sha256) "Candidate identity: $($file.path)"
    if ($file.path -ceq 'EndfieldJiggle.ini') { continue }
    Check ((Get-FileHash -LiteralPath (Join-Path $AcceptedDirectory $file.path)).Hash -ceq $file.sha256) `
        "Published payload unchanged: $($file.path)"
    Check $file.timePreserved "Source timestamp preserved: $($file.path)"
}
$before = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $reportPath) 'EndfieldJiggle.before.ini'))
$runtime = [IO.File]::ReadAllText((Join-Path $package 'EndfieldJiggle.ini'))
$oldSections = @(Read-OrderedIniSections $before)
$newSections = @(Read-OrderedIniSections $runtime)
foreach ($section in $oldSections | Where-Object name -CNotIn @('Constants','KeyDrag','CommandListStatus')) {
    $new = @($newSections | Where-Object name -CEQ $section.name)
    Check ($new.Count -eq 1 -and
        ($new[0].lines -join "`n").TrimEnd() -ceq ($section.lines -join "`n").TrimEnd()) `
        "Unchanged command/resource section: $($section.name)"
}
$newDrag = @($newSections | Where-Object name -CEQ 'KeyDrag')[0]
Check ((@($newSections | Where-Object name -CLike 'Key*' | ForEach-Object lines |
    Where-Object { $_ -match '^\s*\$drag\s*=' })).Count -eq 1) 'Only one hold binding writes drag'
Check (($newDrag.lines -join "`n") -match '(?m)^type = hold\r?$') 'Existing hold behavior retained'
Check ($runtime -match '(?m)^global \$cursor_flip_y = 1\r?$') 'Startup mapping remains character menu'
Check ($report.dragKeyAfter -ceq (($report.dragKeyBefore -split '\s+' |
    Where-Object { $_ -cne 'no_alt' }) -join ' ')) 'Only the extra Alt restriction is removed'

$scenarios = @'

[Constants]
global $world_test_failures = 0
global $world_test_result = -1

[CommandListWorldInputTest]
if $cursor_flip_y != 1
    $world_test_failures = $world_test_failures + 1
endif
$drag = 1
$ui_mouse = 1
run = CommandListToggleWorldCursorMapping
if $cursor_flip_y != 1
    $world_test_failures = $world_test_failures + 1
endif
$drag = 0
run = CommandListToggleWorldCursorMapping
if $cursor_flip_y != 1
    $world_test_failures = $world_test_failures + 1
endif
$drag = 1
$ui_mouse = 0
run = CommandListToggleWorldCursorMapping
if $cursor_flip_y != 1
    $world_test_failures = $world_test_failures + 1
endif
$drag = 0
$session_frames = 2
$complete = 1
run = CommandListToggleWorldCursorMapping
if $cursor_flip_y != 0 || $complete != 0 || $session_frames != 0 || $cancelled != 1
    $world_test_failures = $world_test_failures + 1
endif
$mod_id = 1
$\EFMIv1\enable_mods = 1
$color_draws = 1
$ui_navigation = 0
$cancelled = 0
run = CommandListUpdateNativeSession
if $complete != 0 || $session_frames != 1
    $world_test_failures = $world_test_failures + 1
endif
run = CommandListUpdateNativeSession
if $complete != 1 || $session_frames != 2
    $world_test_failures = $world_test_failures + 1
endif
$drag = 1
$ui_mouse = 1
run = CommandListUpdateNativeSession
if $complete != 1 || $cancelled != 0
    $world_test_failures = $world_test_failures + 1
endif
$drag = 0
run = CommandListUpdateNativeSession
if $complete != 0 || $cancelled != 1
    $world_test_failures = $world_test_failures + 1
endif
$ui_mouse = 0
$color_draws = 0
$cancelled = 0
run = CommandListUpdateNativeSession
if $complete != 0 || $cancelled != 1
    $world_test_failures = $world_test_failures + 1
endif
run = CommandListToggleWorldCursorMapping
if $cursor_flip_y != 1 || $complete != 0 || $session_frames != 0
    $world_test_failures = $world_test_failures + 1
endif
$world_test_result = $world_test_failures
'@
$loaderResults = @()
foreach ($mode in @('unchanged-candidate','mapping-transitions')) {
    $sandbox = Join-Path $output $mode
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'Mods'),(Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    foreach ($name in @('Core','d3d11.dll','d3dcompiler_47.dll')) {
        Copy-Item -LiteralPath (Join-Path $LoaderDirectory $name) -Destination $sandbox -Recurse
    }
    Copy-Item -LiteralPath $LoaderHost -Destination (Join-Path $sandbox 'loader_host.exe')
    $target = Join-Path $sandbox 'Mods\EndfieldJiggleEFMI'
    Copy-Item -LiteralPath $package -Destination $target -Recurse
    if ($mode -ceq 'mapping-transitions') {
        # Separate test include shares the namespace; the deliverable is unchanged.
        $testIni = "namespace = EndfieldJiggleEFMI`n[Present]`nrun = CommandListWorldInputTest`n"+$scenarios
        [IO.File]::WriteAllText((Join-Path $target 'WorldInputTest.ini'),$testIni,[Text.UTF8Encoding]::new($false))
    }
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
    [IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'),$config,[Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $sandbox 'loader_host.exe'))
    $start.WorkingDirectory = $sandbox
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add((Join-Path $sandbox 'd3d11.dll'))
    $start.ArgumentList.Add('--eligibility')
    foreach ($shader in $ShaderFixtures) { $start.ArgumentList.Add((Resolve-Path -LiteralPath $shader).Path) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'Isolated world-input loader timed out.'
    }
    $text = $stdout.Result+$stderr.Result
    [IO.File]::WriteAllText((Join-Path $sandbox 'host-output.txt'),$text)
    $log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
    $errors = @($log -split '\r?\n' | Where-Object {
        $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
    })
    Check ($process.ExitCode -eq 0 -and !$errors.Count) "Official EFMI loader without errors: $mode"
    Check ([regex]::Matches($text,'srv-restored').Count -eq $ShaderFixtures.Count) "Foreign shader resources restored: $mode"
    if ($mode -ceq 'mapping-transitions') {
        $result = [regex]::Matches($log,
            '(?m)^FrameAnalysisContext[^\r\n]*\$world_test_result = \$world_test_failures\r?\n[^\r\n]*= ([0-9.]+)')
        Check ($result.Count -eq 1 -and [double]$result[0].Groups[1].Value -eq 0) `
            'Loader executes all eleven mapping, held-input, warmup and missing-color assertions'
    }
    $loaderResults += [ordered]@{mode=$mode; sandbox=$sandbox; exit=$process.ExitCode; diagnostics=$errors}
}
foreach ($file in $report.files) {
    Check ((Get-FileHash -LiteralPath (Join-Path $report.baselineDirectory $file.path)).Hash -ceq
        $file.baselineSha256) "Original installation unchanged: $($file.path)"
}
$resultPath = Join-Path $output 'test-report.json'
[IO.File]::WriteAllText($resultPath,([ordered]@{
    passed=$true; checks=$checks.ToArray(); loader=$loaderResults
    inGameVerified=$false; liveInstallationModified=$false; physicalKeyPollingTested=$false
} | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output "PASS: $($checks.Count) candidate checks; eleven command-list scenarios."
Write-Output "TEST_REPORT=$resultPath"
