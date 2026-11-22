[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LauncherZip,
    [Parameter(Mandatory)][string]$EfmiZip,
    [Parameter(Mandatory)][string]$LibrariesZip,
    [Parameter(Mandatory)][string]$LibrariesManifest,
    [Parameter(Mandatory)][string]$DropInZip,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedDropInSha256,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^v\d+\.\d+\.\d+[a-z]?$')][string]$Version = 'v0.2.7c'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path "$PSScriptRoot/..").Path
Import-Module "$PSScriptRoot/Universal.Common.psm1" -Force
$sums = [ordered]@{}
$inputs = @(
    @($LauncherZip, 'B519F223C8FA9493ECFAE0661524D281D8086041D4413FCEF0223DFB80D30493'),
    @($EfmiZip, '10B350C620054A3A3D46D704789ABBDD83EF43AFDFD3B70932A036236E426C0A'),
    @($LibrariesZip, '168561008D456E32F3B0CC4A2505DBD68AD6E2BE0019CD40FF79C8E9769B37AF'),
    @($LibrariesManifest, '8B702C83475BF70B5760629B3C334F727DB065B19D2E4979D43E0ED9684D93FA'),
    @($DropInZip, $ExpectedDropInSha256.ToUpperInvariant())
)
foreach ($pair in $inputs) {
    $hash = (Get-FileHash -LiteralPath $pair[0]).Hash
    if ($hash -cne $pair[1]) { throw "Unexpected input: $($pair[0])" }
    $sums[[IO.Path]::GetFileName($pair[0])] = $hash.ToLowerInvariant()
}
$work = Assert-UniversalPath (Join-Path $root 'reports') ([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $work) { throw 'Preserve existing builds.' }
$product = Join-Path $work 'XXMI-EndfieldJiggle'
New-Item -ItemType Directory -Path $product | Out-Null
$expected = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
function Expand-Verified([string]$zip, [string]$prefix, [switch]$Launcher) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $zip).Path)
    try {
        $sourceRoot = ''
        if ($Launcher) {
            $suffix = 'Resources/Bin/XXMI Launcher.exe'
            $exe = @($archive.Entries | Where-Object { $_.FullName.Replace('\','/').EndsWith($suffix) })
            if ($exe.Count -ne 1) { throw 'Missing unique official launcher executable' }
            $sourceRoot = $exe[0].FullName.Substring(0,$exe[0].FullName.Length-$suffix.Length)
        }
        foreach ($e in $archive.Entries) {
            $name = $e.FullName.Replace('\','/')
            if (!$name.StartsWith($sourceRoot,[StringComparison]::Ordinal)) { throw "Unknown official package root: $name" }
            $name = $name.Substring($sourceRoot.Length)
            if (!$name) { continue }
            $directory = $name.EndsWith('/')
            if ($directory) { $name = $name.TrimEnd('/') }
            if ($name -match ':|(^|/)(\.{0,2})(/|$)') { throw "Unexpected path: $name" }
            $dest = $prefix + $name
            $path = Join-Path $product $dest
            if ($directory) {
                New-Item -ItemType Directory -Path $path -Force | Out-Null
                continue
            }
            $stream = $e.Open()
            try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($expected.ContainsKey($dest)) {
                if ($expected[$dest] -cne $digest) { throw "Official files conflict: $dest" }
                continue
            }
            $expected.Add($dest,$digest)
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e,$path,$false)
        }
    } finally { $archive.Dispose() }
}
Expand-Verified $inputs[0][0] '' -Launcher
Expand-Verified $inputs[1][0] 'EFMI/'
Expand-Verified $inputs[2][0] 'Resources/Packages/XXMI/'
# The two renderer DLLs are the normal EFMI deployment. InputGuard also locates the official loader here.
Expand-Verified $inputs[2][0] 'EFMI/'
Expand-Verified $inputs[4][0] 'EFMI/Mods/EndfieldJiggleEFMI/'
function Copy-Checked([string]$source,[string]$name) {
    $target = Join-Path $product $name
    $digest = (Get-FileHash -LiteralPath $source).Hash
    if ($expected.ContainsKey($name)) {
        if ($expected[$name] -cne $digest) { throw "Conflicting file: $name" }
        return
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    $expected.Add($name,$digest)
}
Copy-Checked $inputs[3][0] 'Resources/Packages/XXMI/Manifest.json'
$noticeDir = New-Item -ItemType Directory -Path (Join-Path $work 'notices')
foreach ($notice in @(
    @('XXMI-Launcher','4f5ef31cc237394568119f9feac2d99f44665466','LICENSE','XXMI-Launcher-LICENSE.txt'),
    @('EFMI-Package','45e0ca07af9db81b0907c537c413d281db4f235b','LICENSE','EFMI-LICENSE.txt'),
    @('XXMI-Libs-Package','8bd31f74727f033b3d9049509e19da3563c8b98c','LICENSE.GPL.txt','XXMI-LIBS-LICENSE.GPL.txt'),
    @('XXMI-Libs-Package','8bd31f74727f033b3d9049509e19da3563c8b98c','COPYING.txt','XXMI-LIBS-COPYING.txt'),
    @('XXMI-Libs-Package','8bd31f74727f033b3d9049509e19da3563c8b98c','DirectXTK/LICENSE','DirectXTK-LICENSE.txt'),
    @('XXMI-Libs-Package','8bd31f74727f033b3d9049509e19da3563c8b98c','pcre2-10.30/LICENCE','PCRE2-LICENSE.txt')
)) {
    $response = & gh api "repos/SpectrumQT/$($notice[0])/contents/$($notice[2])?ref=$($notice[1])"
    if ($LASTEXITCODE -ne 0) { throw "Missing upstream notice: $($notice[3])" }
    $target = Join-Path $noticeDir $notice[3]
    [IO.File]::WriteAllBytes($target,[Convert]::FromBase64String(($response | ConvertFrom-Json).content))
    Copy-Checked $target "Licenses/$($notice[3])"
}
$sourceArchive = Join-Path $work "EndfieldJiggleEFMI-$Version-source.zip"
$paths = @(& git -C $root ls-files)
if ($LASTEXITCODE -ne 0 -or !$paths.Count) { throw 'Cannot enumerate public source.' }
$source = [IO.Compression.ZipFile]::Open($sourceArchive,[IO.Compression.ZipArchiveMode]::Create)
try {
    # Include the renamed historical builder in pre-commit working-tree builds as well.
    foreach ($relative in @(($paths + 'tools/Build-ManualStandalone.ps1') | Sort-Object -Unique)) {
        if ($relative -match '(^|/)(reports|dist|build|obj|bin|\.git)/|\.(zip|dll|exe|bin|pdb|shdr)$|(^|/)(\.env|AGENTS\.md)') { throw "Unexpected source: $relative" }
        $path = Assert-UniversalPath $root (Join-Path $root $relative)
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($source,$path,$relative) | Out-Null
    }
} finally { $source.Dispose() }
Copy-Checked $sourceArchive "Sources/EndfieldJiggleEFMI-$Version-source.zip"
$startCmd = Join-Path $work 'Start.cmd'
[IO.File]::WriteAllText($startCmd,"@echo off`r`ncd /d `"%~dp0Resources\Bin`" || exit /b 1`r`nstart `"`" `"XXMI Launcher.exe`"`r`n",[Text.Encoding]::ASCII)
Copy-Checked $startCmd 'Start.cmd'
Copy-Checked "$root/docs/package/README-Standalone-zh-CN.txt" 'README.txt'
Copy-Checked "$root/THIRD-PARTY-NOTICES.md" 'Licenses/Standalone-Notices.txt'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $product 'Resources/Packages/XXMI/Manifest.json') | ConvertFrom-Json
if ($manifest.version -cne '1.3.3' -or $manifest.signatures.PSObject.Properties.Name.Count -ne 3) { throw 'XXMI signature manifest mismatch' }
$basename = "EndfieldJiggleEFMI-$Version-standalone-win64.zip"
$zip = Join-Path $work $basename
[IO.Compression.ZipFile]::CreateFromDirectory($product,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries | Where-Object { !$_.FullName.EndsWith('/') }) {
        $name = $entry.FullName.Substring('XXMI-EndfieldJiggle/'.Length)
        if (!$seen.Add($name) -or !$expected.ContainsKey($name)) { throw "Unexpected output: $name" }
        $s = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)) } finally { $s.Dispose() }
        if ($hash -cne $expected[$name]) { throw "Output bytes differ: $name" }
        if ($name -match '(^|/)(d3d11_log\.txt|launch-settings\.json|EndfieldJiggleLauncher\.exe|Endfield\.exe|d3dx_user\.ini)$') { throw "Unexpected private state or executable: $name" }
    }
    if ($seen.Count -ne $expected.Count) { throw 'Incomplete portable archive' }
    foreach ($name in @('Start.cmd','Resources/Bin/XXMI Launcher.exe','EFMI/d3dx.ini',
        'EFMI/Core/EFMI/main.ini','EFMI/Mods/EndfieldJiggleEFMI/Configurator/EndfieldJiggleConfigurator.exe',
        'EFMI/Mods/EndfieldJiggleEFMI/OutfitPreparation.ini','EFMI/Mods/EndfieldJiggleEFMI/Outfits.ini')) {
        if (!$seen.Contains($name)) { throw "Missing required entry: $name" }
    }
    $modShaders = 'XXMI-EndfieldJiggle/EFMI/Mods/EndfieldJiggleEFMI/shaders/'
    foreach ($shader in @('step_cs','character_pick_ps','pick_1479b2b594b9c91a','pick_c6e55aaa8f4b3218')) {
        $hlsl = $archive.GetEntry("$modShaders$shader.hlsl")
        $cache = @($archive.Entries | Where-Object { $_.FullName -like "$modShaders$shader.*.bin" })
        if (!$hlsl -or $cache.Count -ne 1 -or $hlsl.LastWriteTime -ne $cache[0].LastWriteTime) {
            throw "Shader/cache timestamp mismatch: $shader"
        }
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
[ordered]@{ archive=$zip; sha256=$hash; verifiedFiles=$expected.Count; inputs=$sums; liveInstallationModified=$false; gameStarted=$false; officialLauncherStarted=$false } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'build-report.json') -Encoding utf8
"PASS: $($seen.Count) archive entries verified. $zip"
