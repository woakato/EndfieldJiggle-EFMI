[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path $OutputDirectory) { throw 'Preserve existing fixture evidence.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$fixture = Join-Path $OutputDirectory ("fixture " + [char]0x6e38 + [char]0x620f + ' (1)')
New-Item -ItemType Directory -Path $fixture | Out-Null
Get-ChildItem -LiteralPath $PackageDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $fixture -Recurse
}
# Exercise the working collector, not a stale copy in an earlier package.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Collect-Diagnostics.ps1') `
    -Destination (Join-Path $fixture 'Collect-Diagnostics.ps1') -Force
$ps = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Value, [string]$Name) {
    if (!$Value) { throw "FAIL: $Name" }
    $checks.Add($Name)
    Write-Output "PASS: $Name"
}
function Collect([string]$Name) {
    $out = Join-Path $OutputDirectory $Name
    & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Collect-Diagnostics.ps1') `
        -PackageDirectory $fixture -OutputDirectory $out
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell 5.1 collector failed.' }
    return $out
}
$shaderDir = Join-Path $fixture 'EFMI\Mods\EndfieldJiggleEFMI\shaders'
$corrupt = Join-Path $shaderDir 'pick_c6e55aaa8f4b3218.vs_5_0.8000.bin'
$bytes = [IO.File]::ReadAllBytes($corrupt)
$bytes[100] = $bytes[100] -bxor 1
[IO.File]::WriteAllBytes($corrupt, $bytes)
$mismatch = Join-Path $shaderDir 'pick_1479b2b594b9c91a.vs_5_0.8000.bin'
[IO.File]::SetLastWriteTimeUtc($mismatch, [IO.File]::GetLastWriteTimeUtc(
    (Join-Path $shaderDir 'pick_1479b2b594b9c91a.hlsl')).AddTicks(7))
$missing = Join-Path $shaderDir 'input_controller.hlsl'
Remove-Item -LiteralPath $missing
$gameDir = Join-Path $OutputDirectory ('game ' + [char]0x6e38 + [char]0x620f)
New-Item -ItemType Directory -Path $gameDir | Out-Null
$settings = @{ schema = 1; gameExecutable = (Join-Path $gameDir 'Endfield.exe') } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $fixture 'launch-settings.json'), $settings, [Text.UTF8Encoding]::new($false))
$log = Join-Path $gameDir 'd3d11_log.txt'
$head = "ERROR EARLY_PICKER_FAILURE $gameDir\pick_1479.hlsl UID=123456789`n" +
    (("input shift held $gameDir\Endfield.exe`n") * 1200)
$tail = "`nERROR LATE_PICKER_FAILURE`n"
[IO.File]::WriteAllText($log, $head, [Text.UTF8Encoding]::new($false))
& fsutil.exe sparse setflag $log | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Sparse test file setup failed.' }
$stream = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $stream.SetLength(10GB)
    $stream.Position = $stream.Length - 4096
    $tailBytes = [Text.Encoding]::UTF8.GetBytes($tail)
    $stream.Write($tailBytes, 0, $tailBytes.Length)
} finally { $stream.Dispose() }
$manifestPath = Join-Path $fixture 'standalone-manifest.json'
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
$manifest | Add-Member -NotePropertyName launchWorkflow -NotePropertyValue @{
    mode = 'manual-official-launcher'
} -Force
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
$launcherDirectory = Join-Path $fixture 'Logs'
New-Item -ItemType Directory -Path $launcherDirectory -Force | Out-Null
$launcherLog = Join-Path $launcherDirectory 'launcher.log'
$previousLog = Join-Path $launcherDirectory 'launcher.log.previous'
function Write-SparseLauncherLog([string]$Path, [long]$Length, [string]$Head, [string]$Tail) {
    [IO.File]::WriteAllText($Path, $Head, [Text.UTF8Encoding]::new($false))
    & fsutil.exe sparse setflag $Path | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Sparse launcher log setup failed.' }
    $file = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $file.SetLength($Length)
        $end = [Text.Encoding]::UTF8.GetBytes("`n" + $Tail + "`n")
        $file.Position = $Length - $end.Length
        $file.Write($end, 0, $end.Length)
    } finally { $file.Dispose() }
}
$phaseNames = @('waiting-for-official-launch', 'game-detected', 'loading-efmi', 'efmi-loaded',
    'cancelled', 'timeout', 'failed')
