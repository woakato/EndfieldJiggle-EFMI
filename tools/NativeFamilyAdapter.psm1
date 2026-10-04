Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-NativeFamilyLine {
    param([Collections.Generic.List[string]]$Lines, [string]$Expected)
    $indices = @(for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i].Trim() -ceq $Expected) { $i }
    })
    if ($indices.Count -ne 1) { throw "Missing or ambiguous native-family anchor: $Expected" }
    return [int]$indices[0]
}

function Expand-NativeFamilyFragment {
    param($Fragment, [string[]]$Inputs, [string[]]$Outputs, [int]$TempBase)
    foreach ($line in $Fragment.lines) {
        $line = [regex]::Replace($line, '\br(\d+)\b', {
            param($m)
            "r$($TempBase + [int]$m.Groups[1].Value)"
        })
        $line = [regex]::Replace($line, '\bv(\d+)\b', {
            param($m)
            $index = [int]$m.Groups[1].Value
            if ($index -ge $Inputs.Count) { throw "Unmapped native-family fragment input v$index." }
            $Inputs[$index]
        })
        [regex]::Replace($line, '\bo(\d+)\b', {
            param($m)
            $index = [int]$m.Groups[1].Value
            if ($index -ge $Outputs.Count) { throw "Unmapped native-family fragment output o$index." }
            $Outputs[$index]
        })
    }
}

function Get-NativeFamilySpec {
    param([Parameter(Mandatory)]$Config, [Parameter(Mandatory)][string]$Hash)
    $spec = @($Config.families | Where-Object { $_.hash -ceq $Hash })
    if ($spec.Count -ne 1) { throw "No unique enabled native-family spec for hash $Hash." }
    if (!$spec[0].safe) { throw "Native family $Hash is explicitly marked unsafe." }
    return $spec[0]
}

