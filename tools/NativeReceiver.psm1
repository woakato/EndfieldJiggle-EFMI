Set-StrictMode -Version Latest

function Get-NativeReceiverSpec {
    param([string]$Assembly)
    if ($Assembly -notmatch '(?m)^vs_5_0\r?$' -or
        [regex]::Matches($Assembly,'(?m)^ret[ \t]*\r?$').Count -ne 1 -or
        $Assembly -match '(?m)^\s*(retc_|call|interface_call|label)\b') { return $null }
    $program = Get-ProgramText $Assembly
    $lines = @($program.TrimEnd() -split '\r?\n')
    $instance = [regex]::Match($program, '(?m)^dcl_constantbuffer cb(?<slot>[12])\[409[146]\], dynamicIndexed$')
    if (!$instance.Success -or !$program.Contains('dcl_constantbuffer cb0[82], immediateIndexed') -or
        !$program.Contains('dcl_resource_structured t0, 16') -or $program -match '\bt11[89]\b') {
        return $null
    }
    $slot = [int]$instance.Groups['slot'].Value
    $hooks = @{}
    foreach ($entry in @(@('current',44,3), @('previous',81,9))) {
        $indices = @(0..($lines.Count-2) | Where-Object {
            $lines[$_] -match "^add r\d+\.[xyzw]{3}, -cb0\[$($entry[1])\]\.[xyzw]{4}, cb$slot\[r\d+\.[xyzw] \+ $($entry[2])\]\.[xyzw]{4}$"
        })
        if ($indices.Count -ne 1) { return $null }
        $index = $indices[0]
        $result = [regex]::Match($lines[$index+1], '^add (?<register>r\d+)\.(?<mask>xyz|xyw|xzw|yzw), r\d+\.[xyzw]{4}, r\d+\.[xyzw]{4}$')
        if ($index -lt 3 -or !$result.Success -or $lines[$index-3] -notmatch '^mul ' -or
            $lines[$index-2] -notmatch '^mad ' -or $lines[$index-1] -notmatch '^mad ') { return $null }
        $mask = $result.Groups['mask'].Value
        $write = switch ($mask) { xyz {'xyzx'} xyw {'xyxz'} xzw {'xxyz'} yzw {'xxyz'} }
        $hooks[$entry[0]] = [pscustomobject]@{
            start=$index-3; end=$index+1; register=$result.Groups['register'].Value
            mask=$mask; read=$mask+$mask[0]; write=$write
        }
    }
    if ($hooks.current.mask -cne 'xyz' -or $hooks.current.end -ge $hooks.previous.start) { return $null }
    $normal = $null
    $tangent = $null
    $outputs = @(Get-ShaderSignature $Assembly Output)
    $standard = @($outputs | Where-Object {
        $_.name -ceq 'TEXCOORD' -and $_.index -eq 2 -and $_.mask -ceq 'xyz' -and $_.register -eq 3
    }).Count -eq 1 -and @($outputs | Where-Object {
        $_.name -ceq 'TEXCOORD' -and $_.index -eq 3 -and $_.mask -ceq 'xyzw' -and $_.register -eq 4
    }).Count -eq 1
    $normalRegister = $null
    $tangentRegister = $null
    if ($standard) {
        $normalRegister = 'o3'
        $tangentRegister = 'o4'
    } else {
        # Normal/tangent pairs in the alternate depth/light interface are emitted
        # by independent native normalize operations. Do not interpret clip or UV
        # varyings as normals when this structure is absent.
        $normalized = @([regex]::Matches($program,
            '(?m)^dp3 (?<length>r\d+\.[xyzw]), (?<vector>r\d+\.[xyzw]{4}), \k<vector>\r?\nmax \k<length>, \k<length>, l\([^\r\n]+\)\r?\nrsq \k<length>, \k<length>\r?\nmul (?<output>o\d+)\.xyz, [^\r\n]+\r?\n'))
        if ($normalized.Count -in @(1,2)) {
            $normalRegister = $normalized[0].Groups['output'].Value
            if ($normalized.Count -eq 2) { $tangentRegister = $normalized[1].Groups['output'].Value }
        }
    }
    foreach ($entry in @(@('normal',$normalRegister), @('tangent',$tangentRegister))) {
        if (!$entry[1]) { continue }
        $indices = @(0..($lines.Count-1) | Where-Object { $lines[$_] -match "^(mov|mul) $($entry[1])\.(xyz|xyzw), " })
        if ($indices.Count -ne 1) { throw "Ambiguous receiver surface output $($entry[1])." }
        $hook = [pscustomobject]@{ start=$indices[0]; end=$indices[0]; output=$entry[1]; line=$lines[$indices[0]] }
        if ($entry[0] -ceq 'normal') { $normal=$hook } else { $tangent=$hook }
    }
    return [pscustomobject]@{
        instanceSlot=$slot; current=$hooks.current; previous=$hooks.previous
        normal=$normal; tangent=$tangent; lines=$lines
    }
}

