[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$FxcPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
foreach ($module in @('Universal.Common','TouchInput.Common','OutfitRuntime.Common')) {
    Import-Module (Join-Path $PSScriptRoot "$module.psm1") -Force
}
$baseline = (Resolve-Path -LiteralPath $BaselineDirectory).Path
$output = Assert-UniversalPath (Join-Path $root 'reports') $OutputDirectory
if (Test-Path -LiteralPath $output) { throw 'Preserve existing candidate evidence.' }
foreach ($pair in @(
    @('Passes.ini','104CBCB3C2614C1219670C8D598C72A90DEAED756F235D42EB072D9929C509ED'),
    @('Outfits.ini','DC5E5464F7C2F05960886AF9FDD099889443A59224DF34E40AC1956D698A40E3'),
    @('OutfitPreparation.ini','D0EC67C88CE25120532D10C50232F5A6E11944745B4740B5B38712E5E8C094E1')
)) {
    if ((Get-FileHash -LiteralPath (Join-Path $baseline $pair[0])).Hash -cne $pair[1]) {
        throw "Expected accepted native/outfit runtime: $($pair[0])"
    }
}
$baseRuntime = [IO.File]::ReadAllText((Join-Path $baseline 'EndfieldJiggle.ini'))
$sections = @(Read-OrderedIniSections $baseRuntime)
$template = [IO.File]::ReadAllText((Join-Path $root 'runtime\Universal.ini')).TrimEnd()
foreach ($setting in @('radius','max_offset','drag_scale')) {
    $pattern = '(?m)^global \$'+$setting+' = ([0-9.]+)\r?$'
    $value = [regex]::Match($baseRuntime,$pattern)
    if (!$value.Success) { throw "Missing tuning: $setting" }
    $template = [regex]::Replace($template,$pattern,$value.Value.TrimEnd())
}
$shared = @($sections | Where-Object {
    $_.name -cin @('CommandListCursor','CommandListStep','CustomShaderStep','CommandListPickCursor') -or
    $_.name.StartsWith('CustomShaderPick') -or $_.name.StartsWith('ResourceSaved') -or
    $_.name -cin @('ResourceZeroControl','ResourceState','ResourceCapture','ResourceControl',
        'ResourcePickWorld','ResourcePickNormal','ResourcePickRight','ResourcePickUp','ResourcePickDepth')
})
$runtime = Set-UnifiedFrameInput ($template+"`n`n"+
    (($shared | ForEach-Object { Format-OrderedIniSection $_ }) -join "`n`n"))
Assert-NativeOutfitBridge $runtime `
    ([IO.File]::ReadAllText((Join-Path $baseline 'Passes.ini'))) `
    ([IO.File]::ReadAllText((Join-Path $baseline 'Outfits.ini')))
$package = Join-Path $output 'package'
New-Item -ItemType Directory -Path $package | Out-Null
foreach ($name in @('Passes.ini','Outfits.ini','OutfitPreparation.ini','JiggleForge-LICENSE',
    'LICENSE','THIRD-PARTY-NOTICES.md','shaders')) {
    Copy-Item -LiteralPath (Join-Path $baseline $name) -Destination $package -Recurse
}
[IO.File]::WriteAllText((Join-Path $package 'EndfieldJiggle.ini'),$runtime,[Text.UTF8Encoding]::new($false))
$stepSource = Join-Path $package 'shaders\step_cs.hlsl'
$stepCache = Join-Path $package 'shaders\step_cs.cs_5_0.8000.bin'
$sourceText = [IO.File]::ReadAllText((Join-Path $root 'shaders\step_cs.hlsl')).Replace("`r`n","`n")
$baselineText = [IO.File]::ReadAllText($stepSource).Replace("`r`n","`n")
if ($sourceText -cne $baselineText) {
    Copy-Item -LiteralPath (Join-Path $root 'shaders\step_cs.hlsl') -Destination $stepSource -Force
    & $FxcPath /nologo /T cs_5_0 /E main /O3 /Fo $stepCache $stepSource
    if ($LASTEXITCODE -ne 0) { throw 'Step shader compilation failed.' }
    [IO.File]::SetLastWriteTimeUtc($stepCache,[IO.File]::GetLastWriteTimeUtc($stepSource))
}
Copy-Item -LiteralPath (Join-Path $root 'docs\UNIFIED-ALT-INPUT.md') -Destination $output
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($package,$_.FullName)
    $original = Join-Path $baseline $relative
    $hash = (Get-FileHash -LiteralPath $_.FullName).Hash
    if ($relative -cnotin @('EndfieldJiggle.ini','shaders\step_cs.hlsl','shaders\step_cs.cs_5_0.8000.bin')) {
        if ($hash -cne (Get-FileHash -LiteralPath $original).Hash -or
            $_.LastWriteTimeUtc -ne [IO.File]::GetLastWriteTimeUtc($original)) {
            throw "Unrelated payload or timestamp changed: $relative"
        }
    }
    [ordered]@{path=$relative; sha256=$hash; lastWriteTimeUtc=$_.LastWriteTimeUtc.ToString('o')}
})
$zip = Join-Path $output 'EndfieldJiggleEFMI-unified-alt-test.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip)
& (Join-Path $PSScriptRoot 'Test-ReleaseRegression.ps1') -ModZipPath $zip
[ordered]@{
    schema=1; kind='unified-alt-input-candidate'; baselineDirectory=$baseline
    packageDirectory=$package; zipPath=$zip; files=$files
    changedRuntimeFiles=@($files | Where-Object {
        $_.sha256 -cne (Get-FileHash -LiteralPath (Join-Path $baseline $_.path)).Hash
    } | ForEach-Object path)
    nativePickerBytesAndTimesPreserved=$true
    defaultEnabled=$true; dragKey='no_ctrl no_shift alt VK_LBUTTON'
    toggleKey='ctrl shift no_alt VK_F9'; mapping='shared-character-menu'
    liveInstallationModified=$false; inGameVerified=$false
    limitations=@('World cursor alignment still requires in-game validation; no new world capture was supplied.')
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'build-report.json') -Encoding utf8
Write-Output "UNIFIED_INPUT_REPORT=$(Join-Path $output 'build-report.json')"
