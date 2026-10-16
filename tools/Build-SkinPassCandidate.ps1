[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseCandidateReportPath,
    [Parameter(Mandatory)][string]$NativeBuildReportPath,
    [Parameter(Mandatory)][string]$OriginalShaderPath,
    [Parameter(Mandatory)][string]$AssemblerPath,
    [string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
foreach ($module in @('Universal.Common','TouchAdapter.Common','ShaderProbe.Common',
    'NativeFamilyAdapter','NativePickAdapter','ShaderPatch','OutfitRuntime.Common')) {
    Import-Module (Join-Path $PSScriptRoot "$module.psm1") -Force -DisableNameChecking
}
$basePath = (Resolve-Path -LiteralPath $BaseCandidateReportPath).Path
$parentPath = (Resolve-Path -LiteralPath $NativeBuildReportPath).Path
$sourcePath = (Resolve-Path -LiteralPath $OriginalShaderPath).Path
if ((Get-FileHash -LiteralPath $basePath).Hash -cne '4B603DE7CE6A14AF62BE53D4991C53642807EE035F03CF6CF4BB5B34356E07F2' -or
    (Get-FileHash -LiteralPath $parentPath).Hash -cne '8A60B7D34B59ECE20716377ABE20F25EB506CF92AF722E4B7B2204DAD29AC0D2') {
    throw 'Use the reviewed shading candidate and native evidence.'
}
$base = Get-Content -Raw -LiteralPath $basePath | ConvertFrom-Json
$parent = Get-Content -Raw -LiteralPath $parentPath | ConvertFrom-Json
$baseDirectory = Split-Path -Parent $basePath
$parentDirectory = Split-Path -Parent $parentPath
$hash = '5869b530fc5ca4cf'
$pickerHash = 'c6e55aaa8f4b3218'
$original = [IO.File]::ReadAllText($sourcePath)
$reviewed = [IO.File]::ReadAllText((Join-Path $parentDirectory "$pickerHash.original.asm"))
foreach ($kind in @('Input','Output')) {
    if ((@(Get-ShaderSignature $original $kind) | ConvertTo-Json -Compress) -cne
        (@(Get-ShaderSignature $reviewed $kind) | ConvertTo-Json -Compress)) {
        throw "Unexpected $kind interface in the new skin program."
    }
}
# These register permutations have identical live outputs r1.z/r2.xyz.
# Reject any additional native instruction change before reusing picker 7.
$equivalent = $original
$packingChanges = @(
    @('mad r4.xyw, r2.yyyy, r4.yzyx, r5.yzyx', 'mad r4.xyz, r2.yyyy, r4.xyzx, r5.xyzx'),
    @('mad r4.z, r1.z, l(2.000000), l(-1.000000)', 'mad r4.w, r1.z, l(2.000000), l(-1.000000)'),
    @('movc r1.z, r2.x, r4.w, v7.x', 'movc r1.z, r2.x, r4.x, v7.x'),
    @('movc r2.xyz, r2.xxxx, r4.xyzx, v7.yzwy', 'movc r2.xyz, r2.xxxx, r4.yzwy, v7.yzwy')
)
foreach ($change in $packingChanges) {
    if ([regex]::Matches($equivalent, [regex]::Escape($change[0])).Count -ne 1) {
        throw 'Missing or ambiguous reviewed skin packing instruction.'
    }
    $equivalent = $equivalent.Replace($change[0], $change[1])
}
if (((ConvertTo-CanonicalShaderProgram $equivalent) -join "`n") -cne
    ((ConvertTo-CanonicalShaderProgram $reviewed) -join "`n")) {
    throw 'The new program differs beyond the reviewed equivalent packing.'
}
if ((Get-FileHash -LiteralPath (Join-Path $root 'shaders\field_vs.hlsl')).Hash -cne $base.fieldSourceSha256) {
    throw 'Field source changed after the verified numerical candidate.'
}
$build = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $root ('reports\shading-maintenance\skin-pass-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$build = Assert-UniversalPath (Join-Path $root 'reports') $build
if (Test-Path -LiteralPath $build) { throw 'Preserve existing skin-pass evidence.' }
$runtime = Join-Path $build 'package\EndfieldJiggleEFMI'
$assemblyDirectory = Join-Path $build 'assembly'
New-Item -ItemType Directory -Path $runtime,$assemblyDirectory -Force | Out-Null
foreach ($file in $base.files) {
    $source = Assert-UniversalPath $base.packageDirectory (Join-Path $base.packageDirectory $file.path)
    if ((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256) { throw "Base payload changed: $($file.path)" }
    $target = Assert-UniversalPath $runtime (Join-Path $runtime $file.path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    [IO.File]::SetLastWriteTimeUtc($target, [IO.File]::GetLastWriteTimeUtc($source))
}
$encoding = [Text.UTF8Encoding]::new($false)
$originalPath = Join-Path $assemblyDirectory "$hash.original.asm"
# The assembler normalizes declaration case; the exact-match regex is case insensitive.
$original = $original -creplace '\bCB(?=\d)', 'cb'
[IO.File]::WriteAllText($originalPath, $original, $encoding)
$originalBinary = Invoke-ShaderAssembler $AssemblerPath $originalPath
$originalValidation = Test-ShaderAssemblyRoundtrip $AssemblerPath $originalPath $originalBinary
$reference = @($parent.passes | Where-Object hash -CEQ $pickerHash)[0]
$spec = $reference.spec | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$spec.hash = $hash
$spec.id = 13
$spec.review = 'Current-game original skin color program; equivalent packed-tangent permutation, native instructions retained.'
$fragments = @{}
foreach ($name in @('current','previous','frame','normal')) {
    $fragments[$name] = Get-TouchFragment ([IO.File]::ReadAllText((Join-Path $baseDirectory "assembly\$name.fxc.asm")))
}
$patched = New-NativeFamilyAssembly $original $spec $hash `
    $fragments.current $fragments.previous $fragments.frame $fragments.normal
$patchedPath = Join-Path $assemblyDirectory "$hash.patched.asm"
[IO.File]::WriteAllText($patchedPath, $patched, $encoding)
$binary = Invoke-ShaderAssembler $AssemblerPath $patchedPath
$validation = Test-ShaderAssemblyRoundtrip $AssemblerPath $patchedPath $binary
foreach ($kind in @('Input','Output')) {
    if ((@(Get-ShaderSignature $original $kind) | ConvertTo-Json -Compress) -cne
        (@(Get-ShaderSignature $patched $kind) | ConvertTo-Json -Compress)) {
        throw "Native $kind signature changed."
    }
}
# Reuse the reviewed picker interface only after the full equivalence check above.
$probePath = Join-Path $assemblyDirectory "$hash.probe.asm"
[IO.File]::WriteAllText($probePath, (New-CharacterPickAssembly $patched $reference.spec), $encoding)
$probe = Invoke-ShaderAssembler $AssemblerPath $probePath
$null = Test-ShaderAssemblyRoundtrip $AssemblerPath $probePath $probe
$nativePickPath = Join-Path $assemblyDirectory "$hash.pick.asm"
[IO.File]::WriteAllText($nativePickPath, (New-CharacterPickAssembly $original $reference.spec), $encoding)
$nativePick = Invoke-ShaderAssembler $AssemblerPath $nativePickPath
$null = Test-ShaderAssemblyRoundtrip $AssemblerPath $nativePickPath $nativePick
$replacement = New-ShaderRegexPatch $original $patched $hash 13
$passesPath = Join-Path $runtime 'Passes.ini'
$basePasses = [IO.File]::ReadAllText($passesPath)
$baseSections = @(Read-OrderedIniSections $basePasses)
$binding = @($baseSections | Where-Object name -CEQ 'CommandListBindPass7')
if ($binding.Count -ne 1 -or @($baseSections | Where-Object name -CEQ 'CommandListBindPass13').Count) {
    throw 'Unexpected native binding set.'
}
$newBinding = "[CommandListBindPass13]`n" + ($binding[0].lines -join "`n")
$passes = $basePasses.TrimEnd() + "`n`n" + $replacement.text + "`n`n" + $newBinding + "`n"
Assert-NativeOutfitBridge ([IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))) `
    $passes ([IO.File]::ReadAllText((Join-Path $runtime 'Outfits.ini')))
[IO.File]::WriteAllText($passesPath, $passes, $encoding)
foreach ($file in $base.files | Where-Object path -CNE 'Passes.ini') {
    if ((Get-FileHash -LiteralPath (Join-Path $runtime $file.path)).Hash -cne $file.sha256) {
        throw "Unrelated payload changed: $($file.path)"
    }
}
foreach ($picker in $parent.pickPrograms) {
    $source = Join-Path $runtime "shaders\$($picker.sourceName).hlsl"
    $cache = Join-Path $runtime "shaders\$($picker.sourceName).vs_5_0.8000.bin"
    if ([IO.File]::GetLastWriteTimeUtc($source) -ne [IO.File]::GetLastWriteTimeUtc($cache)) {
        throw 'Picker source/cache timestamps differ.'
    }
}
foreach ($name in @('LICENSE','THIRD-PARTY-NOTICES.md','DISCLAIMER.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $build "package\$name")
}
Copy-Item -LiteralPath (Join-Path $root 'docs\SKIN-PASS-CANDIDATE.md') -Destination (Join-Path $build 'package\INSTALL.md')
$zip = Join-Path $build 'EndfieldJiggleEFMI-v0.2.2-skin-pass-fix-candidate-win64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $build 'package'), $zip,
    [IO.Compression.CompressionLevel]::Optimal, $false)
$newPass = [ordered]@{
    hash=$hash; passId=13; role='color'; pickerHash=$pickerHash; pickerPassId=7
    binary=$binary; probeBinary=$probe; originalBinary=$originalBinary; nativePickBinary=$nativePick
    binarySha256=(Get-FileHash -LiteralPath $binary).Hash
    probeSha256=(Get-FileHash -LiteralPath $probe).Hash
    originalBinarySha256=(Get-FileHash -LiteralPath $originalBinary).Hash
    validation=$validation; originalValidation=$originalValidation
    nativeSignaturesPreserved=$true; spec=$spec
}
$reportPath = Join-Path $build 'build-report.json'
[ordered]@{
    schema=1; buildKind='skin-pass-coverage-candidate'; packageDirectory=$runtime
    baseCandidateReportPath=$basePath; baseCandidateReportSha256=(Get-FileHash -LiteralPath $basePath).Hash
    baseModZipSha256=$base.baseModZipSha256; parentBuildReportSha256=$base.parentBuildReportSha256
    fieldSourceSha256=$base.fieldSourceSha256
    originalShaderSourcePath=$sourcePath; originalShaderSourceSha256=(Get-FileHash -LiteralPath $sourcePath).Hash
    pickerPackingEquivalenceReviewed=$true
    passes=@($base.passes) + @($newPass)
    files=@(Get-ChildItem -LiteralPath $runtime -Recurse -File | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($runtime,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    zipPath=$zip; zipSha256=(Get-FileHash -LiteralPath $zip).Hash
    runtimeChangedFiles=@('Passes.ini'); previousRulesAndBindingsPreserved=$true
    pickerBytesAndTimesPreserved=$true; defaultEnabled=$false
    liveInstallationModified=$false; inGameVerified=$false; publicationApproved=$false
    limitations=@('Current-game original skin color coverage; in-game appearance still needs validation.',
        'New color variant has native picking but no new third-party outfit callback identity.')
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "CANDIDATE_REPORT=$reportPath"
Write-Output "CANDIDATE_ZIP=$zip"