function New-NativeReceiverRule {
    param([string]$Assembly, $Spec, $Current, $Previous, $Frame, $Normal, [string]$Name,
        [switch]$GeometryOnly)
    $lines = $Spec.lines
    $registers = @{}
    $events = @(
        [pscustomobject]@{ kind='current'; hook=$Spec.current }
        [pscustomobject]@{ kind='previous'; hook=$Spec.previous }
        if ($Spec.normal -and !$GeometryOnly) { [pscustomobject]@{ kind='normal'; hook=$Spec.normal } }
        if ($Spec.tangent -and !$GeometryOnly) { [pscustomobject]@{ kind='tangent'; hook=$Spec.tangent } }
    ) | Sort-Object { $_.hook.start }
    function Variable-Line([string]$Line) {
        $parts = [regex]::Split($Line, '(\br\d+\b)')
        $result = foreach ($part in $parts) {
            if ($part -cmatch '^r\d+$') {
                if ($registers.ContainsKey($part)) { '\k<' + $registers[$part] + '>' }
                else {
                    $label = 'native' + $registers.Count
                    $registers[$part] = $label
                    '(?<' + $label + '>r[0-9]+)'
                }
            } else { [regex]::Escape($part) }
        }
        return '^' + ($result -join '') + '\r?\n'
    }
    function Ref([string]$Register) { '${' + $registers[$Register] + '}' }
    $program = Get-ProgramText $Assembly
    # Retain complete native skinning/material blocks. The rule recognizes the
    # interface and both five-instruction world transforms, never a shader hash,
    # mesh identity, material constant, or operator.
    $declarations = @($lines | Where-Object { $_ -match '^dcl_(input|output)' })
    $guards = '(?ims)^(?=vs_5_0\r?$)' +
        '(?![\s\S]*?\bt119\b)(?![\s\S]*?^ret[ \t]*\r?\n[\s\S]*?^ret[ \t]*$)' +
        '(?=[\s\S]*?^dcl_constantbuffer cb0\[82\], immediateIndexed\r?$)' +
        "(?=[\s\S]*?^dcl_constantbuffer cb$($Spec.instanceSlot)\[409[146]\], dynamicIndexed\r?`$)" +
        '(?=[\s\S]*?^dcl_resource_structured t0, 16\r?$)'
    # Require the complete input/output declaration set, preventing a different
    # varying layout from borrowing this rule's normal/tangent interpretation.
    if ($GeometryOnly) {
        $guards += '(?=[\s\S]*?^dcl_input v0\.xyz\r?$)'
    } else {
        $declarationPattern = @($declarations | ForEach-Object { [regex]::Escape($_) + '\r?\n' }) -join ''
        $guards += '(?=[\s\S]*?^' + $declarationPattern + 'dcl_temps [0-9]+\r?\n)'
    }
    $pattern = $guards
    $replacement = [Collections.Generic.List[string]]::new()
    $part = 0
    $last = 0
    foreach ($event in $events) {
        if ($event.hook.start -lt $last) { throw 'Overlapping receiver hooks.' }
        $chunk = 'part' + $part++
        if ($last -eq 0) {
            $pattern += '(?<' + $chunk + '>vs_5_0\r?\n[\s\S]*?)'
        } else { $pattern += '(?<' + $chunk + '>[\s\S]*?)' }
        $replacement.Add('${' + $chunk + '}')
        $anchor = 'anchor' + $part
        $pattern += '(?<' + $anchor + '>'
        foreach ($line in $lines[$event.hook.start..$event.hook.end]) { $pattern += Variable-Line $line }
        $pattern += ')'
        if ($event.kind -ceq 'current' -and $Spec.normal -and !$GeometryOnly) {
            # Redirecting outputs until ret requires unconditional final writes.
            # A new material with conditional outputs must use geometry-only
            # reception, which leaves those native writes in their own branches.
            $pattern += '(?![\s\S]*?^[ \t]*(?:if_[nz]+|else|endif|loop|endloop|switch|case|default|endswitch|retc_[nz]+|call|interface_call|label)\b)'
        }
        if ($event.kind -cin @('current','previous')) {
            $replacement.Add('${' + $anchor + '}')
            $hook = $event.hook
            $input = if ($event.kind -ceq 'current') { 'r0' } else { 'r4' }
            $fragment = if ($event.kind -ceq 'current') { $Current } else { $Previous }
            $replacement.Add("mov $input.xyz, $(Ref $hook.register).$($hook.read)`n")
            $replacement.Add((@(Expand-NativeFamilyFragment $fragment @($input) @('r3') 6) -join "`n") + "`n")
            $replacement.Add("mov $(Ref $hook.register).$($hook.mask), r3.$($hook.write)`n")
        } else {
            $scratch = if ($event.kind -ceq 'normal') { 'r1' } else { 'r2' }
            $line = $event.hook.line.Replace($event.hook.output, $scratch)
            $line = [regex]::Replace($line, '\br\d+\b', {
                param($m)
                # The first operand is our scratch register, all RHS registers
                # are references to the original matched instruction.
                if ($m.Index -eq $line.IndexOf($scratch,[StringComparison]::Ordinal)) { $m.Value }
                else { Ref $m.Value }
            })
            $replacement.Add($line + "`n")
            if ($event.hook.line -match '\.xyzw,') {
                $replacement.Add("mov $($event.hook.output).w, $scratch.w`n")
            }
        }
        $last = $event.hook.end+1
    }
    $pattern += '(?<tail>[\s\S]*?)^ret[ \t]*\r?\n'
    $replacement.Add('${tail}')
    if ($Spec.normal -and !$GeometryOnly) {
        if ($Spec.tangent) {
            $replacement.Add((@(Expand-NativeFamilyFragment $Frame @('r0','r1','r2') @('r3','r4','r5') 6) -join "`n") + "`n")
            $replacement.Add("mov $($Spec.normal.output).xyz, r4.xyzx`nmov $($Spec.tangent.output).xyz, r5.xyzx`n")
        } else {
            $replacement.Add((@(Expand-NativeFamilyFragment $Normal @('r0','r1') @('r3','r4') 6) -join "`n") + "`n")
            $replacement.Add("mov $($Spec.normal.output).xyz, r4.xyzx`n")
        }
    }
    $replacement.Add("ret`n")
    $count = 6 + (@($Current.temps,$Previous.temps,$Frame.temps,$Normal.temps) | Measure-Object -Maximum).Maximum
    $temps = @(0..($count-1) | ForEach-Object { 'recv' + $_ })
    $text = [regex]::Replace(($replacement -join ''), '\br(\d+)\b', {
        param($m)
        '${recv' + $m.Groups[1].Value + '}'
    })
    $rule = [pscustomobject]@{
        name=$Name; pattern=$pattern; replacement=$text; temps=$temps
        instanceSlot=$Spec.instanceSlot; normalUpdated=($null -ne $Spec.normal -and !$GeometryOnly)
        tangentUpdated=($null -ne $Spec.tangent -and !$GeometryOnly); geometryFallback=[bool]$GeometryOnly
    }
    if (![regex]::IsMatch($program,$pattern,[Text.RegularExpressions.RegexOptions]::None,[TimeSpan]::FromSeconds(2))) {
        throw 'Receiver template does not match its source.'
    }
    return $rule
}