$phaseTail = ($phaseNames | ForEach-Object {
    "phase=$_ reason=timeout UID=123456789 path=$gameDir\Endfield.exe"
}) -join "`n"
$phaseTail += "`nphase=failed detail=" + ('x' * 3000)
Write-SparseLauncherLog $launcherLog 32MB (("phase=waiting-for-official-launch`n") * 120) $phaseTail
Write-SparseLauncherLog $previousLog 48MB "phase=failed EARLY_PREVIOUS_FAILURE`n" `
    "phase=cancelled PREVIOUS_FINAL_EVENT account_id=987654321 path=$fixture\EndfieldJiggle.exe"
$beforeLauncherTime = [IO.File]::GetLastWriteTimeUtc($launcherLog)
$beforePreviousTime = [IO.File]::GetLastWriteTimeUtc($previousLog)
$beforeManifestHash = (Get-FileHash -LiteralPath $manifestPath).Hash
$beforeTime = [IO.File]::GetLastWriteTimeUtc($log)
$beforeCacheTime = [IO.File]::GetLastWriteTimeUtc($mismatch)
$beforeHash = (Get-FileHash $corrupt).Hash
$messages = @(Collect 'faults-and-large-log')
$out = $messages[-1]
$packageReport = [IO.File]::ReadAllText((Join-Path $out 'package-files.json')) | ConvertFrom-Json
Check (@($packageReport.requiredFiles | Where-Object {
    $_.path -like '*input_controller.hlsl' -and $_.status -eq 'missing'
}).Count -eq 1) 'Missing dependency is reported, not repaired.'
Check (@($packageReport.requiredFiles | Where-Object {
    $_.path -like '*pick_c6*.bin' -and $_.status -eq 'hash-mismatch'
}).Count -eq 1) 'Same-size corrupt picker binary fails checksum comparison.'
Check (@($packageReport.shaderCachePairs | Where-Object {
    $_.source -like '*pick_1479*' -and !$_.sameFileTime
}).Count -eq 1) 'Seven-tick FILETIME mismatch is detected.'
$logReport = [IO.File]::ReadAllText((Join-Path $out 'log-summary.json')) | ConvertFrom-Json
$entry = @($logReport.logs | Where-Object label -EQ 'game-directory/d3d11_log.txt')[0]
Check ($entry.headBytes -eq 16MB -and $entry.tailBytes -eq 4MB -and
    $entry.omittedMiddleBytes -eq (10GB - 20MB)) '10 GiB sparse log reads only bounded head/tail windows.'
$errorText = $entry.errorLines -join "`n"
Check ($errorText.Contains('EARLY_PICKER_FAILURE') -and $errorText.Contains('LATE_PICKER_FAILURE')) `
    'Startup and final errors survive repetitive input records.'
$launch = [IO.File]::ReadAllText((Join-Path $out 'launch-summary.json')) | ConvertFrom-Json
Check ($launch.mode -eq 'manual-official-launcher' -and !$launch.manifestError) `
    'Launch workflow mode is read from the standalone manifest.'
$current = @($launch.logs | Where-Object label -EQ 'package/Logs/launcher.log')[0]
$previous = @($launch.logs | Where-Object label -EQ 'package/Logs/launcher.log.previous')[0]
Check ($current.phaseEvents.Count -eq 100 -and $current.sampledPhaseEventCount -eq 128 -and
    @($current.phaseEvents | Where-Object { $_.Length -gt 2000 }).Count -eq 0) `
    'Recent launcher phase events are bounded to 100 strings of at most 2000 characters.'
foreach ($phase in $phaseNames) {
    Check (@($current.phaseEvents | Where-Object { $_ -like "phase=$phase *" }).Count -ge 1) `
        "Launcher phase $phase is distinguishable in the summary."
}
Check ($launch.latestPhase -eq 'failed' -and $previous.latestPhase -eq 'cancelled' -and
    ($previous.phaseEvents -join "`n").Contains('PREVIOUS_FINAL_EVENT') -and
    !($current.phaseEvents -join "`n").Contains('PREVIOUS_FINAL_EVENT')) `
    'Current latest phase is not overwritten by a rotated log from another attempt.'
foreach ($label in @('package/Logs/launcher.log', 'package/Logs/launcher.log.previous')) {
    $sampledLauncher = @($logReport.logs | Where-Object label -EQ $label)[0]
    Check ($sampledLauncher.headBytes -eq 16MB -and $sampledLauncher.tailBytes -eq 4MB -and
        $sampledLauncher.omittedMiddleBytes -eq ($sampledLauncher.bytes - 20MB)) `
        "$label uses bounded head/tail sampling without a full copy."
}
Check ($launch.gameApiDx11 -eq 'unverified' -and ($launch.warnings -join "`n") -match 'does not prove touch success' -and
    ($launch.warnings -join "`n") -match 'DX11 is unverified') `
    'Launcher success and the native probe do not claim touch success or a verified game DX11 API.'
$allText = (Get-ChildItem $out -File | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
Check (!$allText.Contains($gameDir) -and !$allText.Contains($fixture) -and
    !$allText.Contains('123456789') -and !$allText.Contains('987654321') -and !$allText.Contains($env:USERPROFILE)) `
    'Known personal paths and test UID are redacted.'
