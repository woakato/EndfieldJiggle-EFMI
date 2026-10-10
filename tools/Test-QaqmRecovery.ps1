[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'QaqmState.Common.psm1') -Force
$fixture = Join-Path $root ('reports\qaqm-unit\' + [guid]::NewGuid().ToString('N'))
$mod = Join-Path $fixture 'Mods\Example'
$cache = Join-Path $fixture 'cache'
New-Item -ItemType Directory -Path $mod,$cache -Force | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
$source = @'
namespace = Example
[Constants]
; Persisted state is hosted by qaqm_state_0123456789ab.ini
[Present]
if $\QAQM\Persist\Bridge_0123456789ab\variant == 1
    $\QAQM\Persist\Bridge_0123456789ab\variant = 0
endif
'@
$backup = @'
namespace = Example
[Constants]
global persist $variant = 1
global persist $strength = -0.25
'@
$iniPath = Join-Path $mod 'example.ini'
$backupPath = $iniPath + '.qaqm-persistbak'
[IO.File]::WriteAllText($iniPath,$source,$encoding)
[IO.File]::WriteAllText($backupPath,$backup,$encoding)
$before = (Get-FileHash $iniPath).Hash
$plan = @(Get-QaqmStateRecoveryPlan $mod)
if ($plan.Count -ne 1 -or $plan[0].text -notmatch 'global persist \$variant = 1' -or
    $plan[0].text -notmatch 'global persist \$strength = -0.25') { throw 'Recovery did not retain evidenced defaults.' }
if ((Get-FileHash $iniPath).Hash -cne $before) { throw 'Recovery planning changed the source.' }
$checks = 2
function RejectPlan([string]$Mod,[string]$Efmi = '') {
    $rejected=$false
    try { Get-QaqmStateRecoveryPlan -SourceDirectory $Mod -EfmiPath $Efmi | Out-Null } catch { $rejected=$true }
    if (!$rejected) { throw 'An unsupported state recovery escaped rejection.' }
}
[IO.File]::WriteAllText($iniPath, $source.Replace('\variant','\unknown'), $encoding)
RejectPlan $mod
[IO.File]::WriteAllText($iniPath, $source, $encoding)
[IO.File]::WriteAllText($backupPath, $backup.Replace('global persist $variant = 1','global persist $variant = run = injected'), $encoding)
RejectPlan $mod
[IO.File]::WriteAllText($backupPath, $backup, $encoding)
$checks += 2
$hostText = $plan[0].text
[IO.File]::WriteAllText((Join-Path $cache 'qaqm_state_0123456789ab.ini'),$hostText,$encoding)
[IO.File]::WriteAllText((Join-Path $fixture 'd3dx.ini'),"[Include]`ninclude = active.ini`n",$encoding)
[IO.File]::WriteAllText((Join-Path $fixture 'active.ini'),"[IncludeState]`ninclude = cache\qaqm_state_0123456789ab.ini`n",$encoding)
if (@(Get-QaqmStateRecoveryPlan -SourceDirectory $mod -EfmiPath $fixture).Count -ne 0) {
    throw 'An explicitly loaded external host would be duplicated.'
}
$checks++
[IO.File]::WriteAllText((Join-Path $fixture 'active.ini'),"[IncludeState]`n; deliberately not loaded`n",$encoding)
if (@(Get-QaqmStateRecoveryPlan -SourceDirectory $mod -EfmiPath $fixture).Count -ne 1) {
    throw 'A cache file that is not included must not satisfy a declaration dependency.'
}
$checks++
[IO.File]::WriteAllText((Join-Path $mod 'qaqm_state_0123456789ab.ini'),$hostText,$encoding)
if (@(Get-QaqmStateRecoveryPlan $mod).Count -ne 0) { throw 'Recovery is not idempotent with a local host.' }
[IO.File]::WriteAllText((Join-Path $mod 'qaqm_state_0123456789ab.ini'),$hostText.Replace('namespace = QAQM','namespace = Wrong'),$encoding)
RejectPlan $mod
$checks += 2
Write-Output "PASS: $checks QAQM recovery checks. Explicitly loaded hosts are retained; unincluded caches are not mistaken for active state."
