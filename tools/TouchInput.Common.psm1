Set-StrictMode -Version Latest

function Set-UnifiedFrameInput {
    param([Parameter(Mandatory)][string]$RuntimeText)
    $sections = @(Read-OrderedIniSections $RuntimeText)
    foreach ($section in $sections) {
        if ($section.name -ceq 'CommandListStep') {
            for ($i = 0; $i -lt $section.lines.Count; $i++) {
                if ($section.lines[$i] -ceq 'run = CommandListCursor') {
                    $section.lines[$i] = 'run = CommandListFrameCursor'
                }
                if ($section.lines[$i] -ceq 'x241 = $drag') {
                    # Key polling may happen after the draw. Wait for a pick sampled while held.
                    $section.lines[$i] = 'x241 = $drag && $pick_drag'
                }
                if ($section.lines[$i] -ceq 'y241 = 1') {
                    $section.lines[$i] = 'y241 = 0'
                }
            }
        }
        if ($section.name -ceq 'CommandListPickCursor' -and
            !($section.lines -contains '    $pick_drag = $drag')) {
            $index = $section.lines.IndexOf('    $pick_height = $height')
            if ($index -lt 0) { throw 'Unknown picker cursor snapshot.' }
            $section.lines.Insert($index + 1, '    $pick_drag = $drag')
        }
    }
    return "namespace = EndfieldJiggleEFMI`n`n" +
        (($sections | ForEach-Object { (Format-OrderedIniSection $_).TrimEnd() }) -join "`n`n") + "`n"
}

Export-ModuleMember -Function Set-UnifiedFrameInput
