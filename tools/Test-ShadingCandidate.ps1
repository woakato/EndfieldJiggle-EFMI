[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$BaseModZipPath,
    [Parameter(Mandatory)][string]$NativeBuildReportPath,
    [Parameter(Mandatory)][string]$GpuFixtureSourcePath,
    [Parameter(Mandatory)][string]$LoaderHost,
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$LoaderSandboxRoot,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')][string]$RuntimeFolderName = 'EndfieldJiggleEFMI'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'OutfitRuntime.Common.psm1') -Force
$reportPath = Assert-UniversalPath (Join-Path $root 'reports') (Resolve-Path -LiteralPath $BuildReportPath).Path
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$parent = Get-Content -LiteralPath $NativeBuildReportPath -Raw | ConvertFrom-Json
$directory = Split-Path -Parent $reportPath
$output = Join-Path $directory ('verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $output) { throw 'Preserve existing candidate verification.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runtime = Assert-UniversalPath $directory $report.packageDirectory
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
function Close($Left,$Right) {
    if ($Left.Count -ne $Right.Count) { return $false }
    for ($index=0; $index -lt $Left.Count; $index++) {
        if (![single]::IsFinite($Left[$index]) -or ![single]::IsFinite($Right[$index])) { return $false }
        if ([Math]::Abs($Left[$index] - $Right[$index]) -gt 0.0001) { return $false }
    }
    return $true
}
function Finite($Values) {
    return @($Values | Where-Object { [single]::IsNaN($_) -or [single]::IsInfinity($_) }).Count -eq 0
}
Check ((Get-FileHash -LiteralPath $BaseModZipPath).Hash -ceq $report.baseModZipSha256) 'base ZIP identity'
Check ((Get-FileHash -LiteralPath $NativeBuildReportPath).Hash -ceq $report.parentBuildReportSha256) 'native evidence identity'
Check ((Get-FileHash -LiteralPath $report.zipPath).Hash -ceq $report.zipSha256) 'candidate ZIP identity'
Check (!$report.inGameVerified -and !$report.publicationApproved -and !$report.liveInstallationModified) 'candidate verification boundary'
foreach ($file in $report.files) {
    $path = Assert-UniversalPath $runtime (Join-Path $runtime $file.path)
    Check ((Get-FileHash -LiteralPath $path).Hash -ceq $file.sha256) "candidate payload: $($file.path)"
}
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $BaseModZipPath).Path)
try {
    $prefix = 'EndfieldJiggleEFMI-v0.2.2/Mods/EndfieldJiggleEFMI/'
    $entries = @($archive.Entries | Where-Object { $_.FullName.StartsWith($prefix) -and !$_.FullName.EndsWith('/') })
    Check ($entries.Count -eq 14 -and $report.files.Count -eq 14) 'runtime file set unchanged'
    $basePasses = ''
    foreach ($entry in $entries) {
        $relative = $entry.FullName.Substring($prefix.Length)
        $stream = $entry.Open()
        try {
            if ($relative -ceq 'Passes.ini') {
                $reader = [IO.StreamReader]::new($stream)
                try { $basePasses = $reader.ReadToEnd() } finally { $reader.Dispose() }
            } else {
                $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
                Check ($digest -ceq (Get-FileHash -LiteralPath (Join-Path $runtime $relative)).Hash) "unchanged published bytes: $relative"
            }
        } finally { $stream.Dispose() }
    }
} finally { $archive.Dispose() }
$runtimeText = [IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))
$passesText = [IO.File]::ReadAllText((Join-Path $runtime 'Passes.ini'))
Assert-NativeOutfitBridge $runtimeText $passesText ([IO.File]::ReadAllText((Join-Path $runtime 'Outfits.ini')))
Check ($runtimeText -match '(?m)^global \$enabled = 0\r?$') 'default OFF'
$before = @(Read-OrderedIniSections $basePasses)
$after = @(Read-OrderedIniSections $passesText)
$skinCoverage = $report.buildKind -ceq 'skin-pass-coverage-candidate'
if ($skinCoverage) {
    Check ((Get-FileHash -LiteralPath $report.baseCandidateReportPath).Hash -ceq $report.baseCandidateReportSha256) 'base candidate identity'
    $baseCandidate = Get-Content -Raw -LiteralPath $report.baseCandidateReportPath | ConvertFrom-Json
    $previousSections = @(Read-OrderedIniSections ([IO.File]::ReadAllText((Join-Path $baseCandidate.packageDirectory 'Passes.ini'))))
    Check ($report.passes.Count -eq 13 -and $after.Count -eq $previousSections.Count + 5) 'one additional native shader and binding'
    for ($index=0; $index -lt $previousSections.Count; $index++) {
        Check ($previousSections[$index].name -ceq $after[$index].name -and
            (($previousSections[$index].lines -join "`n").Trim() -ceq ($after[$index].lines -join "`n").Trim())) "previous candidate section preserved: $($previousSections[$index].name)"
    }
    $expectedExtra = @('ShaderRegexEJ_5869b530fc5ca4cf','ShaderRegexEJ_5869b530fc5ca4cf.Pattern',
        'ShaderRegexEJ_5869b530fc5ca4cf.Pattern.Replace','ShaderRegexEJ_5869b530fc5ca4cf.InsertDeclarations','CommandListBindPass13')
    Check (($after[$previousSections.Count..($after.Count-1)].name -join '|') -ceq ($expectedExtra -join '|')) 'exact added skin sections'
    $oldBinding = @($after | Where-Object name -CEQ 'CommandListBindPass7')[0]
    $newBinding = @($after | Where-Object name -CEQ 'CommandListBindPass13')[0]
    Check (($oldBinding.lines -join "`n").Trim() -ceq ($newBinding.lines -join "`n").Trim()) 'new color uses reviewed picker and eligibility'
} else {
    Check (($before.name -join '|') -ceq ($after.name -join '|')) 'INI section set and order unchanged'
}
for ($index=0; $index -lt $before.Count; $index++) {
    if ($before[$index].name.EndsWith('.Pattern.Replace')) { continue }
    $left = @($before[$index].lines | Where-Object { $_.Trim() -and $_ -notmatch '^temps = ' }) -join "`n"
    $right = @($after[$index].lines | Where-Object { $_.Trim() -and $_ -notmatch '^temps = ' }) -join "`n"
    Check ($left -ceq $right) "unchanged matching/binding section: $($before[$index].name)"
}
if (!('EndfieldJiggle.TouchGpuFixture' -as [type])) { Add-Type -Path $GpuFixtureSourcePath }
$step = [IO.File]::ReadAllBytes((Join-Path $runtime 'shaders\step_cs.cs_5_0.8000.bin'))
$pixel = [IO.File]::ReadAllBytes((Join-Path $runtime 'shaders\character_pick_ps.ps_5_0.8000.bin'))
$gpu = [EndfieldJiggle.TouchGpuFixture]::new($step)
$gpu.ExtendedFamilyInputs = $true
try {
    foreach ($pass in $report.passes) {
        Check ((Get-FileHash -LiteralPath $pass.binary).Hash -ceq $pass.binarySha256) "compiled program integrity: $($pass.hash)"
        Check ($gpu.ValidateShader([IO.File]::ReadAllBytes($pass.binary),12) -match 'WARP') "D3D11 native shader creation: $($pass.hash)"
        if (!$pass.probeBinary) { continue }
        Check ((Get-FileHash -LiteralPath $pass.probeBinary).Hash -ceq $pass.probeSha256) "probe integrity: $($pass.hash)"
        $pickerHash = if ($pass.PSObject.Properties['pickerHash']) { $pass.pickerHash } else { $pass.hash }
        $picker = @($parent.pickPrograms | Where-Object hash -CEQ $pickerHash)[0]
        $native = [IO.File]::ReadAllBytes($picker.binary)
        $probe = [IO.File]::ReadAllBytes($pass.probeBinary)
        $off = $gpu.RenderNative($native,$pixel,960,540,$false)
        $patchedOff = $gpu.RenderNative($probe,$pixel,960,540,$false)
        Check ((Finite $off) -and $off[3] -eq 1 -and (Close $off $patchedOff)) "zero-control native raster unchanged: $($pass.hash)"
        $on = $gpu.RenderNative($probe,$pixel,960,540,$true)
        Check ((Finite $on) -and $on[3] -eq 1 -and $on[2] -gt $off[2]+0.001 -and $on[2]-$off[2] -le 0.0401) "finite bounded raster deformation: $($pass.hash)"
        $null = $gpu.RenderWithControl($native,$pixel,960,540)
        $null = $gpu.StepFromRaster(960,540,$false,$false,$false,[single](1.0/60),0.12,0.04)
        $null = $gpu.RenderWithControl($native,$pixel,960,540)
        $control = $gpu.StepFromRaster(960,540,$true,$true,$true,[single](1.0/60),0.12,0.04)
        Check ($control[3] -eq 1) "native pick feeds capture: $($pass.hash)"
        for ($index=0; $index -lt 30; $index++) {
            $null = $gpu.RenderWithControl($native,$pixel,970,535)
            $control = $gpu.StepFromRaster(970,535,$true,$true,$true,[single](1.0/60),0.12,0.04)
        }
        $magnitude = [Math]::Sqrt($control[0]*$control[0]+$control[1]*$control[1]+$control[2]*$control[2])
        Check ((Finite $control) -and $magnitude -gt 0.0001 -and $magnitude -le 0.0401) "bounded drag solver: $($pass.hash)"
        $draw = $gpu.RenderWithControl($probe,$pixel,970,535)
        Check ((Finite $draw) -and $draw[3] -eq 1) "actual adapted program consumes solver: $($pass.hash)"
        $null = $gpu.StepFromRaster(970,535,$false,$true,$false,[single](1.0/60),0.12,0.04)
        Check (@($gpu.ReadControl() | Where-Object { $_ -ne 0 }).Count -eq 0) "session exit resets control: $($pass.hash)"
    }
} finally { $gpu.Dispose() }
$loaderResults = @()
$loaderRoot = [IO.Path]::GetFullPath($LoaderSandboxRoot)
if ($loaderRoot -notmatch '(?i)[\\/]reports(?:[\\/]|$)') { throw 'Loader tests must stay under a reports directory.' }
$loaderOutput = Assert-UniversalPath $loaderRoot (Join-Path $loaderRoot ([guid]::NewGuid().ToString('N').Substring(0,8)))
foreach ($mode in @('default','active-picking')) {
    $sandbox = Assert-UniversalPath $loaderOutput (Join-Path $loaderOutput $mode)
    if ((Join-Path $sandbox 'Core\EFMI\Shaders\MergedSkeleton_InstanceConfigOverrider_RollingBuffer.hlsl').Length -ge 230) {
        throw 'Use a shorter LoaderSandboxRoot for the official legacy compiler.'
    }
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'Mods'),(Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    foreach ($name in @('d3d11.dll','d3dcompiler_47.dll','Core')) {
        Copy-Item -LiteralPath (Join-Path $LoaderDirectory $name) -Destination $sandbox -Recurse
    }
    Copy-Item -LiteralPath $LoaderHost -Destination (Join-Path $sandbox 'loader_host.exe')
    Copy-Item -LiteralPath $runtime -Destination (Join-Path $sandbox "Mods\$RuntimeFolderName") -Recurse
    if ($mode -eq 'active-picking') {
        $testRuntime = Join-Path $sandbox "Mods\$RuntimeFolderName\EndfieldJiggle.ini"
        $text = [IO.File]::ReadAllText($testRuntime).Replace('global $enabled = 0','global $enabled = 1')
        $text = $text.Replace('global $mod_id = 0','global $mod_id = 1')
        $text = $text.Replace('if $pick_cursor_x >= 0 && $pick_cursor_y >= 0',
            "x240 = 160`ny240 = 90`nz240 = 320`nw240 = 180`nif 1")
        [IO.File]::WriteAllText($testRuntime,$text)
    }
    $config = @'
[Include]
include = Core\EFMI\main.ini
include_recursive = Mods
[Logging]
log_level = info
calls = 0
input = 1
unbuffered = 1
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
    [IO.File]::WriteAllText((Join-Path $sandbox 'd3dx.ini'),$config)
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $sandbox 'loader_host.exe'))
    $start.WorkingDirectory = $sandbox
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add((Join-Path $sandbox 'd3d11.dll'))
    $start.ArgumentList.Add('--eligibility')
    foreach ($pass in $report.passes) { $start.ArgumentList.Add($pass.originalBinary) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw 'Loader timed out.' }
    $text = $stdout.Result + $stderr.Result
    [IO.File]::WriteAllText((Join-Path $sandbox 'host-output.txt'),"exit=$($process.ExitCode)`n$text")
    $log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
    $errors = @($log -split '\r?\n' | Where-Object {
        $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
    })
    Check ($process.ExitCode -eq 0 -and $errors.Count -eq 0) "official loader without warnings: $mode"
    Check ([regex]::Matches($text,'srv-restored').Count -eq $report.passes.Count) "t119 restored for all programs: $mode"
    foreach ($pass in $report.passes) {
        $match = "ShaderRegex: vs_5_0 $($pass.hash) matches [ShaderRegex\EndfieldJiggleEFMI\EJ_$($pass.hash)]"
        Check ($log.Contains($match)) "official loader applies exact rule: $mode/$($pass.hash)"
    }
    foreach ($picker in $parent.pickPrograms) {
        $cacheName = [regex]::Escape("$($picker.sourceName).vs_5_0.8000.bin")
        Check ($log -match "Loaded cached shader: [^\r\n]*$cacheName") "native picker cache loaded: $mode/$($picker.hash)"
    }
    $loaderResults += [ordered]@{ mode=$mode; sandbox=$sandbox; exit=$process.ExitCode; warnings=$errors; logBytes=$log.Length }
}
$testPath = Join-Path $output 'test-report.json'
[ordered]@{
    passed=$true; checks=$checks.ToArray(); loader=$loaderResults
    buildReportSha256=(Get-FileHash -LiteralPath $reportPath).Hash
    gpuFixtureSourceSha256=(Get-FileHash -LiteralPath $GpuFixtureSourcePath).Hash
    gameStarted=$false; liveInstallationModified=$false; inGameVerified=$false
    syntheticGeometry=$true; newGameShaderCoverageVerified=$skinCoverage
    runtimeFolderName=$RuntimeFolderName
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $testPath -Encoding utf8
Write-Output "Candidate static/GPU/loader checks passed: $($checks.Count)"
Write-Output "CANDIDATE_TEST_REPORT=$testPath"
