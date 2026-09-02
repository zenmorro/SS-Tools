<#
.SYNOPSIS
    ForensicKit embedded script: highlights potentially suspicious processes and services.
.DESCRIPTION
    Read-only triage aid. Flags running processes whose image lives in unusual
    locations (temp, appdata, users, programdata), processes with no signed image
    or missing company info, and non-Microsoft services set to auto-start. This is a
    heuristic starting point for analysts, NOT a verdict of maliciousness.
.NOTES
    Ships inside ForensicKit (not downloaded at runtime) so its contents are auditable.
#>

$ErrorActionPreference = 'Continue'

function Write-Section($title) {
    Write-Output ''
    Write-Output ('=' * 60)
    Write-Output "  $title"
    Write-Output ('=' * 60)
}

Write-Output "ForensicKit :: Suspicious Process / Service Triage"
Write-Output ("Generated : {0}" -f (Get-Date).ToString('u'))
Write-Output "Note      : Heuristic flags only. Verify before acting."

$suspiciousPaths = @('\Temp\', '\AppData\', '\Users\Public\', '\ProgramData\', '\Downloads\', '\Windows\Temp\')

Write-Section 'Processes in unusual locations'
try {
    Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath } |
        ForEach-Object {
            $path = $_.ExecutablePath
            $flag = $false
            foreach ($p in $suspiciousPaths) { if ($path -like "*$p*") { $flag = $true; break } }
            if ($flag) {
                [pscustomobject]@{
                    PID  = $_.ProcessId
                    Name = $_.Name
                    Path = $path
                }
            }
        } | Format-Table -AutoSize | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Section 'Running processes without a verified signature'
try {
    Get-Process | Where-Object { $_.Path } | Sort-Object Name -Unique | ForEach-Object {
        $sig = $null
        try { $sig = Get-AuthenticodeSignature -FilePath $_.Path -ErrorAction Stop } catch {}
        if (-not $sig -or $sig.Status -ne 'Valid') {
            [pscustomobject]@{
                Name    = $_.Name
                PID     = $_.Id
                Signature = if ($sig) { $sig.Status } else { 'Unknown' }
                Path    = $_.Path
            }
        }
    } | Format-Table -AutoSize | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Section 'Non-Microsoft auto-start services'
try {
    Get-CimInstance Win32_Service |
        Where-Object { $_.StartMode -eq 'Auto' -and $_.PathName } |
        Where-Object { $_.PathName -notlike '*\Windows\*' } |
        Select-Object Name, State, StartName, PathName |
        Format-Table -AutoSize | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Output ''
Write-Output "Done."