function Invoke-NativeReceiverRule {
    param([string]$Assembly, $Rule)
    $regex = [regex]::new($Rule.pattern,[Text.RegularExpressions.RegexOptions]::None,[TimeSpan]::FromSeconds(2))
    $match = $regex.Match($Assembly)
    if (!$match.Success) { return $null }
    $base = [int][regex]::Match($Assembly,'(?m)^dcl_temps (\d+)').Groups[1].Value
    $replacement = $Rule.replacement
    for ($i=0; $i -lt $Rule.temps.Count; $i++) { $replacement=$replacement.Replace('${'+$Rule.temps[$i]+'}',"r$($base+$i)") }
    $updated = $match.Result($replacement)
    $updated = $updated.Replace("dcl_temps $base`n","dcl_temps $($base+$Rule.temps.Count)`n")
    $updated = $updated.Replace("dcl_temps $base`r`n","dcl_temps $($base+$Rule.temps.Count)`n")
    $updated = $updated.Replace('dcl_input v0.xyz',"dcl_resource_buffer (float,float,float,float) t119`ndcl_input v0.xyz")
    return $Assembly.Substring(0,$match.Index) + $updated + $Assembly.Substring($match.Index+$match.Length)
}

function Format-NativeReceiverRule {
    param($Rule)
    $name = $Rule.name
    return @(
        "[$name]", 'shader_model = vs_5_0', ('temps = ' + ($Rule.temps -join ' ')),
        "run = CommandListReceiveNative$($Rule.instanceSlot)",
        'post vs-t119 = ref ResourceSavedVsControl', '',
        "[$name.Pattern]", $Rule.pattern, '',
        "[$name.Pattern.Replace]", $Rule.replacement.Replace("`n",'\n'+"`n").TrimEnd(), '',
        "[$name.InsertDeclarations]", 'dcl_resource_buffer (float,float,float,float) t119', ''
    ) -join "`n"
}

Export-ModuleMember -Function Get-NativeReceiverSpec, New-NativeReceiverRule, Invoke-NativeReceiverRule, Format-NativeReceiverRule
