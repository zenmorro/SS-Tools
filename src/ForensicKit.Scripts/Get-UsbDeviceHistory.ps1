<#
.SYNOPSIS
    ForensicKit embedded script: lists USB storage devices seen by this system.
.DESCRIPTION
    Read-only. Enumerates the USBSTOR registry key to recover the history of USB mass
    storage devices ever connected, including friendly name and last-known driver info.
    Useful for exfiltration / device-usage triage. Does not modify the registry.
.NOTES
    Ships inside ForensicKit (not downloaded at runtime) so its contents are auditable.
    Some values are best recovered with administrator rights.
#>

$ErrorActionPreference = 'Continue'

Write-Output "ForensicKit :: USB Storage Device History"
Write-Output ("Generated : {0}" -f (Get-Date).ToString('u'))
Write-Output ''

$root = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR'
if (-not (Test-Path $root)) {
    Write-Output "USBSTOR key not found (no USB storage history, or insufficient rights)."
    return
}

try {
    Get-ChildItem $root -ErrorAction Stop | ForEach-Object {
        $deviceClass = $_.PSChildName
        Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            $props = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            [pscustomobject]@{
                DeviceClass  = $deviceClass
                SerialNumber = $_.PSChildName
                FriendlyName = $props.FriendlyName
                Service      = $props.Service
            }
        }
    } | Format-Table -AutoSize | Out-String | Write-Output
} catch {
    Write-Output "  [!] $($_.Exception.Message)"
}

Write-Output ''
Write-Output "Also currently attached USB disks:"
try {
    Get-Disk | Where-Object BusType -eq 'USB' |
        Select-Object Number, FriendlyName, SerialNumber,
            @{N='SizeGB';E={ [math]::Round($_.Size/1GB,1) }} |
        Format-Table -AutoSize | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Output ''
Write-Output "Done."
