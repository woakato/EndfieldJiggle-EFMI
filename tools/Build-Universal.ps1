[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$parentPath = Join-Path $root 'reports\characters-build\build-report.json'
$accepted = '8A60B7D34B59ECE20716377ABE20F25EB506CF92AF722E4B7B2204DAD29AC0D2'
if ((Get-FileHash -LiteralPath $parentPath).Hash -cne $accepted) {
    throw 'Accepted native shader evidence changed. Do not infer a new shader interface.'
}
$parent = Get-Content -LiteralPath $parentPath -Raw | ConvertFrom-Json
$out = Join-Path $root ('reports\universal\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$package = Join-Path $out 'package\Mods\EndfieldJiggleEFMI'
Assert-UniversalPath $root $package | Out-Null
if (Test-Path -LiteralPath $out) { throw 'Preserve existing universal build evidence.' }
New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach ($file in $parent.files) {
    $source = Assert-UniversalPath $parent.packageDirectory (Join-Path $parent.packageDirectory $file.path)
    if ((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256 -or
        [IO.File]::GetLastWriteTimeUtc($source).Ticks -ne ([DateTimeOffset]$file.lastWriteTimeUtc).UtcDateTime.Ticks) {
        throw "Accepted native payload changed: $($file.path)"
    }
    $destination = Assert-UniversalPath $package (Join-Path $package $file.path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    [IO.File]::SetLastWriteTimeUtc($destination, ([DateTimeOffset]$file.lastWriteTimeUtc).UtcDateTime)
}
$basePasses = @(Read-OrderedIniSections ([IO.File]::ReadAllText((Join-Path $parent.packageDirectory 'Passes.ini'))))
$passText = [Collections.Generic.List[string]]::new()
$passText.Add('namespace = EndfieldJiggleEFMI')
foreach ($section in $basePasses | Where-Object { $_.name.StartsWith('ShaderRegex') }) {
    $passText.Add((Format-OrderedIniSection $section))
}
foreach ($pass in $parent.passes) {
    $spec = $pass.spec
    if ($spec.cameraSlot -ne 0 -or $spec.instanceSlot -notin @(1,2) -or
        $spec.role -cnotin @('color','auxiliary','outline')) { throw 'Unsupported native contract.' }
    $command = [Collections.Generic.List[string]]::new()
    $command.Add("[CommandListBindPass$($pass.passId)]")
    $command.Add('ResourceSavedVsControl = ref vs-t119')
    $command.Add('vs-t119 = ResourceZeroControl')
    $command.Add('$matched = 1')
    $command.Add('local $eligible = 0')
    $command.Add("if INDEX_COUNT >= 3 && (DRAW_TYPE == 2 || (DRAW_TYPE == 4 && INSTANCE_COUNT == 1 && FIRST_INSTANCE == 0)) && vs-cb0->Size >= 1312 && vs-cb$($spec.instanceSlot)->Size >= 65456")
    $command.Add('    $eligible = 1')
    $command.Add('    $native_draws = $native_draws + 1')
    if ($spec.role -ceq 'color') { $command.Add('    $color_draws = $color_draws + 1') }
    $command.Add('endif')
    $command.Add('if $eligible && $enabled && $mod_id > 0 && $\EFMIv1\enable_mods && !$ui_mouse && !$ui_navigation && !$cancelled')
    if ($spec.role -ceq 'color') {
        $command.Add('    local $saved_pick_program = $pick_program')
        $command.Add("    `$pick_program = $($pass.passId)")
        $command.Add('    run = CommandListPickCursor')
        $command.Add('    $pick_program = $saved_pick_program')
    }
    $command.Add('    if $complete')
    $command.Add('        vs-t119 = ResourceControl')
    $command.Add('    endif')
    $command.Add('endif')
    $passText.Add(($command -join "`n"))
}
$baseRuntime = @(Read-OrderedIniSections ([IO.File]::ReadAllText((Join-Path $parent.packageDirectory 'EndfieldJiggle.ini'))))
$templatePath = Join-Path $root 'runtime\Universal.ini'
$runtime = [Collections.Generic.List[string]]::new()
$runtime.Add([IO.File]::ReadAllText($templatePath).TrimEnd())
$sharedSections = @('CommandListCursor','CommandListStep','CustomShaderStep','CommandListPickCursor')
foreach ($section in $baseRuntime) {
    if ($section.name -cin $sharedSections -or $section.name.StartsWith('CustomShaderPick') -or
        $section.name.StartsWith('ResourceSaved') -or
        $section.name -cin @('ResourceZeroControl','ResourceState','ResourceCapture','ResourceControl',
            'ResourcePickWorld','ResourcePickNormal','ResourcePickRight','ResourcePickUp','ResourcePickDepth')) {
        $runtime.Add((Format-OrderedIniSection $section))
    }
}
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $package 'Passes.ini'), ($passText -join "`n`n"), $encoding)
[IO.File]::WriteAllText((Join-Path $package 'EndfieldJiggle.ini'), ($runtime -join "`n`n"), $encoding)
$report = [ordered]@{
    schema=1; created=(Get-Date).ToString('o')
    buildKind='universal-native-families'; packageDirectory=$package
    parentBuildReportSha256=$accepted
    passes=$parent.passes; pickPrograms=$parent.pickPrograms
    files=@(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($package,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash; lastWriteTimeUtc=$_.LastWriteTimeUtc.ToString('o') }
    })
    runtimeSourceSha256=(Get-FileHash -LiteralPath $templatePath).Hash
    requiresCharacterProfiles=$false; usesMeshHashWhitelist=$false; usesExactIndexCountWhitelist=$false
    waitsForPerCharacterPassSet=$false; nativePipelineWarmupFrames=2
    globalPhysicalField=$true; defaultEnabled=$false; liveInstallationModified=$false; inGameVerified=$false
    upstreamMechanism='Shared native color-pipeline picking and one default world-space field, analogous to JiggleForge OriginalParts.'
    limits=@(
        'Only the 12 reviewed native shader interfaces qualify. New interfaces require interface adaptation, not individual character solving.'
        'Only ordinary indexed or single-instance indexed draws with the native ranged camera/instance contract qualify.'
        'Displayed/native Y mapping remains the verified operator-overview route; world/compositor auto-calibration is not implemented.'
        'UI navigation and absent color draws cancel a session. No semantic operator identity is inferred.'
        'Replacement meshes, alpha masking, overlapping characters and unknown shader families require separate validation.'
    )
}
$path = Join-Path $out 'build-report.json'
$report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding utf8
Get-UniversalReport -Root $root -ReportPath $path | Out-Null
Write-Output "Universal compatible native interfaces: $($parent.passes.Count); character profiles and mesh whitelists: none."
Write-Output "Build report: $path"
