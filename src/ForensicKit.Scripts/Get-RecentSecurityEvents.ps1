<#
.SYNOPSIS
    ForensicKit embedded script: dumps recent Security event-log entries of interest.
.DESCRIPTION
    Read-only. Retrieves recent logon (4624), logon-failure (4625), account-lockout
    (4740) and new-service (7045) events. Reading the Security log requires
    administrator rights, so run this script elevated for full results.
.NOTES
    Ships inside ForensicKit (not downloaded at runtime) so its contents are auditable.
#>

$ErrorActionPreference = 'Continue'
$maxEvents = 100

function Write-Section($title) {
    Write-Output ''
    Write-Output ('=' * 60)
    Write-Output "  $title"
    Write-Output ('=' * 60)
}

Write-Output "ForensicKit :: Recent Security Events"
Write-Output ("Generated : {0}" -f (Get-Date).ToString('u'))
Write-Output ("Max/type  : {0}" -f $maxEvents)

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Output ''
    Write-Output "[!] Not running elevated. The Security log usually requires admin rights;"
    Write-Output "    re-run this script with elevation for complete results."
}

function Dump-Events($logName, $id, $title) {
    Write-Section $title
    try {
        Get-WinEvent -FilterHashtable @{ LogName = $logName; Id = $id } -MaxEvents $maxEvents -ErrorAction Stop |
            Select-Object TimeCreated, Id,
                @{N='Message';E={ ($_.Message -split "`r`n")[0] }} |
            Format-Table -AutoSize | Out-String | Write-Output
    } catch {
        Write-Output "  [!] $($_.Exception.Message)"
    }
}

Dump-Events 'Security' 4624 'Successful logons (4624)'
Dump-Events 'Security' 4625 'Failed logons (4625)'
Dump-Events 'Security' 4740 'Account lockouts (4740)'
Dump-Events 'System'   7045 'New services installed (7045)'

Write-Output ''
Write-Output "Done."
