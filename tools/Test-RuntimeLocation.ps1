[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimePath,
    [Parameter(Mandatory)][string]$EfmiPath,
    [Parameter(Mandatory)][string]$LoaderHostPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LoaderDiagnostics.psm1') -Force
$output = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $output) { throw 'Preserve previous location-test evidence.' }
$runtime = (Resolve-Path -LiteralPath $RuntimePath).Path
$efmi = Join-Path $output 'EFMI'
New-Item -ItemType Directory -Path (Join-Path $efmi 'Mods'),(Join-Path $efmi 'ShaderFixes') -Force | Out-Null
foreach ($name in @('d3d11.dll','d3dcompiler_47.dll','Core')) {
    Copy-Item -LiteralPath (Join-Path $EfmiPath $name) -Destination (Join-Path $efmi $name) -Recurse
}
$loader = Join-Path $efmi 'loader_host.exe'
Copy-Item -LiteralPath $LoaderHostPath -Destination $loader
[IO.File]::WriteAllText((Join-Path $efmi 'd3dx.ini'), @'
[Include]
include = Core\EFMI\main.ini
include_recursive = Mods
exclude_recursive = DISABLED*
exclude_recursive = desktop.ini
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
'@, [Text.UTF8Encoding]::new($false))
$layouts = @(
    'EndfieldJiggleEFMI-v0.2.5-drop-in-win64',
    'Tools\EndfieldJiggleEFMI-v0.2.5-drop-in-win64',
    'Tools\Touch\Renamed',
    'Tools\Mods\Touch',
    '功能\触摸\任意名称',
    (((0..13 | ForEach-Object { "Level$_" }) -join '\') + '\Touch')
)
$results = [Collections.Generic.List[object]]::new()
$previous = $null
for ($index = 0; $index -lt $layouts.Count; $index++) {
    $target = Assert-UniversalPath $efmi (Join-Path $efmi ('Mods\' + $layouts[$index]))
    if ($previous) {
        $previous = Assert-UniversalPath $efmi $previous
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Move-Item -LiteralPath $previous -Destination $target
    } else {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($name in @('EndfieldJiggle.ini','Passes.ini','Outfits.ini','OutfitPreparation.ini','shaders')) {
            Copy-Item -LiteralPath (Join-Path $runtime $name) -Destination (Join-Path $target $name) -Recurse
        }
    }
    $start = [Diagnostics.ProcessStartInfo]::new($loader)
    $start.WorkingDirectory = $efmi
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add((Join-Path $efmi 'd3d11.dll'))
    foreach ($name in @('pick_1479b2b594b9c91a.vs_5_0.8000.bin','pick_c6e55aaa8f4b3218.vs_5_0.8000.bin')) {
        $start.ArgumentList.Add((Join-Path $target "shaders\$name"))
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'Isolated location loader timed out.'
    }
    $hostText = $stdout.Result + $stderr.Result
    $log = [IO.File]::ReadAllText((Join-Path $efmi 'd3d11_log.txt'))
    [IO.File]::WriteAllText((Join-Path $output "layout-$index-host.txt"), $hostText)
    [IO.File]::WriteAllText((Join-Path $output "layout-$index-d3d11_log.txt"), $log)
    $diagnostics = @(Get-LoaderDiagnostics $log)
    $loaded = @('EndfieldJiggle.ini','Passes.ini','Outfits.ini','OutfitPreparation.ini') | Where-Object {
        $log.Contains(('Mods\' + $layouts[$index] + '\' + $_), [StringComparison]::OrdinalIgnoreCase)
    }
    $passed = $process.ExitCode -eq 0 -and $diagnostics.Count -eq 0 -and
        @($loaded).Count -eq 4 -and $hostText.Contains('isolated-loader-ok') -and
        @([regex]::Matches($hostText,'srv-restored')).Count -eq 2
    $results.Add([pscustomobject]@{
        layout=$layouts[$index]; passed=$passed; exitCode=$process.ExitCode
        diagnostics=$diagnostics; runtimeInisLoaded=@($loaded).Count
    })
    $process.Dispose()
    $previous = $target
    Write-Output "Layout $index`: passed=$passed diagnostics=$($diagnostics.Count)"
}
$passed = @($results | Where-Object { !$_.passed }).Count -eq 0
[ordered]@{
    passed=$passed; results=$results.ToArray()
    sourceLoaderSha256=(Get-FileHash -LiteralPath (Join-Path $EfmiPath 'd3d11.dll')).Hash
    liveInstallationModified=$false; inGameVerified=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding utf8
Write-Output "LOCATION_REPORT=$output\validation.json"
if (!$passed) { throw 'Runtime directory layout loading failed.' }
