[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$GpuFixtureSourcePath,
    [Parameter(Mandatory)][string]$LoaderHost,
    [Parameter(Mandatory)][string]$LoaderDirectory,
    [Parameter(Mandatory)][string]$LoaderSandboxRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
foreach ($module in @('Universal.Common','ShaderProbe.Common','ShaderPatch','NativeReceiver','OutfitRuntime.Common')) {
    Import-Module (Join-Path $PSScriptRoot "$module.psm1") -Force -DisableNameChecking
}
$reportPath = (Resolve-Path -LiteralPath $BuildReportPath).Path
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
$base = Get-Content -Raw -LiteralPath $report.baseCandidateReportPath | ConvertFrom-Json
$directory = Split-Path -Parent $reportPath
$output = Join-Path $directory ('verification-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw "FAIL: $Message" }
    $checks.Add($Message)
}
Check ((Get-FileHash -LiteralPath $report.baseCandidateReportPath).Hash -ceq $report.baseCandidateReportSha256) 'base candidate identity'
Check ((Get-FileHash -LiteralPath $report.zipPath).Hash -ceq $report.zipSha256) 'flat archive identity'
Check (!$report.inGameVerified -and !$report.liveInstallationModified -and !$report.publicationApproved) 'verification boundaries'
$runtime = $report.packageDirectory
foreach ($file in $report.files) {
    Check ((Get-FileHash -LiteralPath (Join-Path $runtime $file.path)).Hash -ceq $file.sha256) "payload identity: $($file.path)"
}
foreach ($file in $base.files | Where-Object path -CNE 'Passes.ini') {
    $path = Join-Path $runtime $file.path
    $before = Join-Path $base.packageDirectory $file.path
    Check ((Get-FileHash -LiteralPath $path).Hash -ceq $file.sha256 -and
        [IO.File]::GetLastWriteTimeUtc($path) -eq [IO.File]::GetLastWriteTimeUtc($before)) "unchanged bytes and timestamps: $($file.path)"
}
$passText = [IO.File]::ReadAllText((Join-Path $runtime 'Passes.ini'))
$basePassText = [IO.File]::ReadAllText((Join-Path $base.packageDirectory 'Passes.ini'))
Check ($passText.StartsWith($basePassText.TrimEnd(),[StringComparison]::Ordinal)) 'all thirteen legacy rules and outfit callbacks retained'
Assert-NativeOutfitBridge ([IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))) `
    $passText ([IO.File]::ReadAllText((Join-Path $runtime 'Outfits.ini')))
$null = Read-OrderedIniSections $passText
$archive = [IO.Compression.ZipFile]::OpenRead($report.zipPath)
try {
    Check (@($archive.Entries | Where-Object FullName -CEQ 'Passes.ini').Count -eq 1) 'Passes.ini directly at ZIP root'
    Check (@($archive.Entries | Where-Object { $_.FullName.StartsWith('shaders/') }).Count -gt 0) 'shaders directly at ZIP root'
    Check (@($archive.Entries | Where-Object { $_.FullName -match '(?i)(original|patched|\.asm$|\.shdr$|build-report|FrameAnalysis)' }).Count -eq 0) 'no captured game sources or research payload in archive'
} finally { $archive.Dispose() }
if (!('EndfieldJiggle.TouchGpuFixture' -as [type])) { Add-Type -Path $GpuFixtureSourcePath }
$gpu = [EndfieldJiggle.TouchGpuFixture]::new([IO.File]::ReadAllBytes((Join-Path $runtime 'shaders\step_cs.cs_5_0.8000.bin')))
try {
    foreach ($pass in @($report.passes)+@($report.variants)) {
        $original = [IO.File]::ReadAllText((Join-Path $directory "originals\$($pass.hash).original.asm"))
        $patched = [IO.File]::ReadAllText((Join-Path $directory "assembly\$($pass.hash).patched.asm"))
        $rule = @($report.rules | Where-Object name -CEQ $pass.rule)[0]
        Check ((Get-FileHash -LiteralPath $pass.binary).Hash -ceq $pass.binarySha256) "compiled receiver identity: $($pass.hash)"
        Check ($gpu.ValidateShader([IO.File]::ReadAllBytes($pass.binary),12) -match 'WARP') "D3D11 creates receiver: $($pass.hash)"
        Check ((Invoke-NativeReceiverRule $original $rule) -ceq $patched) "runtime template equals assembled program: $($pass.hash)"
        $renamed = [regex]::Replace($original,'\br(\d+)\b',{param($m) "r$([int]$m.Groups[1].Value+32)"})
        $renamed = [regex]::Replace($renamed,'(?m)^dcl_temps (\d+)',{param($m) "dcl_temps $([int]$m.Groups[1].Value+32)"})
        Check ([regex]::IsMatch($renamed,$rule.pattern)) "unseen register allocation accepted without shader hash: $($pass.hash)"
        Check (![regex]::IsMatch($patched,$rule.pattern)) "already patched program excluded: $($pass.hash)"
        Check (![regex]::IsMatch($original.Replace('cb0[44]','cb0[43]'),$rule.pattern)) "wrong current camera rejected: $($pass.hash)"
        Check (![regex]::IsMatch($original.Replace('cb0[81]','cb0[80]'),$rule.pattern)) "wrong previous camera rejected: $($pass.hash)"
        Check (![regex]::IsMatch($original.Replace('dcl_resource_structured t0, 16','dcl_resource_structured t0, 32'),$rule.pattern)) "wrong bone stride rejected: $($pass.hash)"
        $newLayout = $renamed -replace '(?m)^(dcl_temps \d+)',"dcl_output o30.x`n`$1"
        $fallbacks = @($report.rules | Where-Object { $_.geometryFallback -and [regex]::IsMatch($newLayout,$_.pattern) })
        Check ($fallbacks.Count -gt 0) "unseen varying layout gets shared geometry without new shader identity: $($pass.hash)"
        if ($rule.normalUpdated) {
            $conditional = $original.Replace("ret`n","if_nz r0.x`nendif`nret`n").Replace("ret`r`n","if_nz r0.x`nendif`nret`n")
            Check (![regex]::IsMatch($conditional,$rule.pattern)) "conditional material does not move surface writes outside branches: $($pass.hash)"
            Check (@($report.rules | Where-Object { $_.geometryFallback -and [regex]::IsMatch($conditional,$_.pattern) }).Count -gt 0) "conditional material retains native branches via geometry fallback: $($pass.hash)"
        }
        foreach ($kind in @('Input','Output')) {
            Check ((@(Get-ShaderSignature $original $kind) | ConvertTo-Json -Compress) -ceq
                (@(Get-ShaderSignature $patched $kind) | ConvertTo-Json -Compress)) "unchanged $kind interface: $($pass.hash)"
        }
    }
} finally { $gpu.Dispose() }
foreach ($entry in $report.inventory | Where-Object { !$_.matched }) {
    $source = [IO.File]::ReadAllText($entry.source) -creplace '\bCB(?=\d)','cb'
    $matches = @($report.rules | Where-Object { [regex]::IsMatch($source,$_.pattern) })
    Check ($matches.Count -eq 0) "unrelated cached program not matched: $($entry.hash)"
}
$loaderResults = @()
foreach ($mode in @('default','active-picking','unseen-layout')) {
    $sandbox = Assert-UniversalPath $LoaderSandboxRoot `
        (Join-Path $LoaderSandboxRoot (([guid]::NewGuid().ToString('N').Substring(0,8))+'\'+$mode))
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'Mods'),(Join-Path $sandbox 'ShaderFixes') -Force | Out-Null
    foreach ($name in @('d3d11.dll','d3dcompiler_47.dll','Core')) {
        Copy-Item -LiteralPath (Join-Path $LoaderDirectory $name) -Destination $sandbox -Recurse
    }
    Copy-Item -LiteralPath $LoaderHost -Destination (Join-Path $sandbox 'loader_host.exe')
    $targetRuntime = Join-Path $sandbox 'Mods\EndfieldJiggleEFMI-v0.2.3-native-receiver-candidate-win64'
    Copy-Item -LiteralPath $runtime -Destination $targetRuntime -Recurse
    if ($mode -ceq 'active-picking') {
        $iniPath = Join-Path $targetRuntime 'EndfieldJiggle.ini'
        $text = [IO.File]::ReadAllText($iniPath).Replace('global $enabled = 0','global $enabled = 1')
        $text = $text.Replace('global $mod_id = 0','global $mod_id = 1').Replace('global $complete = 0','global $complete = 1')
        $text = $text.Replace('if $pick_cursor_x >= 0 && $pick_cursor_y >= 0',
            "x240 = 160`ny240 = 90`nz240 = 320`nw240 = 180`nif 1")
        [IO.File]::WriteAllText($iniPath,$text)
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
    $start.WorkingDirectory=$sandbox
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    $start.ArgumentList.Add((Join-Path $sandbox 'd3d11.dll'))
    $start.ArgumentList.Add('--eligibility')
    $programs = if ($mode -ceq 'unseen-layout') { @($report.variants) } else { @($report.passes) }
    foreach ($pass in $programs) { $start.ArgumentList.Add($pass.originalBinary) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); throw 'Receiver loader timeout.' }
    $text = $stdout.Result+$stderr.Result
    [IO.File]::WriteAllText((Join-Path $sandbox 'host-output.txt'),"exit=$($process.ExitCode)`n$text")
    $log = [IO.File]::ReadAllText((Join-Path $sandbox 'd3d11_log.txt'))
    $errors = @($log -split '\r?\n' | Where-Object {
        $_ -match '(?i)Error compiling|Unrecogni[sz]ed (entry|section)|Duplicate section|Unknown section|\bWARNING\b|failed to'
    })
    Check ($process.ExitCode -eq 0 -and !$errors.Count) "official loader without warnings: $mode"
    Check ([regex]::Matches($text,'srv-restored').Count -eq $programs.Count) "foreign t119 restored: $mode"
    if ($mode -ceq 'unseen-layout') {
        $hits = @([regex]::Matches($log,'ShaderRegex: vs_5_0 [0-9a-f]{16} matches \[([^\]]+)\]') |
            ForEach-Object { $_.Groups[1].Value })
        Check ($hits.Count -eq $programs.Count) 'one patch for each synthetic unknown shader'
        Check (@($hits | Where-Object { $_ -notmatch 'EJ_zzReceiverGeometry\d+$' }).Count -eq 0) 'new layouts use structural geometry fallback'
    }
    foreach ($pass in $programs | Where-Object { $mode -cne 'unseen-layout' }) {
        $hits = @([regex]::Matches($log,"ShaderRegex: vs_5_0 $($pass.hash) matches \[([^\]]+)\]") |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        Check ($hits.Count -eq 1) "exactly one patch per program: $mode/$($pass.hash)"
        $expected = if ($pass.hash -in $base.passes.hash) { "ShaderRegex\EndfieldJiggleEFMI\EJ_$($pass.hash)" }
            else { 'ShaderRegex\EndfieldJiggleEFMI\'+$pass.rule.Substring('ShaderRegex'.Length) }
        Check ($hits[0] -ceq $expected) "correct native or structural rule: $mode/$($pass.hash)"
    }
    $loaderResults += [ordered]@{mode=$mode;sandbox=$sandbox;exit=$process.ExitCode;warnings=$errors}
}
$testPath = Join-Path $output 'test-report.json'
[ordered]@{
    passed=$true; checks=$checks.ToArray(); loader=$loaderResults
    buildReportSha256=(Get-FileHash -LiteralPath $reportPath).Hash
    nativePrograms=$report.passes.Count; structuralTemplates=$report.rules.Count
    inGameVerified=$false; liveInstallationModified=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $testPath -Encoding utf8
Write-Output "Receiver checks passed: $($checks.Count)"
Write-Output "RECEIVER_TEST_REPORT=$testPath"
