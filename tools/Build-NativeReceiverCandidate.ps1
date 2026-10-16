[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseCandidateReportPath,
    [Parameter(Mandatory)][string]$NativeBuildReportPath,
    [Parameter(Mandatory)][string]$ShaderCacheDirectory,
    [Parameter(Mandatory)][string]$AssemblerPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
foreach ($module in @('Universal.Common','ShaderProbe.Common','ShaderPatch','TouchAdapter.Common',
    'NativeFamilyAdapter','NativeReceiver','OutfitRuntime.Common')) {
    Import-Module (Join-Path $PSScriptRoot "$module.psm1") -Force -DisableNameChecking
}
$basePath = (Resolve-Path -LiteralPath $BaseCandidateReportPath).Path
$base = Get-Content -Raw -LiteralPath $basePath | ConvertFrom-Json
if ($base.buildKind -cne 'skin-pass-coverage-candidate' -or $base.passes.Count -ne 13) {
    throw 'Expected the thirteen-program v0.2.3 runtime.'
}
$nativePath = (Resolve-Path -LiteralPath $NativeBuildReportPath).Path
if ((Get-FileHash -LiteralPath $nativePath).Hash -cne $base.parentBuildReportSha256) {
    throw 'Native build identity changed.'
}
$build = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $build) { throw 'Preserve existing receiver evidence.' }
$runtime = Join-Path $build 'package'
$assemblyDirectory = Join-Path $build 'assembly'
$sourcesDirectory = Join-Path $build 'originals'
New-Item -ItemType Directory -Path $runtime,$assemblyDirectory,$sourcesDirectory -Force | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($file in $base.files) {
    $source = Assert-UniversalPath $base.packageDirectory (Join-Path $base.packageDirectory $file.path)
    if ((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256) { throw "Changed base file: $($file.path)" }
    $target = Assert-UniversalPath $runtime (Join-Path $runtime $file.path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    [IO.File]::SetLastWriteTimeUtc($target,[IO.File]::GetLastWriteTimeUtc($source))
}
$numerical = Get-Content -Raw -LiteralPath $base.baseCandidateReportPath | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $root 'shaders\field_vs.hlsl')).Hash -cne $base.fieldSourceSha256) {
    throw 'Revalidate the shared deformation field before changing it.'
}
$fragments = @{}
foreach ($name in @('current','previous','frame','normal')) {
    $path = Join-Path (Split-Path -Parent $base.baseCandidateReportPath) "assembly\$name.fxc.asm"
    $fragments[$name] = Get-TouchFragment ([IO.File]::ReadAllText($path))
}
$sources = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $nativePath) -Filter '*.original.asm' -File) {
    $sources[$file.Name.Split('.')[0]]=$file.FullName
}
foreach ($file in Get-ChildItem -LiteralPath $ShaderCacheDirectory -Filter '*-vs.txt' -File) {
    if ($file.Name -cmatch '^(?<hash>[0-9a-f]{16})-vs\.txt$') { $sources[$Matches.hash]=$file.FullName }
}
$rules = [Collections.Generic.List[object]]::new()
$accepted = [Collections.Generic.List[object]]::new()
$inventory = [Collections.Generic.List[object]]::new()
foreach ($hash in $sources.Keys) {
    $source = $sources[$hash]
    $original = [IO.File]::ReadAllText($source) -creplace '\bCB(?=\d)','cb'
    $spec = Get-NativeReceiverSpec $original
    if (!$spec) {
        $inventory.Add([ordered]@{ hash=$hash; source=$source; matched=$false })
        continue
    }
    $matching = @($rules | Where-Object {
        [regex]::IsMatch($original,$_.pattern,[Text.RegularExpressions.RegexOptions]::None,[TimeSpan]::FromSeconds(2))
    })
    if (!$matching.Count) {
        $rule = New-NativeReceiverRule $original $spec $fragments.current $fragments.previous `
            $fragments.frame $fragments.normal ('ShaderRegexEJ_zzReceiver{0:d3}' -f ($rules.Count+1))
        $rules.Add($rule)
    } else { $rule=$matching[0] }
    $patched = Invoke-NativeReceiverRule $original $rule
    $originalPath = Join-Path $sourcesDirectory "$hash.original.asm"
    [IO.File]::WriteAllText($originalPath,$original,$encoding)
    $originalBinary = Invoke-ShaderAssembler $AssemblerPath $originalPath
    $null = Test-ShaderAssemblyRoundtrip $AssemblerPath $originalPath $originalBinary
    $patchedPath = Join-Path $assemblyDirectory "$hash.patched.asm"
    [IO.File]::WriteAllText($patchedPath,$patched,$encoding)
    $binary = Invoke-ShaderAssembler $AssemblerPath $patchedPath
    $validation = Test-ShaderAssemblyRoundtrip $AssemblerPath $patchedPath $binary
    foreach ($kind in @('Input','Output')) {
        if ((@(Get-ShaderSignature $original $kind) | ConvertTo-Json -Compress) -cne
            (@(Get-ShaderSignature $patched $kind) | ConvertTo-Json -Compress)) {
            throw "Receiver changed the native $kind signature: $hash"
        }
    }
    $accepted.Add([ordered]@{
        hash=$hash; rule=$rule.name; instanceSlot=$spec.instanceSlot
        originalBinary=$originalBinary; binary=$binary
        source=$source; sourceSha256=(Get-FileHash -LiteralPath $source).Hash
        binarySha256=(Get-FileHash -LiteralPath $binary).Hash
        originalBinarySha256=(Get-FileHash -LiteralPath $originalBinary).Hash
        normalUpdated=$rule.normalUpdated; tangentUpdated=$rule.tangentUpdated
        validation=$validation
    })
    $inventory.Add([ordered]@{hash=$hash; source=$source; matched=$true; rule=$rule.name})
    Write-Output "Receiver $hash -> $($rule.name)"
}
if ($accepted.Count -lt 30) { throw 'Incomplete current-cache receiver coverage.' }
$surfaceRuleCount = $rules.Count
$geometryRules = [Collections.Generic.List[object]]::new()
foreach ($pass in $accepted) {
    $original = [IO.File]::ReadAllText((Join-Path $sourcesDirectory "$($pass.hash).original.asm"))
    $matching = @($geometryRules | Where-Object {
        [regex]::IsMatch($original,$_.pattern,[Text.RegularExpressions.RegexOptions]::None,[TimeSpan]::FromSeconds(2))
    })
    if ($matching.Count) { continue }
    $spec = Get-NativeReceiverSpec $original
    $geometryRules.Add((New-NativeReceiverRule $original $spec $fragments.current $fragments.previous `
        $fragments.frame $fragments.normal ('ShaderRegexEJ_zzReceiverGeometry{0:d3}' -f ($geometryRules.Count+1)) -GeometryOnly))
}
$rules.AddRange($geometryRules)
$variants = @()
foreach ($hash in @('1479b2b594b9c91a','5869b530fc5ca4cf','04cfed1feeda8014')) {
    $seed = @($accepted | Where-Object hash -CEQ $hash)[0]
    $original = [IO.File]::ReadAllText((Join-Path $sourcesDirectory "$hash.original.asm"))
    $original = [regex]::Replace($original,'\br(\d+)\b',{param($m) "r$([int]$m.Groups[1].Value+32)"})
    $original = [regex]::Replace($original,'(?m)^dcl_temps (\d+)',{param($m) "dcl_output o31.x`ndcl_temps $([int]$m.Groups[1].Value+32)"})
    $original = $original.Replace('vs_5_0',"// EJ_LAYOUT                 0   x          31     NONE   float   x`n//`nvs_5_0")
    $original = $original.Replace("ret`n","mov o31.x, l(0.125000)`nret`n")
    $original = $original.Replace("ret`r`n","mov o31.x, l(0.125000)`nret`n")
    $matching = @($geometryRules | Where-Object {
        [regex]::IsMatch($original,$_.pattern,[Text.RegularExpressions.RegexOptions]::None,[TimeSpan]::FromSeconds(2))
    })
    if (!$matching.Count) { throw 'An unseen varying layout missed structural geometry.' }
    $rule = $matching[0]
    $name = "layout_$hash"
    $originalPath = Join-Path $sourcesDirectory "$name.original.asm"
    [IO.File]::WriteAllText($originalPath,$original,$encoding)
    $originalBinary = Invoke-ShaderAssembler $AssemblerPath $originalPath
    $null = Test-ShaderAssemblyRoundtrip $AssemblerPath $originalPath $originalBinary
    $patchedPath = Join-Path $assemblyDirectory "$name.patched.asm"
    [IO.File]::WriteAllText($patchedPath,(Invoke-NativeReceiverRule $original $rule),$encoding)
    $binary = Invoke-ShaderAssembler $AssemblerPath $patchedPath
    $null = Test-ShaderAssemblyRoundtrip $AssemblerPath $patchedPath $binary
    $variants += [ordered]@{
        hash=$name; seed=$hash; rule=$rule.name; instanceSlot=$seed.instanceSlot
        originalBinary=$originalBinary; binary=$binary
        originalBinarySha256=(Get-FileHash -LiteralPath $originalBinary).Hash
        binarySha256=(Get-FileHash -LiteralPath $binary).Hash
        normalUpdated=$false; tangentUpdated=$false; syntheticLayout=$true
    }
}
$passesPath = Join-Path $runtime 'Passes.ini'
$passes = [IO.File]::ReadAllText($passesPath).TrimEnd()+"`n`n"
foreach ($rule in $rules) { $passes += (Format-NativeReceiverRule $rule)+"`n" }
foreach ($slot in @(1,2)) {
    $passes += @"

[CommandListReceiveNative$slot]
ResourceSavedVsControl = ref vs-t119
vs-t119 = ResourceZeroControl
`$matched = 1
if !`$outfit_seen && INDEX_COUNT >= 3 && (DRAW_TYPE == 2 || (DRAW_TYPE == 4 && INSTANCE_COUNT == 1 && FIRST_INSTANCE == 0)) && vs-cb0->Size >= 1312 && vs-cb$slot->Size >= 65456
    `$native_draws = `$native_draws + 1
    if `$enabled && `$mod_id > 0 && `$\EFMIv1\enable_mods && !(`$ui_mouse && !`$drag) && !`$ui_navigation && !`$cancelled && `$complete
        vs-t119 = ResourceControl
    endif
endif

"@
}
Assert-NativeOutfitBridge ([IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))) `
    $passes ([IO.File]::ReadAllText((Join-Path $runtime 'Outfits.ini')))
[IO.File]::WriteAllText($passesPath,$passes,$encoding)
foreach ($file in $base.files | Where-Object path -CNE 'Passes.ini') {
    if ((Get-FileHash -LiteralPath (Join-Path $runtime $file.path)).Hash -cne $file.sha256) {
        throw "Receiver changed unrelated runtime file: $($file.path)"
    }
}
foreach ($name in @('LICENSE','THIRD-PARTY-NOTICES.md','DISCLAIMER.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $runtime $name)
}
Copy-Item -LiteralPath (Join-Path $root 'docs\NATIVE-RECEIVER-CANDIDATE.md') -Destination (Join-Path $runtime 'INSTALL.md')
$zip = Join-Path $build 'EndfieldJiggleEFMI-v0.2.3-native-receiver-candidate-win64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($runtime,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$reportPath = Join-Path $build 'build-report.json'
[ordered]@{
    schema=1; buildKind='structural-native-receiver-candidate'; packageDirectory=$runtime
    baseCandidateReportPath=$basePath; baseCandidateReportSha256=(Get-FileHash -LiteralPath $basePath).Hash
    fieldSourceSha256=$base.fieldSourceSha256
    rules=$rules.ToArray(); passes=$accepted.ToArray(); variants=$variants; inventory=$inventory.ToArray()
    surfaceRuleCount=$surfaceRuleCount; geometryRuleCount=$geometryRules.Count
    files=@(Get-ChildItem -LiteralPath $runtime -Recurse -File | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($runtime,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })
    zipPath=$zip; zipSha256=(Get-FileHash -LiteralPath $zip).Hash
    runtimeChangedFiles=@('Passes.ini'); requiresCharacterProfiles=$false; usesMeshHashWhitelist=$false
    nativeBlocksPreserved=$true; legacyRulesPreserved=$true
    pickerBytesAndTimesPreserved=$true; defaultEnabled=$false
    liveInstallationModified=$false; inGameVerified=$false; publicationApproved=$false
    limitations=@(
        'Structural receiver coverage is bounded by the native camera/instance/position contracts.',
        'Existing native color picking still establishes the shared touch session.',
        'Vertex-stage and loader tests do not establish all-character visual acceptance.'
    )
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "RECEIVER_REPORT=$reportPath"
Write-Output "RECEIVER_ZIP=$zip"
