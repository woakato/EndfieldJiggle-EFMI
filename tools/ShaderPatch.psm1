Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-PatchLine {
    param([Collections.Generic.List[string]]$Lines, [string]$Expected)
    $indices = @(for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i].Trim() -ceq $Expected) { $i }
    })
    if ($indices.Count -ne 1) { throw "Missing or ambiguous native anchor: $Expected" }
    return [int]$indices[0]
}

function Expand-FieldFragment {
    param($Fragment, [string[]]$Inputs, [string[]]$Outputs, [int]$Base)
    foreach ($line in $Fragment.lines) {
        $line = [regex]::Replace($line, '\br(\d+)\b', {
            param($m)
            "r$($Base + [int]$m.Groups[1].Value)"
        })
        $line = [regex]::Replace($line, '\bv(\d+)\b', {
            param($m)
            $i = [int]$m.Groups[1].Value
            if ($i -ge $Inputs.Count) { throw "Unmapped fragment input." }
            $Inputs[$i]
        })
        [regex]::Replace($line, '\bo(\d+)\b', {
            param($m)
            $i = [int]$m.Groups[1].Value
            if ($i -ge $Outputs.Count) { throw "Unmapped fragment output." }
            $Outputs[$i]
        })
    }
}

function New-NativeFieldPatch {
    param([string]$Assembly, $Spec, $Current, $Previous, $Frame, $Normal)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]($Assembly -split '\r?\n'))
    if ($Assembly -match '(?m)^(?!//).*\bt119\b') { throw "Control SRV slot occupied." }
    $decl = [regex]::Match($Assembly, '(?m)^dcl_temps (\d+)\s*$')
    if (!$decl.Success) { throw "No native temporary declaration." }
    $n = [int]$decl.Groups[1].Value
    $scratch = @($n..($n + 5) | ForEach-Object { "r$_" })
    $fragmentBase = $n + 6
    $count = @($Current.temps, $Previous.temps, $Frame.temps, $Normal.temps) |
        Measure-Object -Maximum
    $lines[(Find-PatchLine $lines "dcl_temps $n")] = "dcl_temps $($fragmentBase + $count.Maximum)"
    $lines.Insert((Find-PatchLine $lines "dcl_input v0.xyz"),
        "dcl_resource_buffer (float,float,float,float) t119")
    $at = Find-PatchLine $lines $Spec.currentAnchor
    $patch = @("mov $($scratch[0]).xyz, $($Spec.currentRegister).xyzx")
    $patch += @(Expand-FieldFragment $Current @($scratch[0]) @($scratch[3]) $fragmentBase)
    $patch += "mov $($Spec.currentRegister).xyz, $($scratch[3]).xyzx"
    $lines.InsertRange($at + 1, [string[]]$patch)
    $at = Find-PatchLine $lines $Spec.previousAnchor
    $patch = @("mov $($scratch[4]).xyz, $($Spec.previousRegister).xyzx")
    $patch += @(Expand-FieldFragment $Previous @($scratch[4]) @($scratch[3]) $fragmentBase)
    $patch += "mov $($Spec.previousRegister).xyz, $($scratch[3]).xyzx"
    $lines.InsertRange($at + 1, [string[]]$patch)

    $lines[(Find-PatchLine $lines $Spec.normalWrite)] =
        $Spec.normalWrite.Replace("o3.xyz", "$($scratch[1]).xyz")
    if ($Spec.PSObject.Properties["tangentWrite"]) {
        $at = Find-PatchLine $lines $Spec.tangentWrite
        $lines[$at] = $Spec.tangentWrite.Replace("o4", $scratch[2])
        if ($Spec.tangentWrite -match 'o4.xyzw') {
            $lines.Insert($at + 1, "mov o4.w, $($scratch[2]).w")
        }
        $patch = @(Expand-FieldFragment $Frame $scratch[0..2] $scratch[3..5] $fragmentBase)
        $patch += @("mov o3.xyz, $($scratch[4]).xyzx", "mov o4.xyz, $($scratch[5]).xyzx")
    } else {
        $patch = @(Expand-FieldFragment $Normal $scratch[0..1] $scratch[3..4] $fragmentBase)
        $patch += "mov o3.xyz, $($scratch[4]).xyzx"
    }
    $lines.InsertRange((Find-PatchLine $lines "ret"), [string[]]$patch)
    return ($lines -join "`n") + "`n"
}

function Get-ProgramText {
    param([string]$Assembly)
    $match = [regex]::Match($Assembly, '(?ms)^vs_5_0\r?\n.*?^ret[ \t]*\r?\n')
    if (!$match.Success) { throw "A single-entry VS program ending in ret is required." }
    if ([regex]::Matches($Assembly, '(?m)^ret[ \t]*$').Count -ne 1) {
        throw "Early returns are unsupported."
    }
    return $match.Value.Replace("`r", "")
}

function New-ShaderRegexPatch {
    param([string]$Original, [string]$Patched, [string]$Hash, [int]$PassId)
    $program = Get-ProgramText $Original
    $updated = Get-ProgramText $Patched
    $before = [int][regex]::Match($program, '(?m)^dcl_temps (\d+)').Groups[1].Value
    $after = [int][regex]::Match($updated, '(?m)^dcl_temps (\d+)').Groups[1].Value
    $temps = @($before..($after - 1) | ForEach-Object { "ej{0:d3}" -f ($_ - $before) })
    $replacement = [regex]::Replace($updated, '\br(\d+)\b', {
        param($m)
        $i = [int]$m.Groups[1].Value
        if ($i -lt $before) { return $m.Value }
        '${' + ("ej{0:d3}" -f ($i - $before)) + '}'
    })
    # XXMI owns declaration insertion and temp allocation; leave native declarations intact.
    $replacement = $replacement.Replace("dcl_temps $after", "dcl_temps $before")
    $replacement = $replacement.Replace("dcl_resource_buffer (float,float,float,float) t119`n", "")
    $patternLines = @($program.TrimEnd("`n") -split "`n" | ForEach-Object {
        $escaped = [regex]::Escape($_.Trim())
        $escaped = [regex]::Replace($escaped, '(?i)E(?<sign>[-+])0*(?<exponent>[1-9]\d*)', {
            param($m)
            'E' + $m.Groups['sign'].Value + '0*' + $m.Groups['exponent'].Value
        })
        '[ \t]*' + $escaped + '[ \t]*\r?\n'
    })
    $pattern = '(?im)^' + ($patternLines -join "")
    $name = "ShaderRegexEJ_$Hash"
    $command = "run = CommandListBindPass$PassId"
    $section = @(
        "[$name]", "shader_model = vs_5_0", "temps = $($temps -join ' ')", $command,
        "post vs-t119 = ref ResourceSavedVsControl", "",
        "[$name.Pattern]", $pattern, "",
        "[$name.Pattern.Replace]"
    )
    $section += @($replacement.TrimEnd("`n") -split "`n" | ForEach-Object { $_ + '\n' })
    $section += @("", "[$name.InsertDeclarations]",
        "dcl_resource_buffer (float,float,float,float) t119", "")
    return [pscustomobject]@{
        text = $section -join "`n"
        pattern = $pattern
        replacement = $replacement
        tempNames = $temps
        originalTemps = $before
        patchedTemps = $after
    }
}

Export-ModuleMember -Function New-NativeFieldPatch, New-ShaderRegexPatch, Get-ProgramText
