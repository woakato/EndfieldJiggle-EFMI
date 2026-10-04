Set-StrictMode -Version Latest

function New-CharacterPickAssembly {
    param([string]$Assembly, $Spec)
    if ($Spec.role -cne 'color' -or $Spec.hash -notin @('1479b2b594b9c91a', 'c6e55aaa8f4b3218')) {
        throw 'Only the two explicitly reviewed native color programs can pick.'
    }
    if ($Assembly -match '(?m)^(?!//).*\bt120\b') { throw 'Picker parameter slot is already occupied.' }
    $program = Get-ProgramText $Assembly
    $outputs = @(Get-ShaderSignature $Assembly Output)
    $position = @($outputs | Where-Object { $_.systemValue -ceq 'POS' })
    $world = @($outputs | Where-Object { $_.name -ceq 'TEXCOORD' -and $_.index -eq 1 -and $_.mask -ceq 'xyz' })
    $normal = @($outputs | Where-Object { $_.name -ceq 'TEXCOORD' -and $_.index -eq 2 -and $_.mask -ceq 'xyz' })
    if ($position.Count -ne 1 -or $world.Count -ne 1 -or $normal.Count -ne 1 -or $Spec.cameraSlot -ne 0) {
        throw 'The picker requires the reviewed clip/world/normal signatures and native camera b0.'
    }
    $count = [int][regex]::Match($program, '(?m)^dcl_temps (\d+)').Groups[1].Value
    $outputCount = ($outputs | ForEach-Object { [int]$_.register } | Measure-Object -Maximum).Maximum + 1
    $clip = "r$($count + $position[0].register)"
    $worldTemp = "r$($count + $world[0].register)"
    $normalTemp = "r$($count + $normal[0].register)"
    $sample = "r$($count + $outputCount)"
    $right = "r$($count + $outputCount + 1)"
    $up = "r$($count + $outputCount + 2)"
    $program = [regex]::Replace($program, '(?m)^dcl_output[^\r\n]*\r?\n', '')
    $program = [regex]::Replace($program, '\bo(\d+)\b', {
        param($match)
        "r$($count + [int]$match.Groups[1].Value)"
    })
    $program = $program.Replace("dcl_temps $count", "dcl_temps $($count + $outputCount + 3)")
    $at = $program.IndexOf('dcl_input v0.xyz', [StringComparison]::Ordinal)
    if ($at -lt 0) { throw 'Expected native position input declaration.' }
    $declarations = @(
        'dcl_resource_texture1d (float,float,float,float) t120'
        'dcl_output_siv o0.xyzw, position'
        'dcl_output o1.xyz'
        'dcl_output o2.xyz'
        'dcl_output o3.xyz'
        'dcl_output o4.xyz'
    )
    $program = $program.Insert($at, ($declarations -join "`n") + "`n")
    $post = @(
        "ld_indexable(texture1d)(float,float,float,float) $sample.xyzw, l(240, 0, 0, 0), t120.xyzw"
        "max $sample.zw, $sample.zzzw, l(0.000000, 0.000000, 1.000000, 1.000000)"
        "div $right.xy, $sample.xyxx, $sample.zwzz"
        "mad $right.xy, $right.xyxx, l(2.000000, -2.000000, 0.000000, 0.000000), l(-1.000000, 1.000000, 0.000000, 0.000000)"
        "mad $right.xy, -$right.xyxx, $clip.wwww, $clip.xyxx"
        "mul o0.xy, $right.xyxx, $sample.zwzz"
        "mov o0.zw, $clip.zzzw"
        "add o1.xyz, $worldTemp.xyzx, cb0[44].xyzx"
        "mov o2.xyz, $normalTemp.xyzx"
        "mov $right.x, cb0[32].x"
        "mov $right.y, cb0[33].x"
        "mov $right.z, cb0[34].x"
        "dp3 $right.w, $right.xyzx, $right.xyzx"
        "max $right.w, $right.w, l(0.00000001)"
        "rsq $right.w, $right.w"
        "mul o3.xyz, $right.xyzx, $right.wwww"
        "mov $up.x, cb0[32].y"
        "mov $up.y, cb0[33].y"
        "mov $up.z, cb0[34].y"
        "dp3 $up.w, $up.xyzx, $up.xyzx"
        "max $up.w, $up.w, l(0.00000001)"
        "rsq $up.w, $up.w"
        "mul o4.xyz, $up.xyzx, $up.wwww"
    )
    $program = $program.Replace("ret`n", ($post -join "`n") + "`nret`n")
    $inputEnd = $Assembly.IndexOf('// Output signature:', [StringComparison]::Ordinal)
    if ($inputEnd -lt 0) { throw 'Missing native input signature.' }
    $signature = @'
// Output signature:
//
// Name                 Index   Mask Register SysValue  Format   Used
// -------------------- ----- ------ -------- -------- ------- ------
// SV_Position              0   xyzw        0      POS   float   xyzw
// TEXCOORD                 0   xyz         1     NONE   float   xyz
// TEXCOORD                 1   xyz         2     NONE   float   xyz
// TEXCOORD                 2   xyz         3     NONE   float   xyz
// TEXCOORD                 3   xyz         4     NONE   float   xyz
//
'@
    return $Assembly.Substring(0, $inputEnd) + $signature + "`n" + $program
}

Export-ModuleMember -Function New-CharacterPickAssembly
