Set-StrictMode -Version Latest

function Get-QaqmActiveStateHosts {
    param([Parameter(Mandatory)][string]$EfmiPath)
    $root = (Resolve-Path -LiteralPath $EfmiPath).Path
    $pending = [Collections.Generic.Queue[string]]::new()
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending.Enqueue((Join-Path $root 'd3dx.ini'))
    while ($pending.Count) {
        $path = $pending.Dequeue()
        if (!$visited.Add($path)) { continue }
        if ($visited.Count -gt 1024) { throw 'The explicit INI include graph is unexpectedly large.' }
        if (!(Test-Path -LiteralPath $path)) { throw "An explicit included INI is missing: $path" }
        $text = [IO.File]::ReadAllText($path)
        $namespace = [regex]::Match($text, '(?im)^\s*namespace\s*=\s*QAQM\\Persist\\Bridge_(?<id>[0-9a-f]{12})\s*$')
        if ($namespace.Success) {
            [pscustomobject]@{
                id=$namespace.Groups['id'].Value.ToLowerInvariant()
                path=$path; sha256=(Get-FileHash $path).Hash
                variables=@([regex]::Matches($text, '(?im)^\s*global(?:\s+persist)?\s+\$(?<variable>[A-Za-z_][A-Za-z0-9_]*)') |
                    ForEach-Object { $_.Groups['variable'].Value })
            }
        }
        foreach ($include in [regex]::Matches($text, '(?im)^\s*include\s*=\s*(?<path>[^;\r\n]+)')) {
            $relative = $include.Groups['path'].Value.Trim().Trim('"')
            $target = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $relative))
            if (!$target.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
                $relative -match '[*?$]') { throw 'Cannot safely resolve the explicit INI include graph.' }
            $pending.Enqueue($target)
        }
    }
}

function Get-QaqmStateRecoveryPlan {
    param([Parameter(Mandatory)][string]$SourceDirectory, [string]$EfmiPath)
    $root = (Resolve-Path -LiteralPath $SourceDirectory).Path
    $activeHosts = @{}
    if ($EfmiPath) {
        foreach ($state in Get-QaqmActiveStateHosts $EfmiPath) { $activeHosts[$state.id]=$state.variables }
    }
    $plans = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    $references = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.ini') {
        $text = [IO.File]::ReadAllText($file.FullName)
        foreach ($match in [regex]::Matches($text, '\$\\QAQM\\Persist\\Bridge_(?<id>[0-9a-f]{12})\\(?<variable>[A-Za-z_][A-Za-z0-9_]*)', 'IgnoreCase')) {
            $id = $match.Groups['id'].Value.ToLowerInvariant()
            if (!$references.ContainsKey($id)) {
                $references.Add($id, [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase))
            }
            $references[$id].Add($match.Groups['variable'].Value) | Out-Null
        }
        $markers = [regex]::Matches($text, '(?im)^\s*;\s*Persisted state is hosted by qaqm_state_(?<id>[0-9a-f]{12})\.ini\s*$')
        if (!$markers.Count) { continue }
        if ($markers.Count -ne 1) { throw "Ambiguous QAQM host marker: $($file.Name)" }
        $id = $markers[0].Groups['id'].Value.ToLowerInvariant()
        $hostPath = Join-Path $root "qaqm_state_$id.ini"
        if ((Test-Path -LiteralPath $hostPath) -or $activeHosts.ContainsKey($id)) { continue }
        $backupPath = $file.FullName + '.qaqm-persistbak'
        if (!(Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            throw "Cannot recover QAQM defaults without the matching .qaqm-persistbak: $($file.FullName)"
        }
        $backup = [IO.File]::ReadAllText($backupPath)
        $declarations = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
        $section = ''
        foreach ($line in ($backup -split '\r?\n')) {
            if ($line -match '^\s*\[([^\]]+)\]\s*$') { $section=$Matches[1]; continue }
            if ($section -ieq 'Constants' -and $line -match '^\s*global\s+persist\s+\$(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*(?<default>[+-]?(?:\d+(?:\.\d*)?|\.\d+)))?\s*(?:;.*)?$') {
                $variable = $Matches['variable']
                $value = if ($Matches['default']) { $Matches['default'] } else { '0' }
                $declarations.Add($variable, $value)
            }
        }
        if (!$declarations.Count) { throw "No numeric persisted declarations in $backupPath" }
        if ($plans.ContainsKey($id)) { throw "Multiple source files claim missing QAQM host $id" }
        $lines = [Collections.Generic.List[string]]::new()
        $lines.Add("namespace = QAQM\Persist\Bridge_$id")
        $lines.Add('')
        $lines.Add('[Constants]')
        foreach ($variable in $declarations.Keys) {
            $lines.Add("global persist `$$variable = $($declarations[$variable])")
        }
        $plans.Add($id, [pscustomobject]@{
            id=$id; filename="qaqm_state_$id.ini"; text=($lines -join "`n") + "`n"
            variables=@($declarations.Keys)
            sourceIni=$file.FullName; sourceSha256=(Get-FileHash $file.FullName).Hash
            backup=$backupPath; backupSha256=(Get-FileHash $backupPath).Hash
        })
    }
    foreach ($id in $references.Keys) {
        if ($plans.ContainsKey($id)) {
            $declared = $plans[$id].variables
        } elseif ($activeHosts.ContainsKey($id)) {
            $declared = $activeHosts[$id]
        } else {
            $statePath = Join-Path $root "qaqm_state_$id.ini"
            if (!(Test-Path -LiteralPath $statePath)) { throw "Missing QAQM host $id has no recoverable marker/backup." }
            $hostText = [IO.File]::ReadAllText($statePath)
            if ($hostText -notmatch "(?im)^namespace\s*=\s*QAQM\\Persist\\Bridge_$id\s*$") {
                throw "QAQM host namespace mismatch: $statePath"
            }
            $declared = @([regex]::Matches($hostText, '(?im)^\s*global(?:\s+persist)?\s+\$(?<variable>[A-Za-z_][A-Za-z0-9_]*)') |
                ForEach-Object { $_.Groups['variable'].Value })
        }
        foreach ($variable in $references[$id]) {
            if ($variable -notin $declared) { throw "QAQM reference lacks an evidenced declaration: $id\$variable" }
        }
    }
    return $plans.Values
}

Export-ModuleMember -Function Get-QaqmStateRecoveryPlan, Get-QaqmActiveStateHosts
