[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$LoaderHost,
    [Parameter(Mandatory)][string]$UnknownShaderPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('active','disabled','cancelled','incomplete','outfit-disabled','invalid-range','invalid-instances')]
    [string]$Scenario = 'active'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$report = Get-Content -Raw -LiteralPath $BuildReportPath | ConvertFrom-Json
$native = Get-Content -Raw -LiteralPath $report.nativeReceiverReportPath | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $report.nativeReceiverReportPath).Hash -cne $report.nativeReceiverReportSha256) {
    throw 'Native receiver evidence changed.'
}
foreach ($file in $report.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $report.packageDirectory $file.path)).Hash -cne $file.sha256) {
        throw "Candidate payload changed: $($file.path)"
    }
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Preserve previous outfit test evidence.' }
$sandbox = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path (Join-Path $sandbox 'Mods'),(Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
foreach ($name in @('d3d11.dll','d3dcompiler_47.dll','Core')) {
    Copy-Item -LiteralPath (Join-Path $LoaderDirectory $name) -Destination $sandbox -Recurse
}
Copy-Item -LiteralPath $LoaderHost -Destination (Join-Path $sandbox 'loader_host.exe')
$runtime = Join-Path $sandbox 'Mods\EndfieldJiggleEFMI'
Copy-Item -LiteralPath $report.packageDirectory -Destination $runtime -Recurse
$iniPath = Join-Path $runtime 'EndfieldJiggle.ini'
$ini = [IO.File]::ReadAllText($iniPath).Replace('global $enabled = 0','global $enabled = 1').
    Replace('global $mod_id = 0','global $mod_id = 1').Replace('global $complete = 0','global $complete = 1')
$ini = $ini.Replace('[ResourceControl]',"[ResourceControl]`ndata = 0.05 0 0 1  0 0 0 1  0.05 0 0 2  0 0 0 0")
if ($Scenario -ceq 'disabled') { $ini = $ini.Replace('global $enabled = 1','global $enabled = 0') }
if ($Scenario -ceq 'cancelled') { $ini = $ini.Replace('global $cancelled = 0','global $cancelled = 1') }
if ($Scenario -ceq 'incomplete') { $ini = $ini.Replace('global $complete = 1','global $complete = 0') }
[IO.File]::WriteAllText($iniPath, $ini)
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
$fixture = @'
namespace = OutfitFixture
[Constants]
global $observed_program = 0
global $observed_draws = 0
global $observed_filter = 0
[ResourceProbeOutput]
type = RWBuffer
format = R32G32B32A32_FLOAT
array = 1
bind_flags = unordered_access shader_resource
[CustomShaderProbe]
cs = outfit_probe.hlsl
cs-t0 = ref vs-t119
cs-u0 = ResourceProbeOutput
dispatch = 1, 1, 1
post cs-t0 = null
post cs-u0 = null
[TextureOverrideReplacement]
match_type = Buffer
match_bind_flags = 0x2
match_byte_width = 6
handling = skip
$observed_program = $\EndfieldJiggleEFMI\outfit_program
$observed_filter = vs
$\EndfieldJiggleEFMI\outfit_draws = 0
$\EndfieldJiggleEFMI\outfit_count = 3
$\EndfieldJiggleEFMI\outfit_first = 0
$\EndfieldJiggleEFMI\outfit_base = 0
$\EndfieldJiggleEFMI\outfit_instances = 1
$\EndfieldJiggleEFMI\outfit_first_instance = 0
run = CommandList\EndfieldJiggleEFMI\BeginOutfitDraw
$observed_draws = $\EndfieldJiggleEFMI\outfit_draws
run = CustomShaderProbe
vs-t118 = ref ResourceProbeOutput
drawindexed = 3, 0, 0
run = CommandList\EndfieldJiggleEFMI\EndOutfitDraw
'@
if ($Scenario -ceq 'outfit-disabled') {
    $fixture = $fixture.Replace('run = CommandList\EndfieldJiggleEFMI\BeginOutfitDraw',
        "`$\EndfieldJiggleEFMI\outfit_enabled = 0`nrun = CommandList\EndfieldJiggleEFMI\BeginOutfitDraw")
}
if ($Scenario -ceq 'invalid-range') { $fixture = $fixture.Replace('outfit_count = 3','outfit_count = 2') }
if ($Scenario -ceq 'invalid-instances') { $fixture = $fixture.Replace('outfit_instances = 1','outfit_instances = 2') }
$programs = if ($Scenario -ceq 'active') { @($native.passes) + @($native.variants) }
    else { @($native.passes | Where-Object hash -In @('1479b2b594b9c91a','5869b530fc5ca4cf','1a35d3c7704c57ff')) }
$programs += [pscustomobject]@{hash='unknown'; originalBinary=(Resolve-Path -LiteralPath $UnknownShaderPath).Path}
foreach ($pass in $programs | Where-Object { $_.hash -cmatch '^[0-9a-f]{16}$' }) {
    $fixture += @"

[ShaderOverrideAuthorFilter$($pass.hash)]
hash = $($pass.hash)
filter_index = 202
allow_duplicate_hash = overrule
"@
}
[IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'), $config)
[IO.File]::WriteAllText((Join-Path $sandbox 'Mods\OutfitFixture.ini'), $fixture)
[IO.File]::WriteAllText((Join-Path $sandbox 'Mods\outfit_probe.hlsl'), @'
Buffer<float4> Control : register(t0);
RWBuffer<float4> Result : register(u0);
[numthreads(1,1,1)]
void main() { Result[0] = Control[0]; }
'@)
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $sandbox 'loader_host.exe'))
$start.WorkingDirectory = $sandbox
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add((Join-Path $sandbox 'd3d11.dll'))
$start.ArgumentList.Add('--outfit-probe')
foreach ($pass in $programs) { $start.ArgumentList.Add($pass.originalBinary) }
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
if (!$process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw 'Outfit probe timed out.' }
$hostText = $stdout.Result + $stderr.Result
[IO.File]::WriteAllText((Join-Path $sandbox 'host-output.txt'), $hostText)
$log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
$diagnostics = @($log -split '\r?\n' | Where-Object {
    $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
})
function Observed([string]$Name) {
    $pattern = '(?im)\[textureoverride\\outfitfixture\\replacement\] \$' + [regex]::Escape($Name) +
        ' = [^\r\n]*\r?\n[^\r\n]*=\s*(?<value>-?\d+)\.000000'
    @([regex]::Matches($log,$pattern) | ForEach-Object { [int]$_.Groups['value'].Value })
}
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw "FAIL: $Message; inspect $sandbox" }
    $checks.Add($Message)
}
Check ($process.ExitCode -eq 0 -and !$diagnostics.Count) 'Official loader without warnings'
Check ([regex]::Matches($hostText,'srv-restored').Count -eq $programs.Count) 'Foreign t119 restored after every program'
$ids = @(Observed 'observed_program')
$filters = @(Observed 'observed_filter')
$controls = @([regex]::Matches($hostText,'(?m)^outfit-control (\d+) ([^\r\n]+)') | ForEach-Object {
    ,@($_.Groups[2].Value.Split(' ') | ForEach-Object {
        [double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)
    })
})
Check ($controls.Count -eq $programs.Count*4 -and $ids.Count -eq $controls.Count) 'Every replacement draw was observed'
for ($i=0; $i -lt $programs.Count; $i++) {
    $hash = $programs[$i].hash
    for ($draw=0; $draw -lt 4; $draw++) {
        $at = $i*4+$draw
        $expected = if ($hash -ceq 'unknown') { @(0.125,0,0,0.75) }
            elseif ($Scenario -ceq 'active' -and $draw -lt 3) { @(0.05,0,0,1) } else { @(0,0,0,0) }
        $maxError = 0.0
        for ($component=0; $component -lt 4; $component++) {
            $maxError = [Math]::Max($maxError,[Math]::Abs($controls[$at][$component]-$expected[$component]))
        }
        Check ($maxError -lt 0.000001) "Actual outfit control: $hash/$draw/$Scenario"
        if ($hash -cmatch '^[0-9a-f]{16}$') {
            Check ($filters[$at] -eq 202) "Author shader filter retained: $hash/$draw"
        }
        if ($hash -ceq '5869b530fc5ca4cf') { Check ($ids[$at] -eq 13) 'New color picker routed before replacement draw' }
        if ($hash -ceq 'unknown') { Check ($ids[$at] -eq 0) 'Unsupported shader does not inherit a previous picker' }
    }
}
[ordered]@{
    passed=$true; scenario=$Scenario; checks=$checks.ToArray(); shaderPrograms=$programs.Count
    buildReportSha256=(Get-FileHash -LiteralPath $BuildReportPath).Hash
    diagnostics=$diagnostics; inGameVerified=$false; liveInstallationModified=$false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $sandbox 'test-report.json') -Encoding utf8
Write-Output "PASS: $($checks.Count) outfit receiver checks ($Scenario)."
