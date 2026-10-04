Set-StrictMode -Version Latest

function Get-LoaderDiagnostics {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Log)
    @($Log -split '\r?\n' | Where-Object {
        $_ -match '(?i)\b(?:WARNING|ERROR|failed)\b|Unrecogni[sz]ed (?:entry|section)|Duplicate section found|Entry outside of section|Unknown section type|Possible Mod Conflict' -and
        $_ -notmatch '^load failed\. Trying to chain load '
    })
}

Export-ModuleMember -Function Get-LoaderDiagnostics
