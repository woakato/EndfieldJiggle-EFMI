[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
$checks = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'tools') -File) {
    if ($file.Extension -notin @('.ps1','.psm1')) { continue }
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw "PowerShell parse errors in $($file.Name): $errors" }
    $checks++
}
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$iniPath = Join-Path $root 'runtime\Universal.ini'
$ini = [IO.File]::ReadAllText($iniPath)
$sections = @(Read-OrderedIniSections $ini)
if ($sections.Count -lt 10 -or $ini -notmatch '(?m)^global \$enabled = 0$') {
    throw 'Missing native session sections or default-off state.'
}
if ($ini -match '(?i)TextureOverrideCaptured|\$active_character|\$mesh_character|\$overview_primary') {
    throw 'Unexpected per-operator eligibility in universal template.'
}
$checks += 2
foreach ($name in @('NativeFamilyAdapter','NativePickAdapter','ShaderPatch','ShaderProbe.Common','TouchAdapter.Common','LoaderDiagnostics')) {
    Import-Module (Join-Path $PSScriptRoot "$name.psm1") -Force -DisableNameChecking
    $checks++
}
foreach ($relative in @(
    'LICENSE',
    'third_party\JiggleForge\LICENSE',
    'third_party\JiggleForge\THIRD-PARTY-NOTICES.md',
    'third_party\JiggleForge\BRANDING.md',
    'shaders\step_cs.hlsl',
    'shaders\character_pick_ps.hlsl',
    'shaders\field_vs.hlsl'
)) {
    if (!(Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        throw "Missing public source or notice: $relative"
    }
    $checks++
}
Write-Output "Public source checks passed: $checks. No game deployment or complete runtime test was performed."