function New-NativeFamilyAssembly {
    param(
        [Parameter(Mandatory)][string]$Assembly,
        [Parameter(Mandatory)]$Spec,
        [Parameter(Mandatory)][string]$Hash,
        [Parameter(Mandatory)]$Current,
        [Parameter(Mandatory)]$Previous,
        [Parameter(Mandatory)]$Frame,
        [Parameter(Mandatory)]$Normal
    )
    if ($Spec.hash -cne $Hash) { throw "Requested hash does not match its native-family spec." }
    if (!$Spec.safe) { throw "Native family $Hash is explicitly marked unsafe." }
    if ($Assembly -notmatch '(?m)^vs_5_0\s*$') { throw "Expected a vs_5_0 program." }
    if ($Assembly -match '(?m)^(?!//).*\bt119\b') { throw "Native control slot t119 is occupied." }
    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]($Assembly -split '\r?\n'))
    foreach ($declaration in $Spec.requiredDeclarations) {
        $null = Find-NativeFamilyLine $lines $declaration
    }
    foreach ($declaration in $Spec.requiredOutputs) {
        $null = Find-NativeFamilyLine $lines $declaration
    }
    foreach ($field in @(
        "currentRegister", "currentReadSwizzle", "currentWriteMask", "currentWriteSource",
        "previousRegister", "previousReadSwizzle", "previousWriteMask", "previousWriteSource",
        "normalWrite", "normalOutput", "worldOutput", "positionOutput"
    )) {
        if (!$Spec.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$Spec.$field)) {
            throw "Native family $Hash is missing required field '$field'."
        }
    }

    $tempDeclaration = [regex]::Match($Assembly, '(?m)^dcl_temps (\d+)\s*$')
    if (!$tempDeclaration.Success) { throw "Native family has no temporary declaration." }
    $nativeTempCount = [int]$tempDeclaration.Groups[1].Value
    $scratchBase = $nativeTempCount
    $fragmentBase = $scratchBase + 6
    $fragmentCount = @($Current.temps, $Previous.temps, $Frame.temps, $Normal.temps) |
        Measure-Object -Maximum
    $lines[(Find-NativeFamilyLine $lines "dcl_temps $nativeTempCount")] =
        "dcl_temps $($fragmentBase + [int]$fragmentCount.Maximum)"
    $lines.Insert((Find-NativeFamilyLine $lines "dcl_input v0.xyz"),
        "dcl_resource_buffer (float,float,float,float) t119")

    foreach ($entry in @(
        [pscustomobject]@{
            anchor = $Spec.currentAnchor
            register = $Spec.currentRegister
            readSwizzle = $Spec.currentReadSwizzle
            writeMask = $Spec.currentWriteMask
            writeSource = $Spec.currentWriteSource
            inputTemp = "r$scratchBase"
            outputTemp = "r$($scratchBase + 3)"
            fragment = $Current
        },
        [pscustomobject]@{
            anchor = $Spec.previousAnchor
            register = $Spec.previousRegister
            readSwizzle = $Spec.previousReadSwizzle
            writeMask = $Spec.previousWriteMask
            writeSource = $Spec.previousWriteSource
            inputTemp = "r$($scratchBase + 4)"
            outputTemp = "r$($scratchBase + 3)"
            fragment = $Previous
        }
    )) {
        if ($entry.readSwizzle -notmatch '^[xyzw]{4}$' -or
            $entry.writeMask -notmatch '^[xyzw]{3}$' -or
            $entry.writeSource -notmatch '^[xyzw]{4}$') {
            throw "Invalid positional swizzle/write mapping in $Hash."
        }
        $anchorIndex = Find-NativeFamilyLine $lines $entry.anchor
        $patch = [string[]]@("mov $($entry.inputTemp).xyz, $($entry.register).$($entry.readSwizzle)")
        $patch += @(Expand-NativeFamilyFragment $entry.fragment `
            @($entry.inputTemp) @($entry.outputTemp) $fragmentBase)
        $patch += "mov $($entry.register).$($entry.writeMask), $($entry.outputTemp).$($entry.writeSource)"
        $lines.InsertRange($anchorIndex + 1, [string[]]$patch)
    }

    $normalIndex = Find-NativeFamilyLine $lines $Spec.normalWrite
    $normalLine = $Spec.normalWrite
    $normalMatch = [regex]::Match($normalLine, '\bo\d+\.xyz\b')
    if (!$normalMatch.Success) { throw "Normal write must target an exact XYZ output." }
    $lines[$normalIndex] = $normalLine.Replace($normalMatch.Value, "r$($scratchBase + 1).xyz")

    $hasTangent = $null -ne $Spec.tangentWrite
    if ($hasTangent) {
        $tangentIndex = Find-NativeFamilyLine $lines $Spec.tangentWrite
        $tangentLine = $Spec.tangentWrite
        $tangentMatch = [regex]::Match($tangentLine, '\bo\d+\.(?:xyz|xyzw)\b')
        if (!$tangentMatch.Success) { throw "Tangent write must target XYZ or XYZW." }
        $lines[$tangentIndex] = $tangentLine.Replace($tangentMatch.Value,
            "r$($scratchBase + 2).$($tangentMatch.Value.Substring($tangentMatch.Value.IndexOf('.') + 1))")
        if ($tangentMatch.Value.EndsWith(".xyzw")) {
            $lines.Insert($tangentIndex + 1, "mov $($Spec.tangentOutput).w, r$($scratchBase + 2).w")
        }
    }

    $retIndex = Find-NativeFamilyLine $lines "ret"
    if ($hasTangent) {
        $patch = @(Expand-NativeFamilyFragment $Frame `
            @("r$scratchBase", "r$($scratchBase + 1)", "r$($scratchBase + 2)") `
            @("r$($scratchBase + 3)", "r$($scratchBase + 4)", "r$($scratchBase + 5)") $fragmentBase)
        $patch += @("mov $($Spec.normalOutput).xyz, r$($scratchBase + 4).xyzx",
            "mov $($Spec.tangentOutput).xyz, r$($scratchBase + 5).xyzx")
    } else {
        $patch = @(Expand-NativeFamilyFragment $Normal `
            @("r$scratchBase", "r$($scratchBase + 1)") `
            @("r$($scratchBase + 3)", "r$($scratchBase + 4)") $fragmentBase)
        $patch += "mov $($Spec.normalOutput).xyz, r$($scratchBase + 4).xyzx"
    }
    $lines.InsertRange($retIndex, [string[]]$patch)
    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function Get-NativeFamilySpec, New-NativeFamilyAssembly
