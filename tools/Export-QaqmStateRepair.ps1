[CmdletBinding()]
param([Parameter(Mandatory)][string]$ModPath, [string]$EfmiPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'QaqmState.Common.psm1') -Force
$source = (Resolve-Path -LiteralPath $ModPath).Path
$plan = @(Get-QaqmStateRecoveryPlan -SourceDirectory $source -EfmiPath $EfmiPath)
if (!$plan.Count) { throw 'No missing recoverable QAQM state hosts were found.' }
$output = Join-Path $root ('reports\qaqm-repair\v0.2.1-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Assert-UniversalPath $root $output | Out-Null
$package = Join-Path $output 'package\QAQM-State-Recovery-v0.2.1'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
foreach ($item in $plan) {
    if ((Get-FileHash $item.sourceIni).Hash -cne $item.sourceSha256 -or
        (Get-FileHash $item.backup).Hash -cne $item.backupSha256) { throw 'QAQM evidence changed before export.' }
    [IO.File]::WriteAllText((Join-Path $package $item.filename), $item.text, $encoding)
}
Copy-Item -LiteralPath (Join-Path $root 'docs\QAQM-STATE-RECOVERY.md') -Destination (Join-Path $package 'README-zh-CN.md')
$zip = Join-Path $output 'EndfieldJiggle-QAQM-State-Recovery-v0.2.1.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Split-Path -Parent $package), $zip)
$hash = (Get-FileHash $zip).Hash
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash *$([IO.Path]::GetFileName($zip))`n", $encoding)
[ordered]@{
    schema=1; sourceDirectory=$source; hosts=$plan; archive=$zip; archiveSha256=$hash
    explicitExternalStateHostsChecked=[bool]$EfmiPath
    externalStateHosts=$(if ($EfmiPath) { @(Get-QaqmActiveStateHosts $EfmiPath) } else { @() })
    liveInstallationModified=$false; sourceFilesModified=$false; inGameVerified=$false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'repair-report.json') -Encoding utf8
Write-Output "QAQM_REPAIR=$zip"
Write-Output "QAQM_HOST_DIRECTORY=$package"
