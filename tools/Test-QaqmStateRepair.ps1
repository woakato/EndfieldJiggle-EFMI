[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModPath,
    [Parameter(Mandatory)][string]$RecoveryDirectory,
    [Parameter(Mandatory)][string]$RuntimePackagePath,
    [Parameter(Mandatory)][string]$LoaderHostPath,
    [Parameter(Mandatory)][string]$ShaderPath,
    [Parameter(Mandatory)][string]$EfmiPath,
    [Parameter(Mandatory)][string]$DependencyModPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LoaderDiagnostics.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'QaqmState.Common.psm1') -Force
$source = (Resolve-Path -LiteralPath $ModPath).Path
$recovery = (Resolve-Path -LiteralPath $RecoveryDirectory).Path
$runRoot = Join-Path $root ('reports\qaqm-tests\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Assert-UniversalPath $root $runRoot | Out-Null
$originals = @(Get-ChildItem -LiteralPath $source -Recurse -File | ForEach-Object {
    [pscustomobject]@{ path=$_.FullName; sha256=(Get-FileHash $_.FullName).Hash; ticks=$_.LastWriteTimeUtc.Ticks }
})
$results = [Collections.Generic.List[object]]::new()
foreach ($mode in @('baseline','repaired')) {
    $sandbox = Join-Path $runRoot $mode
    $mods = Join-Path $sandbox 'Mods'
    $outfit = Join-Path $mods 'EJTouchFixture'
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $outfit -Recurse
    $disabledUi = Join-Path $outfit 'DISABLED_k'
    if (Test-Path -LiteralPath $disabledUi) {
        $from = Assert-UniversalPath $runRoot $disabledUi
        $to = Assert-UniversalPath $runRoot (Join-Path $outfit 'k')
        if (Test-Path -LiteralPath $to) { throw 'Conflicting UI directory in isolated fixture.' }
        Move-Item -LiteralPath $from -Destination $to
    }
    Copy-Item -LiteralPath $RuntimePackagePath -Destination (Join-Path $mods 'EndfieldJiggleEFMI') -Recurse
    Copy-Item -LiteralPath $DependencyModPath -Destination (Join-Path $mods 'RabbitFXDependency') -Recurse
    Copy-Item -LiteralPath (Join-Path $EfmiPath 'qaqm') -Destination (Join-Path $sandbox 'qaqm') -Recurse
    Copy-Item -LiteralPath (Join-Path $EfmiPath 'Core') -Destination (Join-Path $sandbox 'Core') -Recurse
    foreach ($name in @('d3d11.dll','d3dcompiler_47.dll')) {
        Copy-Item -LiteralPath (Join-Path $EfmiPath $name) -Destination (Join-Path $sandbox $name)
    }
    Copy-Item -LiteralPath $LoaderHostPath -Destination (Join-Path $sandbox 'loader_host.exe')
    if ($mode -ceq 'repaired') {
        foreach ($stateFile in Get-ChildItem -LiteralPath $recovery -Filter 'qaqm_state_*.ini') {
            Copy-Item -LiteralPath $stateFile.FullName -Destination (Join-Path $outfit $stateFile.Name)
        }
    }
    $ini = @'
[Include]
include = qaqm\qaqm_includer.ini
include = Core\EFMI\main.ini
include_recursive = Mods
exclude_recursive = DISABLED*
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
texture_hash = 0
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
'@
    [IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'), $ini, [Text.UTF8Encoding]::new($false))
    Push-Location $sandbox
    try {
        $output = & (Join-Path $sandbox 'loader_host.exe') (Join-Path $sandbox 'd3d11.dll') $ShaderPath 2>&1
        $exit = $LASTEXITCODE
    } finally { Pop-Location }
    $output | Set-Content -LiteralPath (Join-Path $sandbox 'host-output.txt') -Encoding utf8
    $log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
    $diagnostics = @(Get-LoaderDiagnostics $log)
    $missingState = @($diagnostics | Where-Object { $_ -match '(?i)Unrecognised entry.*QAQM\\Persist\\Bridge_' })
    $uiIncluded = $log -match '(?i)\[commandlist\\mods\\ejtouchfixture\\k\\1\.ini\\click\]'
    $results.Add([pscustomobject]@{
        mode=$mode; hostExitCode=$exit; diagnostics=$diagnostics
        missingStateDiagnosticCount=$missingState.Count; sandbox=$sandbox
        outfitUiParsed=$uiIncluded
    })
    Write-Output "$mode`: exit=$exit; diagnostics=$($diagnostics.Count); QAQM missing-state warnings=$($missingState.Count)"
}
foreach ($file in $originals) {
    if ((Get-FileHash $file.path).Hash -cne $file.sha256 -or [IO.File]::GetLastWriteTimeUtc($file.path).Ticks -ne $file.ticks) {
        throw 'The source outfit was changed during its isolated test.'
    }
}
$passed = $results[0].hostExitCode -eq 0 -and $results[0].missingStateDiagnosticCount -gt 0 -and
    $results[1].hostExitCode -eq 0 -and $results[1].diagnostics.Count -eq 0 -and
    $results[0].outfitUiParsed -and $results[1].outfitUiParsed
[ordered]@{
    passed=$passed; results=$results.ToArray(); sourceFiles=$originals
    sourceLoaderSha256=(Get-FileHash (Join-Path $EfmiPath 'd3d11.dll')).Hash
    liveInstallationModified=$false; sourceFilesUnchanged=$true; inGameVerified=$false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runRoot 'test-report.json') -Encoding utf8
Write-Output "QAQM_TEST_REPORT=$runRoot\test-report.json"
if (!$passed) { throw 'QAQM A/B parser test did not meet the baseline/repaired acceptance criteria.' }
