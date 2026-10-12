[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$DropInZipPath,
    [Parameter(Mandatory)][string]$DotnetPath,
    [string]$LoaderHostPath,
    [string]$OriginalShaderDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LoaderDiagnostics.psm1') -Force
$product = Assert-UniversalPath (Join-Path $root 'reports\standalone') $PackageDirectory
$output = Join-Path $root ('reports\standalone\test-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $DotnetPath run --project (Join-Path $root 'tests\EndfieldJiggle.Launcher.Tests\EndfieldJiggle.Launcher.Tests.csproj') `
    --configuration Release -- $output $product
if ($LASTEXITCODE -ne 0) { throw 'Standalone launcher tests failed.' }
$runtime = Join-Path $product 'EFMI\Mods\EndfieldJiggleEFMI'
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $DropInZipPath).Path)
$compared = 0
try {
    foreach ($entry in $archive.Entries) {
        $prefix = 'EndfieldJiggleEFMI/'
        if (!$entry.FullName.StartsWith($prefix,[StringComparison]::Ordinal) -or $entry.FullName.EndsWith('/')) { continue }
        $relative = $entry.FullName.Substring($prefix.Length)
        $path = Assert-UniversalPath $runtime (Join-Path $runtime $relative)
        $stream = $entry.Open()
        try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -cne $expected) {
            throw "Standalone changed a published Mod/configurator file: $relative"
        }
        $compared++
    }
} finally { $archive.Dispose() }
if ($compared -lt 15) { throw 'Incomplete Mod/configurator comparison.' }
$loaderResult = $null
if ($LoaderHostPath -or $OriginalShaderDirectory) {
    if (!$LoaderHostPath -or !$OriginalShaderDirectory) { throw 'Supply both offline loader host and original shader directory.' }
    $hostSource = (Resolve-Path -LiteralPath $LoaderHostPath).Path
    if ([IO.Path]::GetFileName($hostSource) -cne 'loader_host.exe') { throw 'Use the existing isolated D3D11 loader host.' }
    $fixture = Join-Path $output 'loader-fixture'
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $product 'EFMI') -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $fixture -Recurse
    }
    Copy-Item -LiteralPath $hostSource -Destination (Join-Path $fixture 'loader_host.exe')
    $config = @'
[Include]
include = Core\EFMI\main.ini
include_recursive = Mods
exclude_recursive = DISABLED*
exclude_recursive = desktop.ini
[Logging]
log_level = debug
calls = 1
debug = 1
unbuffered = 1
[ClearRenderTargetView]
[ClearDepthStencilView]
[ClearUnorderedAccessViewUint]
[ClearUnorderedAccessViewFloat]
[System]
skip_early_includes_load = 0
allow_platform_update = 1
allow_check_interface = 1
[Rendering]
override_directory = ShaderFixes
cache_directory = ShaderCache
storage_directory = ShaderFromGame
shader_hash = 3dmigoto
texture_hash = 0
track_region_hashes = 1
cache_shaders = 0
ini_params = 120
assemble_signature_comments = 1
disassemble_undecipherable_custom_data = 1
patch_assembly_cb_offsets = 1
recursive_include = 1
export_shaders = 0
[Hunting]
hunting = 0
[Stereo]
automatic_mode = 0
'@
    [IO.File]::WriteAllText((Join-Path $fixture 'd3dx.ini'), $config, [Text.UTF8Encoding]::new($false))
    $shaders = @(Get-ChildItem -LiteralPath (Resolve-Path -LiteralPath $OriginalShaderDirectory).Path -Filter '*.shdr' -File)
    if ($shaders.Count -ne 12) { throw 'Offline integration expects the twelve native-interface shader fixtures.' }
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $fixture 'loader_host.exe'))
    $start.WorkingDirectory = $fixture
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add((Join-Path $fixture 'd3d11.dll'))
    foreach ($shader in $shaders) { $start.ArgumentList.Add($shader.FullName) }
    $hostProcess = [Diagnostics.Process]::Start($start)
    $stdout = $hostProcess.StandardOutput.ReadToEndAsync()
    $stderr = $hostProcess.StandardError.ReadToEndAsync()
    if (!$hostProcess.WaitForExit(60000)) {
        $hostProcess.Kill()
        $hostProcess.WaitForExit()
        throw 'Offline standalone loader host timed out.'
    }
    $hostText = $stdout.Result + $stderr.Result
    [IO.File]::WriteAllText((Join-Path $output 'loader-host.txt'),$hostText)
    if ($hostProcess.ExitCode -ne 0) { throw "Offline standalone loader failed: $hostText" }
    $logPath = Join-Path $fixture 'd3d11_log.txt'
    $log = [IO.File]::ReadAllText($logPath)
    $diagnostics = @(Get-LoaderDiagnostics -Log $log)
    if ($diagnostics.Count) { throw "Offline loader diagnostics: $($diagnostics -join "`n")" }
    $restores = ([regex]::Matches($hostText,'srv-restored')).Count
    if ($restores -ne 12) { throw "Expected twelve foreign resource binding restores, got $restores." }
    $loaderResult = [ordered]@{
        exitCode=$hostProcess.ExitCode; shaderFixtures=$shaders.Count; resourceBindingRestores=$restores
        diagnostics=$diagnostics; logPath=$logPath
        testConfiguration='Isolated immediate-includes configuration; not a production game startup'
    }
}
$report = [ordered]@{
    schema=1; comparedPublishedFiles=$compared; publishedModAndConfiguratorUnchanged=$true
    offlineLoader=$loaderResult; gameStarted=$false; liveInstallationModified=$false
}
[IO.File]::WriteAllText((Join-Path $output 'test-report.json'),
    ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Standalone package checks passed; $compared published files unchanged. No game was started."
Write-Output "STANDALONE_TEST_REPORT=$(Join-Path $output 'test-report.json')"
