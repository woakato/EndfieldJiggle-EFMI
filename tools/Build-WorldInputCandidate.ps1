[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'OutfitRuntime.Common.psm1') -Force
$baseline = (Resolve-Path -LiteralPath $BaselineDirectory).Path
$output = Assert-UniversalPath (Join-Path $root 'reports') $OutputDirectory
if (Test-Path -LiteralPath $output) { throw 'Preserve existing candidate evidence.' }

$reviewed = @{
    'Passes.ini' = '104CBCB3C2614C1219670C8D598C72A90DEAED756F235D42EB072D9929C509ED'
    'Outfits.ini' = 'DC5E5464F7C2F05960886AF9FDD099889443A59224DF34E40AC1956D698A40E3'
    'OutfitPreparation.ini' = 'D0EC67C88CE25120532D10C50232F5A6E11944745B4740B5B38712E5E8C094E1'
}
foreach ($name in $reviewed.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $baseline $name)).Hash -cne $reviewed[$name]) {
        throw "The candidate requires the accepted outfit-complete runtime: $name"
    }
}
$path = Join-Path $baseline 'EndfieldJiggle.ini'
$before = [IO.File]::ReadAllText($path)
$sections = @(Read-OrderedIniSections $before)
$dragSection = @($sections | Where-Object name -CEQ 'KeyDrag')
if ($dragSection.Count -ne 1) { throw 'Expected one drag binding.' }
$keyLines = @($dragSection[0].lines | Where-Object { $_ -match '^\s*key\s*=' })
if ($keyLines.Count -ne 1) { throw 'Expected one drag key expression.' }
$keyBefore = ($keyLines[0] -split '=',2)[1].Trim()
$tokens = @($keyBefore -split '\s+')
if ($tokens[-1] -cne 'VK_LBUTTON' -or
    'no_alt' -notin $tokens -or 'no_modifiers' -in $tokens) {
    throw 'Expected an explicit left-button chord with no_alt; preserve other chords for manual review.'
}
$keyAfter = (@($tokens | Where-Object { $_ -cne 'no_alt' }) -join ' ')
if (@($sections | Where-Object name -CEQ 'KeyWorldCursorMapping').Count) {
    throw 'World input extension already exists.'
}
if ($before -notmatch '(?m)^global \$cursor_flip_y = 1\r?$') {
    throw 'The accepted menu mapping must remain the startup default.'
}
$newline = if ($before.Contains("`r`n")) { "`r`n" } else { "`n" }
function Replace-Once([string]$Text, [string]$Old, [string]$New) {
    if ([regex]::Matches($Text,[regex]::Escape($Old)).Count -ne 1) {
        throw "Expected a single reviewed edit anchor: $Old"
    }
    return $Text.Replace($Old,$New)
}
$after = Replace-Once $before ("[KeyDrag]"+$newline+$keyLines[0]) `
    ("[KeyDrag]"+$newline+"key = $keyAfter")
$after = Replace-Once $after ('global $cursor_flip_y = 1') `
    ('global $cursor_flip_y = 1'+$newline+'global $world_mapping_notice_until = 0')
$statusAnchor = 'Resource\EFMIv1\TextParams = ref ResourceStatusParams'+$newline+'if $mod_id < 0'
$status = @'
Resource\EFMIv1\TextParams = ref ResourceStatusParams
if time < $world_mapping_notice_until
    if $cursor_flip_y
        Resource\EFMIv1\Text = ref ResourceStatusMappingMenu
    else
        Resource\EFMIv1\Text = ref ResourceStatusMappingWorld
    endif
elif $mod_id < 0
'@ -replace '\r?\n',$newline
$after = Replace-Once $after $statusAnchor $status
$extension = @'

[KeyWorldCursorMapping]
; Explicit mapping selection uses the accepted pickers and solver unchanged.
key = ctrl shift no_alt VK_F6
run = CommandListToggleWorldCursorMapping

