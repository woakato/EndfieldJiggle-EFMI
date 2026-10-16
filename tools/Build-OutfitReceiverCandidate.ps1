[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeReceiverReportPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'OutfitRuntime.Common.psm1') -Force
$parentPath = (Resolve-Path -LiteralPath $NativeReceiverReportPath).Path
$parent = Get-Content -Raw -LiteralPath $parentPath | ConvertFrom-Json
if ($parent.buildKind -cne 'structural-native-receiver-candidate' -or
    $parent.surfaceRuleCount -ne 28 -or $parent.geometryRuleCount -ne 14) {
    throw 'Use the validated structural native receiver build.'
}
$build = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $build) { throw 'Preserve earlier outfit candidate evidence.' }
$runtime = Join-Path $build 'package'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$runtimeFiles = @($parent.files | Where-Object {
    $_.path.Replace('\','/') -cmatch '^(EndfieldJiggle\.ini|Passes\.ini|Outfits\.ini|JiggleForge-LICENSE|shaders/[^/]+\.(hlsl|bin))$'
})
if ($runtimeFiles.Count -ne 14) { throw 'Expected fourteen parent runtime files.' }
foreach ($file in $runtimeFiles) {
    $source = Assert-UniversalPath $parent.packageDirectory (Join-Path $parent.packageDirectory $file.path)
    if ((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256) { throw "Changed parent payload: $($file.path)" }
    $target = Assert-UniversalPath $runtime (Join-Path $runtime $file.path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    [IO.File]::SetLastWriteTimeUtc($target, [IO.File]::GetLastWriteTimeUtc($source))
}
if ((Get-FileHash -LiteralPath (Join-Path $runtime 'Passes.ini')).Hash -cne
    '104CBCB3C2614C1219670C8D598C72A90DEAED756F235D42EB072D9929C509ED' -or
    (Get-FileHash -LiteralPath (Join-Path $runtime 'Outfits.ini')).Hash -cne
    'D3045776CAC5BE6A72BD83947169174519B7D12828A89905E163EF8A1BAA5367') {
    throw 'Unexpected native receiver or legacy outfit bridge.'
}
# Pattern-only callbacks must run before EFMI's recursive replacement draws.
# They preserve the original program and the outfit author's filter_index.
$preparation = [Collections.Generic.List[string]]::new()
$preparation.Add('namespace = AEndfieldJiggleEFMI')
$index = 0
foreach ($rule in $parent.rules | Where-Object geometryFallback) {
    $index++
    $preparation.Add(@"

[ShaderRegexPrepare$('{0:d3}' -f $index)]
shader_model = vs_5_0
`$\EndfieldJiggleEFMI\outfit_slot = $($rule.instanceSlot)
`$\EndfieldJiggleEFMI\outfit_seen = 0
post `$\EndfieldJiggleEFMI\outfit_slot = 0

[ShaderRegexPrepare$('{0:d3}' -f $index).Pattern]
$($rule.pattern)
"@)
}
if ($index -ne 14) { throw 'Missing structural outfit preparation patterns.' }
[IO.File]::WriteAllText((Join-Path $runtime 'OutfitPreparation.ini'), ($preparation -join "`n") + "`n")
$path = Join-Path $runtime 'Outfits.ini'
$text = [IO.File]::ReadAllText($path)
$text = $text.Replace('global $outfit_program = 0', "global `$outfit_program = 0`nglobal `$outfit_slot = 0")
$text = $text.Replace('if $outfit_program > 0', 'if $outfit_slot > 0')
$text = $text.Replace('($outfit_program == 1 || $outfit_program == 6)', '$outfit_slot == 1')
$text = $text.Replace('$outfit_program != 1 && $outfit_program != 6', '$outfit_slot == 2')
$text = $text.Replace('$outfit_program == 5 || $outfit_program == 7',
    '$outfit_program == 5 || $outfit_program == 7 || $outfit_program == 13')
$text = $text.Replace('            $pick_program = $outfit_program', @'
            $pick_program = $outfit_program
            if $outfit_program == 13
                $pick_program = 7
            endif
'@)
$text += @'

[ShaderOverrideEJOutfitProgram13]
hash = 5869b530fc5ca4cf
allow_duplicate_hash = true
$outfit_program = 13
$outfit_seen = 0
post $outfit_program = 0
'@
[IO.File]::WriteAllText($path, $text)
Assert-NativeOutfitBridge ([IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))) `
    ([IO.File]::ReadAllText((Join-Path $runtime 'Passes.ini'))) $text
foreach ($file in $runtimeFiles | Where-Object path -CNE 'Outfits.ini') {
    $source = Join-Path $parent.packageDirectory $file.path
    $target = Join-Path $runtime $file.path
    if ((Get-FileHash -LiteralPath $target).Hash -cne $file.sha256 -or
        [IO.File]::GetLastWriteTimeUtc($source) -ne [IO.File]::GetLastWriteTimeUtc($target)) {
        throw "Changed native runtime bytes or timestamps: $($file.path)"
    }
}
foreach ($pair in @(
    @('LICENSE','LICENSE'), @('DISCLAIMER.md','DISCLAIMER.md'),
    @('THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.md'),
    @('docs\OUTFIT-RECEIVER-CANDIDATE.md','INSTALL-zh-CN.md'),
    @('docs\OUTFIT-COMPATIBILITY.md','OUTFIT-COMPATIBILITY.md')
)) { Copy-Item -LiteralPath (Join-Path $root $pair[0]) -Destination (Join-Path $runtime $pair[1]) }
$zip = Join-Path $build 'EndfieldJiggleEFMI-v0.2.3-outfit-receiver-candidate-win64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($runtime, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$reportPath = Join-Path $build 'build-report.json'
[ordered]@{
    schema=1; buildKind='outfit-structural-receiver-candidate'; packageDirectory=$runtime
    nativeReceiverReportPath=$parentPath; nativeReceiverReportSha256=(Get-FileHash -LiteralPath $parentPath).Hash
    surfaceRuleCount=28; geometryRuleCount=14; outfitPreparationRules=14
    legacyRulesPreserved=$true; pickerBytesAndTimesPreserved=$true
    files=@(Get-ChildItem -LiteralPath $runtime -Recurse -File | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($runtime,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })
    zipPath=$zip; zipSha256=(Get-FileHash -LiteralPath $zip).Hash
    runtimeChangedFiles=@('Outfits.ini','OutfitPreparation.ini')
    flatDirectory=$true; inGameVerified=$false; liveInstallationModified=$false; published=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "OUTFIT_RECEIVER_REPORT=$reportPath"
Write-Output "OUTFIT_RECEIVER_ZIP=$zip"