Check ((Get-Item ($out + '.zip')).Length -lt 2MB) 'Large raw log is not copied into the exported ZIP.'
Check ((Get-Item $log).Length -eq 10GB -and [IO.File]::GetLastWriteTimeUtc($log) -eq $beforeTime -and
    [IO.File]::GetLastWriteTimeUtc($mismatch) -eq $beforeCacheTime -and
    (Get-FileHash $corrupt).Hash -eq $beforeHash) 'Collection preserves raw log, corrupt bytes and cache timestamps.'
Check ([IO.File]::GetLastWriteTimeUtc($launcherLog) -eq $beforeLauncherTime -and
    [IO.File]::GetLastWriteTimeUtc($previousLog) -eq $beforePreviousTime -and
    (Get-Item -LiteralPath $launcherLog).Length -eq 32MB -and
    (Get-Item -LiteralPath $previousLog).Length -eq 48MB -and
    (Get-FileHash -LiteralPath $manifestPath).Hash -eq $beforeManifestHash) `
    'Collection preserves launcher logs and launch workflow metadata.'

$manifest.PSObject.Properties.Remove('launchWorkflow')
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 16))
$lock = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try { $messages = @(Collect 'locked-log'); $lockedOut = $messages[-1] }
finally { $lock.Dispose() }
$locked = [IO.File]::ReadAllText((Join-Path $lockedOut 'log-summary.json')) | ConvertFrom-Json
$lockedEntry = @($locked.logs | Where-Object label -EQ 'game-directory/d3d11_log.txt')[0]
Check ([bool]$lockedEntry.error) 'Locked log produces a report error instead of aborting all collection.'
$legacyLaunch = [IO.File]::ReadAllText((Join-Path $lockedOut 'launch-summary.json')) | ConvertFrom-Json
Check ($legacyLaunch.mode -eq 'unknown' -and !$legacyLaunch.manifestError) `
    'Older manifests without launchWorkflow retain unknown launch mode.'
[IO.File]::WriteAllText((Join-Path $fixture 'diagnostic-checksums.json'), '{"schema":99}')
[IO.File]::WriteAllText($manifestPath, '{invalid manifest')
$lock = [IO.File]::Open($previousLog, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try { $messages = @(Collect 'invalid-baseline') }
finally { $lock.Dispose() }
$invalid = [IO.File]::ReadAllText((Join-Path $messages[-1] 'package-files.json')) | ConvertFrom-Json
Check ([bool]$invalid.baselineError) 'Invalid checksum baseline cannot be treated as an integrity pass.'
$invalidLaunch = [IO.File]::ReadAllText((Join-Path $messages[-1] 'launch-summary.json')) | ConvertFrom-Json
Check ($invalidLaunch.mode -eq 'unknown' -and [bool]$invalidLaunch.manifestError -and
    [bool](@($invalidLaunch.logs | Where-Object label -EQ 'package/Logs/launcher.log.previous')[0].error)) `
    'Malformed launch metadata and a locked rotated log are reported without aborting collection.'
Remove-Item -LiteralPath $manifestPath, $launcherLog, $previousLog
$messages = @(Collect 'missing-launch-data')
$missingLaunch = [IO.File]::ReadAllText((Join-Path $messages[-1] 'launch-summary.json')) | ConvertFrom-Json
Check ($missingLaunch.mode -eq 'unknown' -and $missingLaunch.latestPhase -eq 'unknown' -and
    @($missingLaunch.logs | Where-Object exists).Count -eq 0) `
    'Missing manifest and launcher logs remain unknown, not successful.'
[IO.File]::WriteAllText($manifestPath, (' ' * (1MB + 1)))
$messages = @(Collect 'oversized-manifest')
$oversizedLaunch = [IO.File]::ReadAllText((Join-Path $messages[-1] 'launch-summary.json')) | ConvertFrom-Json
Check ($oversizedLaunch.mode -eq 'unknown' -and [bool]$oversizedLaunch.manifestError) `
    'Oversized standalone manifests are rejected by the bounded metadata reader.'
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'test-report.json'),
    (@{ checks = $checks.ToArray(); gameStarted = $false; liveInstallationModified = $false } |
        ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
