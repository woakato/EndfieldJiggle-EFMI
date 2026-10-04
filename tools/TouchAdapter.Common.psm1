Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-TouchLine {
    param([Collections.Generic.List[string]]$Lines, [string]$Expected)
    $matches = @(for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i].Trim() -ceq $Expected) { $i }
    })
    if ($matches.Count -ne 1) { throw "Adapter anchor is missing or ambiguous: $Expected" }
    return [int]$matches[0]
}

function Get-TouchFragment {
    param([string]$Assembly)
    if ($Assembly -notmatch '(?m)^vs_5_0\s*$' -or
        $Assembly -notmatch '(?m)^dcl_temps (\d+)\s*$') {
        throw "Expected an FXC vs_5_0 fragment with a temporary count."
    }
    $temps = [int]$Matches[1]
    $instructions = [Collections.Generic.List[string]]::new()
    $started = $false
    foreach ($line in $Assembly -split '\r?\n') {
        if ($line.Trim() -eq "vs_5_0") { $started = $true; continue }
        if (-not $started) { continue }
        $code = ($line -replace '//.*$', '').Trim()
        if (!$code -or $code -match '^dcl_') { continue }
        $instructions.Add($code)
    }
    if (@($instructions | Where-Object { $_ -eq "ret" }).Count -ne 1 -or
        $instructions[$instructions.Count - 1] -ne "ret" -or
        @($instructions | Where-Object { $_ -match '^(retc|call|label)(?:_|\b)' }).Count) {
        throw "Fragments must have a single unconditional final return."
    }
    $instructions.RemoveAt($instructions.Count - 1)
    return [pscustomobject]@{ temps = $temps; lines = $instructions.ToArray() }
}

function Expand-TouchFragment {
    param($Fragment, [string[]]$Inputs, [string[]]$Outputs, [int]$TempBase = 21)
    foreach ($line in $Fragment.lines) {
        $code = [regex]::Replace($line, '\br(\d+)\b', {
            param($m)
            "r$($TempBase + [int]$m.Groups[1].Value)"
        })
        $code = [regex]::Replace($code, '\bv(\d+)\b', {
            param($m)
            $index = [int]$m.Groups[1].Value
            if ($index -ge $Inputs.Count) { throw "Unmapped fragment input." }
            $Inputs[$index]
        })
        $code = [regex]::Replace($code, '\bo(\d+)\b', {
            param($m)
            $index = [int]$m.Groups[1].Value
            if ($index -ge $Outputs.Count) { throw "Unmapped fragment output." }
            $Outputs[$index]
        })
        $code
    }
}

function New-EndfieldTouchAssembly {
    param([string]$Assembly, [ValidateSet("body", "outline")][string]$Kind,
          $CurrentFragment, $PreviousFragment, $FrameFragment)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]($Assembly -split '\r?\n'))
    if ($Assembly -match '(?m)^[^/]*\b(?:t119|r15|r16|r17|r18|r19|r20)\b') {
        throw "Touch resources or reserved temporaries are occupied."
    }
    $expectedTemps = if ($Kind -eq "body") { 15 } else { 13 }
    $tempIndex = Find-TouchLine $lines "dcl_temps $expectedTemps"
    $maxTemps = @($CurrentFragment.temps, $PreviousFragment.temps, $FrameFragment.temps) |
        Measure-Object -Maximum
    $lines[$tempIndex] = "dcl_temps $([int]$maxTemps.Maximum + 21)"
    $inputIndex = Find-TouchLine $lines "dcl_input v0.xyz"
    $lines.Insert($inputIndex, "dcl_resource_buffer (float,float,float,float) t119")
    $null = Find-TouchLine $lines "dcl_constantbuffer CB0[82], immediateIndexed"
    $currentAnchor = if ($Kind -eq "body") {
        "add r4.xyz, r4.xyzx, r7.xyzx"
    } else { "add r2.xyz, r2.xyzx, r3.xyzx" }
    $worldRegister = if ($Kind -eq "body") { "r4" } else { "r2" }
    $index = Find-TouchLine $lines $currentAnchor
    $patch = @("mov r15.xyz, $worldRegister.xyzx")
    $patch += @(Expand-TouchFragment $CurrentFragment @("r15") @("r18"))
    $patch += "mov $worldRegister.xyz, r18.xyzx"
    $lines.InsertRange($index + 1, [string[]]$patch)

    $previousAnchor = if ($Kind -eq "body") {
        "add r0.xyz, r0.xzwx, r3.yzwy"
    } else { "add r4.xyz, r4.xyzx, r5.xyzx" }
    $previousRegister = if ($Kind -eq "body") { "r0" } else { "r4" }
    $index = Find-TouchLine $lines $previousAnchor
    $patch = @("mov r19.xyz, $previousRegister.xyzx")
    $patch += @(Expand-TouchFragment $PreviousFragment @("r19") @("r18"))
    $patch += "mov $previousRegister.xyz, r18.xyzx"
    $lines.InsertRange($index + 1, [string[]]$patch)

    if ($Kind -eq "body") {
        $lines[(Find-TouchLine $lines "mul o3.xyz, r2.wwww, r3.yzwy")] =
            "mul r16.xyz, r2.wwww, r3.yzwy"
        $lines[(Find-TouchLine $lines "mul o4.xyz, r0.xzwx, r2.wwww")] =
            "mul r17.xyz, r0.xzwx, r2.wwww"
    } else {
        $lines[(Find-TouchLine $lines "mov o3.xyz, r1.xywx")] = "mov r16.xyz, r1.xywx"
        $index = Find-TouchLine $lines "mov o4.xyzw, r0.xyzw"
        $lines[$index] = "mov r17.xyz, r0.xyzx"
        $lines.Insert($index + 1, "mov o4.w, r0.w")
    }
    $framePatch = @(Expand-TouchFragment $FrameFragment @("r15", "r16", "r17") @("r18", "r19", "r20"))
    $framePatch += @("mov o3.xyz, r19.xyzx", "mov o4.xyz, r20.xyzx")
    $lines.InsertRange((Find-TouchLine $lines "ret"), [string[]]$framePatch)
    return ($lines -join "`n") + "`n"
}