[CommandListToggleWorldCursorMapping]
; A mapping change is only allowed between gestures.
if !$drag && !$ui_mouse
    $cursor_flip_y = !$cursor_flip_y
    $session_frames = 0
    $complete = 0
    $cancelled = 1
    $pick_started = 0
    clear = ResourceState 0
    clear = ResourceCapture 0
    clear = ResourceControl 0
    clear = ResourcePickWorld 0
    clear = ResourcePickDepth depth 0
    $world_mapping_notice_until = time + 4
    $notice_until = time + 4
endif

[ResourceStatusMappingMenu]
type = Buffer
data = "EJ: CURSOR MAPPING / CHARACTER MENU"

[ResourceStatusMappingWorld]
type = Buffer
data = "EJ: CURSOR MAPPING / WORLD TEST"
'@ -replace '\r?\n',$newline
$after = $after.TrimEnd()+$newline+$extension+$newline
$null = Read-OrderedIniSections $after
Assert-NativeOutfitBridge $after `
    ([IO.File]::ReadAllText((Join-Path $baseline 'Passes.ini'))) `
    ([IO.File]::ReadAllText((Join-Path $baseline 'Outfits.ini')))

$package = Join-Path $output 'package'
New-Item -ItemType Directory -Path $package | Out-Null
$names = @('EndfieldJiggle.ini','Passes.ini','Outfits.ini','OutfitPreparation.ini',
    'JiggleForge-LICENSE','LICENSE','THIRD-PARTY-NOTICES.md','shaders')
foreach ($name in $names) {
    Copy-Item -LiteralPath (Join-Path $baseline $name) -Destination $package -Recurse
}
[IO.File]::WriteAllText((Join-Path $output 'EndfieldJiggle.before.ini'),$before,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $package 'EndfieldJiggle.ini'),$after,[Text.UTF8Encoding]::new($false))
$files = @(foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($package,$file.FullName)
    $original = Join-Path $baseline $relative
    $originalHash = (Get-FileHash -LiteralPath $original).Hash
    $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
    $timePreserved = $file.LastWriteTimeUtc -eq [IO.File]::GetLastWriteTimeUtc($original)
    if ($relative -cne 'EndfieldJiggle.ini' -and ($hash -cne $originalHash -or !$timePreserved)) {
        throw "Unexpected payload or timestamp change: $relative"
    }
    [ordered]@{ path=$relative; sha256=$hash; baselineSha256=$originalHash; timePreserved=$timePreserved }
})
$pairs = @(
    @('pick_1479b2b594b9c91a.hlsl','pick_1479b2b594b9c91a.vs_5_0.8000.bin'),
    @('pick_c6e55aaa8f4b3218.hlsl','pick_c6e55aaa8f4b3218.vs_5_0.8000.bin'),
    @('character_pick_ps.hlsl','character_pick_ps.ps_5_0.8000.bin'),
    @('step_cs.hlsl','step_cs.cs_5_0.8000.bin')
)
foreach ($pair in $pairs) {
    if ([IO.File]::GetLastWriteTimeUtc((Join-Path $package "shaders\$($pair[0])")) -ne
        [IO.File]::GetLastWriteTimeUtc((Join-Path $package "shaders\$($pair[1])"))) {
        throw "Source/cache timestamp mismatch: $($pair[0])"
    }
}
$report = [ordered]@{
    schema=1; kind='world-input-ini-only-candidate'; baselineDirectory=$baseline
    packageDirectory=$package; changedFiles=@('EndfieldJiggle.ini')
    dragKeyBefore=$keyBefore; dragKeyAfter=$keyAfter
    mappingToggle='ctrl shift no_alt VK_F6'; startupMapping='character-menu'
    files=$files; sourceCachePairs=$pairs.Count
    inGameVerified=$false; liveInstallationModified=$false; publicationApproved=$false
    limitations=@(
        'World mapping is an explicit test, not automatic scene detection.',
        'World draws must already use a reviewed color picker; no new shader family is added.',
        'The stable configurator may restore no_alt when saving bindings; this candidate omits it.'
    )
}
[IO.File]::WriteAllText((Join-Path $output 'build-report.json'),
    ($report | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output "CANDIDATE_REPORT=$(Join-Path $output 'build-report.json')"
