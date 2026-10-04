Set-StrictMode -Version Latest

function Read-OrderedIniSections {
    param([Parameter(Mandatory)][string]$Text)
    $sections = [Collections.Generic.List[object]]::new()
    $current = $null
    $names = @{}
    foreach ($line in ($Text -split '\r?\n')) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('[') -and $trimmed.EndsWith(']')) {
            $name = $trimmed.Substring(1, $trimmed.Length - 2)
            if ($names.ContainsKey($name)) { throw "Duplicate native INI section: $name" }
            $names[$name] = $true
            $current = [pscustomobject]@{ name=$name; lines=[Collections.Generic.List[string]]::new() }
            $sections.Add($current)
        } elseif ($null -ne $current) { $current.lines.Add($line) }
    }
    return $sections.ToArray()
}

function Format-OrderedIniSection {
    param([Parameter(Mandatory)]$Section)
    return "[$($Section.name)]`n" + ($Section.lines -join "`n")
}

function Assert-UniversalPath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($Path)
    if (!$resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Universal evidence path leaves its workspace boundary.'
    }
    $current = $resolved
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Reparse point is not allowed in universal evidence: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $resolved
}

function Get-UniversalReport {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$ReportPath)
    $reportRoot = Join-Path $Root 'reports\universal'
    $path = Assert-UniversalPath $reportRoot $ReportPath
    $directory = Split-Path -Parent $path
    if ((Split-Path -Parent $directory) -ine ([IO.Path]::GetFullPath($reportRoot)) -or
        [IO.Path]::GetFileName($path) -cne 'build-report.json') {
        throw 'Universal report must be reports\universal\<unique>\build-report.json.'
    }
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $parentPath = Join-Path $Root 'reports\characters-build\build-report.json'
    $accepted = '8A60B7D34B59ECE20716377ABE20F25EB506CF92AF722E4B7B2204DAD29AC0D2'
    if ($report.buildKind -cne 'universal-native-families' -or
        $report.parentBuildReportSha256 -cne $accepted -or
        (Get-FileHash -LiteralPath $parentPath).Hash -cne $accepted) {
        throw 'Universal build must reuse the unchanged accepted native shader evidence.'
    }
    $parent = Get-Content -LiteralPath $parentPath -Raw | ConvertFrom-Json
    $package = Assert-UniversalPath $directory $report.packageDirectory
    if ($package -ine (Join-Path $directory 'package\Mods\EndfieldJiggleEFMI') -or
        $report.liveInstallationModified -isnot [bool] -or $report.liveInstallationModified -or
        $report.inGameVerified -isnot [bool] -or $report.inGameVerified -or
        $report.requiresCharacterProfiles -isnot [bool] -or $report.requiresCharacterProfiles) {
        throw 'Universal package scope or verification flags are invalid.'
    }
    foreach ($property in @('passes','pickPrograms')) {
        if (($report.$property | ConvertTo-Json -Depth 30 -Compress) -cne
            ($parent.$property | ConvertTo-Json -Depth 30 -Compress)) {
            throw "Native shader evidence changed: $property"
        }
    }
    if ((@($report.files.path | Sort-Object) -join "`n") -cne
        (@($parent.files.path | Sort-Object) -join "`n")) {
        throw 'Universal package must retain the accepted owned-file set.'
    }
    foreach ($file in $report.files) {
        if ([IO.Path]::IsPathRooted($file.path)) { throw 'Universal file path must be relative.' }
        $source = Assert-UniversalPath $package (Join-Path $package $file.path)
        if ((Get-FileHash -LiteralPath $source).Hash -cne $file.sha256 -or
            [IO.File]::GetLastWriteTimeUtc($source).Ticks -ne ([DateTimeOffset]$file.lastWriteTimeUtc).UtcDateTime.Ticks) {
            throw "Universal file bytes/FILETIME changed: $($file.path)"
        }
        $previous = @($parent.files | Where-Object path -CEQ $file.path)[0]
        if ($file.path -cnotin @('EndfieldJiggle.ini', 'Passes.ini') -and
            ($file.sha256 -cne $previous.sha256 -or $file.lastWriteTimeUtc -cne $previous.lastWriteTimeUtc)) {
            throw 'Universal eligibility may not change the accepted shader/cache payloads.'
        }
    }
    return [pscustomobject]@{ Path=$path; Directory=$directory; Package=$package; Report=$report }
}

Export-ModuleMember -Function Read-OrderedIniSections, Format-OrderedIniSection, Assert-UniversalPath, Get-UniversalReport
