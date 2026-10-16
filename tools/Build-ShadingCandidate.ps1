[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseModZipPath,
    [Parameter(Mandatory)][string]$NativeBuildReportPath,
    [Parameter(Mandatory)][string]$AssemblerPath,
    [string]$FxcPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\fxc.exe',
    [string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
foreach ($module in @('Universal.Common','TouchAdapter.Common','ShaderProbe.Common',
    'NativeFamilyAdapter','NativePickAdapter','ShaderPatch','OutfitRuntime.Common')) {
    Import-Module (Join-Path $PSScriptRoot "$module.psm1") -Force -DisableNameChecking
}
$baseZip = (Resolve-Path -LiteralPath $BaseModZipPath).Path
$parentPath = (Resolve-Path -LiteralPath $NativeBuildReportPath).Path
$baseHash = (Get-FileHash -LiteralPath $baseZip).Hash
$parentHash = (Get-FileHash -LiteralPath $parentPath).Hash
if ($baseHash -cne '87F05BFF53B9D7413FAB2E0EEF9EE840068F38451E2E89DB3C0E5E299AD8408E' -or
    $parentHash -cne '8A60B7D34B59ECE20716377ABE20F25EB506CF92AF722E4B7B2204DAD29AC0D2') {
    throw 'Use the reviewed v0.2.2 Mod ZIP and unchanged native build evidence.'
}
$parent = Get-Content -LiteralPath $parentPath -Raw | ConvertFrom-Json
$parentDirectory = Split-Path -Parent $parentPath
$build = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $root ('reports\shading-maintenance\candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$build = Assert-UniversalPath (Join-Path $root 'reports') $build
if (Test-Path -LiteralPath $build) { throw 'Preserve existing candidate evidence.' }
$product = Join-Path $build 'package\EndfieldJiggleEFMI-v0.2.2-shading-candidate'
$runtime = Join-Path $product 'Mods\EndfieldJiggleEFMI'
$assemblyDirectory = Join-Path $build 'assembly'
New-Item -ItemType Directory -Path $runtime,$assemblyDirectory -Force | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
$originalFiles = @{}
$archive = [IO.Compression.ZipFile]::OpenRead($baseZip)
try {
    $prefix = 'EndfieldJiggleEFMI-v0.2.2/'
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        if (!$entry.FullName.StartsWith($prefix,[StringComparison]::Ordinal)) {
            throw 'Unexpected base package root.'
        }
        $relative = $entry.FullName.Substring($prefix.Length)
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or
            @($relative -split '[/\\]' | Where-Object { $_ -in @('.','..','') }).Count) {
            throw 'Unsafe base entry.'
        }
        $target = Assert-UniversalPath $product (Join-Path $product $relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
        $originalFiles[$relative] = (Get-FileHash -LiteralPath $target).Hash
    }
} finally { $archive.Dispose() }
$passesPath = Join-Path $runtime 'Passes.ini'
$sections = @(Read-OrderedIniSections ([IO.File]::ReadAllText($passesPath)))
function Section([string]$Name) {
    $found = @($sections | Where-Object name -CEQ $Name)
    if ($found.Count -ne 1) { throw "Expected one section: $Name" }
    return $found[0]
}
function Content($Section) {
    return (@($Section.lines | Where-Object { $_.Trim().Length }) -join "`n").Trim()
}
function Compile-Fragment([string]$Name,[string[]]$Defines) {
    $binary = Join-Path $assemblyDirectory "$Name.bin"
    $listing = Join-Path $assemblyDirectory "$Name.fxc.asm"
    $arguments = @('/nologo','/T','vs_5_0','/E','main','/O3',
        '/I',(Join-Path $root 'third_party\JiggleForge'),'/Fo',$binary,'/Fc',$listing)
    foreach ($define in $Defines) { $arguments += @('/D',$define) }
    $output = & $FxcPath @arguments (Join-Path $root 'shaders\field_vs.hlsl') 2>&1
    if ($LASTEXITCODE -ne 0) { throw "FXC failed: $output" }
    return Get-TouchFragment ([IO.File]::ReadAllText($listing))
}
$current = Compile-Fragment current @()
$previous = Compile-Fragment previous @('CAMERA_INDEX=81','OFFSET_INDEX=2')
$frame = Compile-Fragment frame @('SURFACE_FRAME=1')
$normal = Compile-Fragment normal @('SURFACE_FRAME=1','NORMAL_ONLY=1')
$legacy = @{}
foreach ($name in @('current','previous','frame','normal')) {
    $legacy[$name] = Get-TouchFragment ([IO.File]::ReadAllText((Join-Path $parentDirectory "$name.fxc.asm")))
}
$passes = @()
foreach ($pass in $parent.passes) {
    $hash = $pass.hash
    if ($hash -cnotmatch '^[0-9a-f]{16}$') { throw 'Unexpected native program hash.' }
    $original = [IO.File]::ReadAllText((Join-Path $parentDirectory "$hash.original.asm"))
    $legacyAssembly = New-NativeFamilyAssembly $original $pass.spec $hash `
        $legacy.current $legacy.previous $legacy.frame $legacy.normal
    $legacyRegex = New-ShaderRegexPatch $original $legacyAssembly $hash $pass.passId
    $legacySections = @(Read-OrderedIniSections $legacyRegex.text)
    # Match the exact published program and replacement, not merely the family label.
    foreach ($section in $legacySections) {
        if ((Content (Section $section.name)) -cne (Content $section)) {
            throw "Published shader provenance differs: $($section.name)"
        }
    }
    $patched = New-NativeFamilyAssembly $original $pass.spec $hash $current $previous $frame $normal
    $path = Join-Path $assemblyDirectory "$hash.patched.asm"
    [IO.File]::WriteAllText($path,$patched,$encoding)
    $binary = Invoke-ShaderAssembler $AssemblerPath $path
    $validation = Test-ShaderAssemblyRoundtrip $AssemblerPath $path $binary
    foreach ($kind in @('Input','Output')) {
        if ((@(Get-ShaderSignature $original $kind) | ConvertTo-Json -Compress) -cne
            (@(Get-ShaderSignature $patched $kind) | ConvertTo-Json -Compress)) {
            throw "Native $kind signature changed: $hash"
        }
    }
    $replacement = New-ShaderRegexPatch $original $patched $hash $pass.passId
    $replacementSections = @(Read-OrderedIniSections $replacement.text)
    foreach ($section in $replacementSections) {
        $existing = Section $section.name
        if ($section.name.EndsWith('.Pattern') -or $section.name.EndsWith('.InsertDeclarations')) {
            if ((Content $existing) -cne (Content $section)) { throw 'Native matching contract changed.' }
        } elseif ($section.name.EndsWith('.Pattern.Replace')) {
            $existing.lines = $section.lines
        } else {
            $tempLines = @(0..($existing.lines.Count - 1) | Where-Object { $existing.lines[$_] -match '^temps = ' })
            $newTemps = @($section.lines | Where-Object { $_ -match '^temps = ' })
            if ($tempLines.Count -ne 1 -or $newTemps.Count -ne 1) { throw 'Unexpected temp declaration.' }
            $existing.lines[$tempLines[0]] = $newTemps[0]
        }
    }
    $probe = $null
    if ($pass.role -ceq 'color') {
        $probePath = Join-Path $assemblyDirectory "$hash.probe.asm"
        [IO.File]::WriteAllText($probePath,(New-CharacterPickAssembly $patched $pass.spec),$encoding)
        $probe = Invoke-ShaderAssembler $AssemblerPath $probePath
        $null = Test-ShaderAssemblyRoundtrip $AssemblerPath $probePath $probe
    }
    $passes += [ordered]@{
        hash=$hash; passId=$pass.passId; role=$pass.role; binary=$binary; probeBinary=$probe
        binarySha256=(Get-FileHash -LiteralPath $binary).Hash
        probeSha256=$(if ($probe) { (Get-FileHash -LiteralPath $probe).Hash } else { $null })
        originalBinary=$pass.originalBinary; validation=$validation
        publishedProgramVerified=$true; nativeSignaturesPreserved=$true
    }
}
if ($passes.Count -ne 12) { throw 'Expected twelve reviewed native programs.' }
$updated = "namespace = EndfieldJiggleEFMI`n`n" +
    (@($sections | ForEach-Object { Format-OrderedIniSection $_ }) -join "`n")
Assert-NativeOutfitBridge ([IO.File]::ReadAllText((Join-Path $runtime 'EndfieldJiggle.ini'))) `
    $updated ([IO.File]::ReadAllText((Join-Path $runtime 'Outfits.ini')))
[IO.File]::WriteAllText($passesPath,$updated,$encoding)
foreach ($relative in $originalFiles.Keys) {
    if ($relative -ceq 'Mods/EndfieldJiggleEFMI/Passes.ini') { continue }
    if ((Get-FileHash -LiteralPath (Join-Path $product $relative)).Hash -cne $originalFiles[$relative]) {
        throw "Unrelated payload changed: $relative"
    }
}
foreach ($picker in $parent.pickPrograms) {
    $source = Join-Path $runtime "shaders\$($picker.sourceName).hlsl"
    $cache = Join-Path $runtime "shaders\$($picker.sourceName).vs_5_0.8000.bin"
    if ([IO.File]::GetLastWriteTimeUtc($source) -ne [IO.File]::GetLastWriteTimeUtc($cache)) {
        throw 'Picker source/cache timestamp mismatch in candidate.'
    }
}
Copy-Item -LiteralPath (Join-Path $root 'docs\SHADING-MAINTENANCE.md') `
    -Destination (Join-Path $product 'SHADING-MAINTENANCE.md')
$candidateZip = Join-Path $build 'EndfieldJiggleEFMI-v0.2.2-shading-candidate-win64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $build 'package'),$candidateZip,
    [IO.Compression.CompressionLevel]::Optimal,$false)
$files = @(Get-ChildItem -LiteralPath $runtime -File -Recurse | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($runtime,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
})
$reportPath = Join-Path $build 'build-report.json'
[ordered]@{
    schema=1; buildKind='shading-numerics-candidate'; packageDirectory=$runtime
    baseModZipSha256=$baseHash; parentBuildReportSha256=$parentHash
    fieldSourceSha256=(Get-FileHash -LiteralPath (Join-Path $root 'shaders\field_vs.hlsl')).Hash
    passes=$passes; files=$files; zipPath=$candidateZip; zipSha256=(Get-FileHash -LiteralPath $candidateZip).Hash
    runtimeChangedFiles=@('Passes.ini'); originalShaderPatternsPreserved=$true
    pickerBytesAndTimesPreserved=$true; defaultEnabled=$false
    liveInstallationModified=$false; inGameVerified=$false; publicationApproved=$false
    limitations=@('Historical native interfaces only; no new-version frame evidence.',
        'Local numerical fixes are not confirmation that the reported black-skin artifact is resolved.')
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "CANDIDATE_REPORT=$reportPath"
Write-Output "CANDIDATE_ZIP=$candidateZip"
