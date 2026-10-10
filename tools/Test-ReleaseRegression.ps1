[CmdletBinding()]
param([Parameter(Mandatory)][string]$ModZipPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'OutfitRuntime.Common.psm1') -Force
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ModZipPath).Path)
try {
    $texts = @{}
    $files = @($archive.Entries | Where-Object {
        $_.FullName -match '/Mods/EndfieldJiggleEFMI/(?!Configurator/)' -and ! $_.FullName.EndsWith('/')
    })
    if ($files.Count -ne 14) { throw 'Release must include all 14 native/outfit runtime files.' }
    foreach ($name in @('EndfieldJiggle.ini','Passes.ini','Outfits.ini')) {
        $entry = @($files | Where-Object { $_.FullName.EndsWith('/' + $name) })
        if ($entry.Count -ne 1) { throw "Missing/duplicate release INI: $name" }
        $reader = [IO.StreamReader]::new($entry[0].Open())
        try { $texts[$name]=$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
} finally { $archive.Dispose() }
$runtime=$texts['EndfieldJiggle.ini']
$passes=$texts['Passes.ini']
$outfits=$texts['Outfits.ini']
Assert-NativeOutfitBridge $runtime $passes $outfits
$checks = 1
function RejectBridge([string]$Runtime,[string]$Passes,[string]$Outfits) {
    $rejected=$false
    try { Assert-NativeOutfitBridge $Runtime $Passes $Outfits } catch { $rejected=$true }
    if (!$rejected) { throw 'A bridge regression escaped the release gate.' }
}
RejectBridge ($runtime.Replace('if $outfit_active','if $outfit_disabled')) $passes $outfits
RejectBridge $runtime ($passes.Replace('if !$outfit_seen &&','if ')) $outfits
RejectBridge $runtime $passes ($outfits.Replace('hash = 1479b2b594b9c91a','hash = 0000000000000000'))
RejectBridge $runtime $passes ($outfits.Replace('allow_duplicate_hash = true','filter_index = 999'))
RejectBridge ($runtime.Replace(' || ($ui_mouse && !$drag) ||',' || $ui_mouse ||')) $passes $outfits
$checks += 5
if ($runtime -notmatch '(?m)^global \$enabled = 0\r?$') { throw 'The release must retain default-off startup.' }
if ($runtime -notmatch '(?m)^key = VK_LBUTTON\r?$') { throw 'Mouse navigation must work with custom chords.' }
if ($outfits -notmatch 'vs-t119 = ref ResourceSavedOutfitVsControl') { throw 'Outfit draws must restore foreign SRVs.' }
$checks += 3
Write-Output "PASS: $checks release regression checks, including missing bridge, wrong program and input-liveness negative controls."