function New-EndfieldDualPositionAssembly {
    param([string]$Assembly, $CurrentFragment, $PreviousFragment,
          [string]$CurrentAnchor, [string]$CurrentRegister,
          [string]$PreviousAnchor, [string]$PreviousRegister)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]($Assembly -split '\r?\n'))
    if ($Assembly -match '(?m)^[^/]*\b(?:t119|r15|r16|r17|r18|r19|r20)\b') {
        throw "Dual-position touch resources or reserved temporaries are occupied."
    }
    $tempDeclaration = [regex]::Match($Assembly, '(?m)^dcl_temps (\d+)\s*$')
    if (-not $tempDeclaration.Success) { throw "Dual-position shader has no temporary declaration." }
    $tempIndex = [int]$tempDeclaration.Groups[1].Value
    $fragmentTemps = [Math]::Max($CurrentFragment.temps, $PreviousFragment.temps)
    $requiredTemps = [Math]::Max($tempIndex, $fragmentTemps + 21)
    $lines[(Find-TouchLine $lines "dcl_temps $tempIndex")] = "dcl_temps $requiredTemps"
    $lines.Insert((Find-TouchLine $lines "dcl_input v0.xyz"),
        "dcl_resource_buffer (float,float,float,float) t119")

    $index = Find-TouchLine $lines $CurrentAnchor
    $patch = [string[]]@("mov r15.xyz, $CurrentRegister.xyzx")
    $patch += @(Expand-TouchFragment $CurrentFragment @("r15") @("r18"))
    $patch += "mov $CurrentRegister.xyz, r18.xyzx"
    $lines.InsertRange($index + 1, [string[]]$patch)

    $index = Find-TouchLine $lines $PreviousAnchor
    $patch = [string[]]@("mov r19.xyz, $PreviousRegister.xyzx")
    $patch += @(Expand-TouchFragment $PreviousFragment @("r19") @("r18"))
    $patch += "mov $PreviousRegister.xyz, r18.xyzx"
    $lines.InsertRange($index + 1, [string[]]$patch)
    return ($lines -join "`n") + "`n"
}

function New-EndfieldPickAssembly {
    param([string]$Assembly)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]($Assembly -split '\r?\n'))
    if ($Assembly -match '(?m)^[^/]*\b(?:t120|r15|r16|r17|r18)\b') {
        throw "Pick resources or reserved temporaries are occupied."
    }
    $lines[(Find-TouchLine $lines "dcl_temps 15")] = "dcl_temps 19"
    $lines.Insert((Find-TouchLine $lines "dcl_input v0.xyz"),
        "dcl_resource_texture1d (float,float,float,float) t120")
    $lines[(Find-TouchLine $lines "mad o0.xy, -r8.xyxx, l(2.000000, -2.000000, 0.000000, 0.000000), r7.xyxx")] =
        "mad r15.xy, -r8.xyxx, l(2.000000, -2.000000, 0.000000, 0.000000), r7.xyxx"
    $lines[(Find-TouchLine $lines "mov o0.zw, r7.zzzw")] = "mov r15.zw, r7.zzzw"
    $lines[(Find-TouchLine $lines "mov o2.xyz, r4.xyzx")] = "add o2.xyz, r4.xyzx, cb0[44].xyzx"
    $patch = [string[]]@(
        "ld_indexable(texture1d)(float,float,float,float) r16.xyzw, l(240, 0, 0, 0), t120.xyzw",
        "max r16.zw, r16.zzzw, l(0.000000, 0.000000, 1.000000, 1.000000)",
        "div r17.xy, r16.xyxx, r16.zwzz",
        "mad r17.xy, r17.xyxx, l(2.000000, -2.000000, 0.000000, 0.000000), l(-1.000000, 1.000000, 0.000000, 0.000000)",
        "mad r17.xy, -r17.xyxx, r15.wwww, r15.xyxx",
        "mul o0.xy, r17.xyxx, r16.zwzz",
        "mov o0.zw, r15.zzzw",
        "mov r17.x, cb0[32].x", "mov r17.y, cb0[33].x", "mov r17.z, cb0[34].x",
        "dp3 r17.w, r17.xyzx, r17.xyzx", "max r17.w, r17.w, l(0.00000001)",
        "rsq r17.w, r17.w", "mul o7.xyz, r17.xyzx, r17.wwww",
        "mov r18.x, cb0[32].y", "mov r18.y, cb0[33].y", "mov r18.z, cb0[34].y",
        "dp3 r18.w, r18.xyzx, r18.xyzx", "max r18.w, r18.w, l(0.00000001)",
        "rsq r18.w, r18.w", "mul o8.xyz, r18.xyzx, r18.wwww"
    )
    $lines.InsertRange((Find-TouchLine $lines "ret"), $patch)
    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function Get-TouchFragment, New-EndfieldTouchAssembly,
    New-EndfieldDualPositionAssembly, New-EndfieldPickAssembly
