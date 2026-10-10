Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1')

function Enable-NativeOutfitBridge {
    param(
        [Parameter(Mandatory)][string]$RuntimeText,
        [Parameter(Mandatory)][string]$PassesText,
        [Parameter(Mandatory)][string]$OutfitTemplate
    )
    $runtimeSections = @(Read-OrderedIniSections $RuntimeText)
    $passSections = @(Read-OrderedIniSections $PassesText)
    $outfits = [Collections.Generic.List[string]]::new()
    $outfits.Add($OutfitTemplate.TrimEnd())
    $pickers = @($runtimeSections | Where-Object name -In @('CustomShaderPick5','CustomShaderPick7'))
    if ($pickers.Count -ne 2 -or
        [regex]::Matches($RuntimeText, '(?m)^draw = from_caller\r?$').Count -ne 2) {
        throw 'Expected exactly two reviewed native color pickers.'
    }
    $runtime = [regex]::Replace($RuntimeText, '(?m)^draw = from_caller\r?$', @'
if $outfit_active
    drawindexedinstanced = $outfit_count, 1, $outfit_first, $outfit_base, 0
else
    draw = from_caller
endif
'@)
    $passes = $PassesText
    for ($id = 1; $id -le 12; $id++) {
        $commands = @($passSections | Where-Object name -EQ "CommandListBindPass$id")
        if ($commands.Count -ne 1) { throw "Missing native draw contract: $id" }
        $regexSections = @($passSections | Where-Object {
            $_.name -match '^ShaderRegexEJ_[0-9a-f]{16}$' -and
            @($_.lines | Where-Object { $_.Trim() -ceq "run = CommandListBindPass$id" }).Count -eq 1
        })
        if ($regexSections.Count -ne 1) { throw "Ambiguous native program identity: $id" }
        $hash = $regexSections[0].name.Substring('ShaderRegexEJ_'.Length)
        $outfits.Add(@"
[ShaderOverrideEJOutfitProgram$id]
hash = $hash
allow_duplicate_hash = true
`$outfit_program = $id
`$outfit_seen = 0
post `$outfit_program = 0
"@)
    }
    if ([regex]::Matches($passes, '(?m)^if INDEX_COUNT >= 3 &&').Count -ne 12) {
        throw 'Expected exactly twelve native eligibility conditions.'
    }
    $passes = $passes.Replace('if INDEX_COUNT >= 3 &&', 'if !$outfit_seen && INDEX_COUNT >= 3 &&')
    $runtime = $runtime.Replace(' || $ui_mouse ||', ' || ($ui_mouse && !$drag) ||')
    $runtime = $runtime.Replace('key = no_shift VK_LBUTTON', 'key = VK_LBUTTON')
    $passes = $passes.Replace('&& !$ui_mouse &&', '&& !($ui_mouse && !$drag) &&')
    $result = [pscustomobject]@{
        RuntimeText = $runtime
        PassesText = $passes
        OutfitsText = $outfits -join "`n`n"
    }
    Assert-NativeOutfitBridge $result.RuntimeText $result.PassesText $result.OutfitsText
    return $result
}

function Assert-NativeOutfitBridge {
    param(
        [Parameter(Mandatory)][string]$RuntimeText,
        [Parameter(Mandatory)][string]$PassesText,
        [Parameter(Mandatory)][string]$OutfitsText
    )
    $runtime = @(Read-OrderedIniSections $RuntimeText)
    $passes = @(Read-OrderedIniSections $PassesText)
    $outfits = @(Read-OrderedIniSections $OutfitsText)
    foreach ($id in @(5,7)) {
        $picker = @($runtime | Where-Object name -EQ "CustomShaderPick$id")
        if ($picker.Count -ne 1 -or
            ($picker[0].lines -join "`n") -notmatch 'if \$outfit_active\r?\n\s*drawindexedinstanced = \$outfit_count, 1, \$outfit_first, \$outfit_base, 0\r?\nelse\r?\n\s*draw = from_caller\r?\nendif') {
            throw "Outfit picker range is missing: $id"
        }
    }
    for ($id = 1; $id -le 12; $id++) {
        $command = @($passes | Where-Object name -EQ "CommandListBindPass$id")
        $hook = @($outfits | Where-Object name -EQ "ShaderOverrideEJOutfitProgram$id")
        $program = @($passes | Where-Object {
            $_.name -match '^ShaderRegexEJ_[0-9a-f]{16}$' -and
            @($_.lines | Where-Object { $_.Trim() -ceq "run = CommandListBindPass$id" }).Count -eq 1
        })
        if ($command.Count -ne 1 -or
            ($command[0].lines -join "`n") -notmatch '(?m)^if !\$outfit_seen && INDEX_COUNT >= 3 &&' -or
            $hook.Count -ne 1 -or ($hook[0].lines -join "`n") -match 'filter_index\s*=') {
            throw "Outfit bridge/native pass exclusion is incomplete: $id"
        }
        if ($program.Count -ne 1 -or
            @($hook[0].lines | Where-Object { $_.Trim() -ceq ('hash = ' + $program[0].name.Substring('ShaderRegexEJ_'.Length)) }).Count -ne 1 -or
            @($hook[0].lines | Where-Object { $_.Trim() -ceq "`$outfit_program = $id" }).Count -ne 1) {
            throw "Outfit callback identity differs from the native pass: $id"
        }
    }
    foreach ($name in @('CommandListBeginOutfitDraw','CommandListEndOutfitDraw','ResourceSavedOutfitVsControl')) {
        if (@($outfits | Where-Object name -EQ $name).Count -ne 1) { throw "Missing outfit bridge section: $name" }
    }
    if ($RuntimeText -match ' \|\| \$ui_mouse \|\|' -or
        $PassesText.Contains('&& !$ui_mouse &&') -or $OutfitsText.Contains('&& !$ui_mouse &&')) {
        throw 'Unconditional mouse cancellation would break custom drag bindings.'
    }
}

Export-ModuleMember -Function Enable-NativeOutfitBridge, Assert-NativeOutfitBridge
