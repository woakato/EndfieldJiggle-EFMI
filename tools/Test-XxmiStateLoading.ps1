[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EfmiPath,
    [Parameter(Mandatory)][string]$LoaderHostPath,
    [Parameter(Mandatory)][string]$ShaderPath,
    [string]$RuntimePath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'LoaderDiagnostics.psm1') -Force
$output = Join-Path $root ('reports\xxmi-state-loading\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
$results = [Collections.Generic.List[object]]::new()
$state = @'
namespace = QAQM\Persist\Bridge_0123456789ab
[Constants]
global persist $Gloves = 0
global persist $Socks = 0
'@
$outfit = @'
namespace = Example
[Constants]
global $Button_number = 10
[CommandListSetButtonCondition]
if $Button_number == 10
    if $\QAQM\Persist\Bridge_0123456789ab\Gloves < 1
        $\QAQM\Persist\Bridge_0123456789ab\Gloves = $\QAQM\Persist\Bridge_0123456789ab\Gloves + 1
    else
        $\QAQM\Persist\Bridge_0123456789ab\Gloves = 0
    endif
elif $Button_number == 11
    if $\QAQM\Persist\Bridge_0123456789ab\Socks < 1
        $\QAQM\Persist\Bridge_0123456789ab\Socks = $\QAQM\Persist\Bridge_0123456789ab\Socks + 1
    else
        $\QAQM\Persist\Bridge_0123456789ab\Socks = 0
    endif
endif
'@
$modes = @('disconnected','connected','mod-include','both-includes')
if ($RuntimePath) { $modes += 'touch-runtime' }
foreach ($mode in $modes) {
    $sandbox = Join-Path $output $mode
    $mod = Join-Path $sandbox 'Mods\Example'
    $cache = Join-Path $sandbox 'qaqm\cache'
    New-Item -ItemType Directory -Path $mod,$cache,(Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    foreach ($name in @('d3d11.dll','d3dcompiler_47.dll')) {
        Copy-Item -LiteralPath (Join-Path $EfmiPath $name) -Destination (Join-Path $sandbox $name)
    }
    $loaderHost = Join-Path $sandbox 'loader_host.exe'
    Copy-Item -LiteralPath $LoaderHostPath -Destination $loaderHost
    [IO.File]::WriteAllText((Join-Path $cache 'qaqm_state_0123456789ab.ini'),$state,$encoding)
    [IO.File]::WriteAllText((Join-Path $mod 'example.ini'),$outfit,$encoding)
    [IO.File]::WriteAllText((Join-Path $sandbox 'qaqm\qaqm_includer.ini'),
        "[IncludePersistBridgeActive]`ninclude = qaqm_persist_active.ini`n",$encoding)
    [IO.File]::WriteAllText((Join-Path $sandbox 'qaqm\qaqm_persist_active.ini'),
        "[IncludeState]`ninclude = cache\qaqm_state_0123456789ab.ini`n",$encoding)
    $configuration = @'
[Include]
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
recursive_include = 1
export_shaders = 0
[Hunting]
hunting = 0
[Stereo]
automatic_mode = 0
[Present]
'@
    if ($mode -in @('connected','both-includes')) {
        $configuration = $configuration.Replace('[Include]',
            "[Include]`ninclude = qaqm\qaqm_includer.ini")
    }
    if ($mode -in @('mod-include','both-includes')) {
        [IO.File]::WriteAllText((Join-Path $mod 'EJQaqmIncludes.ini'),
            "[IncludeEJQaqmState]`ninclude = ..\..\qaqm\cache\qaqm_state_0123456789ab.ini`n",$encoding)
    }
    if ($mode -eq 'touch-runtime') {
        Copy-Item -LiteralPath (Join-Path $EfmiPath 'Core') -Destination (Join-Path $sandbox 'Core') -Recurse
        $runtime = Join-Path $sandbox 'Mods\EndfieldJiggleEFMI'
        New-Item -ItemType Directory -Path $runtime | Out-Null
        foreach ($name in @('EndfieldJiggle.ini','Passes.ini','Outfits.ini','shaders')) {
            Copy-Item -LiteralPath (Join-Path $RuntimePath $name) -Destination (Join-Path $runtime $name) -Recurse
        }
        $configuration = $configuration.Replace('[Include]',
            "[Include]`ninclude = Core\EFMI\main.ini`ninclude = qaqm\qaqm_includer.ini")
        $configuration = $configuration.Replace('recursive_include = 1',
            "recursive_include = 1`nassemble_signature_comments = 1`ndisassemble_undecipherable_custom_data = 1`npatch_assembly_cb_offsets = 1")
    }
    [IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'),$configuration,$encoding)
    Push-Location $sandbox
    try {
        $hostOutput = & $loaderHost (Join-Path $sandbox 'd3d11.dll') $ShaderPath 2>&1
        $exit = $LASTEXITCODE
    } finally { Pop-Location }
    $hostOutput | Set-Content -LiteralPath (Join-Path $sandbox 'host-output.txt') -Encoding utf8
    $log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
    $diagnostics = @(Get-LoaderDiagnostics $log)
    $results.Add([pscustomobject]@{ mode=$mode; exitCode=$exit; diagnostics=$diagnostics })
    Write-Output "$mode`: exit=$exit diagnostics=$($diagnostics.Count)"
}
$passed = $results[0].exitCode -eq 0 -and $results[0].diagnostics.Count -gt 0 -and
    @($results | Select-Object -Skip 1 | Where-Object { $_.exitCode -ne 0 -or $_.diagnostics.Count -ne 0 }).Count -eq 0
[ordered]@{
    passed=$passed; results=$results.ToArray()
    sourceLoaderSha256=(Get-FileHash -LiteralPath (Join-Path $EfmiPath 'd3d11.dll')).Hash
    liveInstallationModified=$false; inGameVerified=$false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'test-report.json') -Encoding utf8
Write-Output "XXMI_STATE_REPORT=$output\test-report.json"
if (!$passed) { throw 'The disconnected/connected QAQM loader regression failed.' }
