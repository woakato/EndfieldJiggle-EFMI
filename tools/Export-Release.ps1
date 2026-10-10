[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseBuildDirectory,
    [Parameter(Mandatory)][string]$ConfiguratorZipPath,
    [string]$QaqmRepairZipPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Preserve an existing export directory.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$inputs = @(
    (Join-Path $ReleaseBuildDirectory 'EndfieldJiggleEFMI-v0.2.1-win64.zip'),
    (Join-Path $ReleaseBuildDirectory 'EndfieldJiggleEFMI-v0.2.1-with-configurator-win64.zip'),
    $ConfiguratorZipPath
)
if ($QaqmRepairZipPath) { $inputs += $QaqmRepairZipPath }
foreach ($inputPath in $inputs) {
    $inputFile = (Resolve-Path -LiteralPath $inputPath).Path
    $target = Join-Path $output ([IO.Path]::GetFileName($inputFile))
    Copy-Item -LiteralPath $inputFile -Destination $target
    if ((Get-FileHash $inputFile).Hash -cne (Get-FileHash $target).Hash) { throw 'Export copy checksum mismatch.' }
}
$sourceZip = Join-Path $output 'EndfieldJiggleEFMI-v0.2.1-source.zip'
Push-Location $root
try {
    $paths = @(& git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate the current source tree.' }
} finally { Pop-Location }
$archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $paths) {
        if ($relative -match '^(reports|build|dist|\.git)/|\.(dll|exe|bin|shdr|pdb|zip)$|(^|/)\.env') {
            throw "Non-source material in export set: $relative"
        }
        $path = Join-Path $root $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Source entry is missing: $relative" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $path, 'EndfieldJiggleEFMI-v0.2.1-source/' + $relative,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Copy-Item -LiteralPath (Join-Path $root 'docs\V021-VALIDATION.md') -Destination (Join-Path $output 'VALIDATION-zh-CN.md')
Copy-Item -LiteralPath (Join-Path $root 'docs\INSTALL-zh-CN.md') -Destination (Join-Path $output 'INSTALL-zh-CN.md')
$files = @(Get-ChildItem -LiteralPath $output -File | ForEach-Object {
    [ordered]@{ file=$_.Name; bytes=$_.Length; sha256=(Get-FileHash $_.FullName).Hash }
})
$checksums = @($files | ForEach-Object { "$($_.sha256.ToLowerInvariant()) *$($_.file)" }) -join "`n"
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS-v0.2.1.txt'), $checksums + "`n", [Text.UTF8Encoding]::new($false))
[ordered]@{
    schema=1; version='v0.2.1'; created=(Get-Date).ToString('o'); files=$files
    sourceFiles=$paths.Count; releaseBuildDirectory=(Resolve-Path $ReleaseBuildDirectory).Path
    liveInstallationModified=$false; inGameVerified=$false; githubPublished=$false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'export-manifest.json') -Encoding utf8
Write-Output "EXPORT_DIRECTORY=$output"
