<#
.SYNOPSIS
    ForensicKit embedded script: collects a general system information snapshot.
.DESCRIPTION
    Read-only. Gathers OS, hardware, network, logged-on user and boot-time data and
    prints it as structured text. Does not modify the system in any way.
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

Write-Output "ForensicKit :: System Information Snapshot"
Write-Output ("Generated : {0}" -f (Get-Date).ToString('u'))
Write-Output ("Host      : {0}" -f $env:COMPUTERNAME)
Write-Output ("User      : {0}\{1}" -f $env:USERDOMAIN, $env:USERNAME)

Write-Section 'Operating System'
try {
    Get-CimInstance Win32_OperatingSystem |
        Select-Object Caption, Version, BuildNumber, OSArchitecture,
            @{N='InstallDate';E={$_.InstallDate}},
            @{N='LastBootUpTime';E={$_.LastBootUpTime}},
            @{N='UptimeHours';E={ [math]::Round(((Get-Date) - $_.LastBootUpTime).TotalHours, 1) }} |
        Format-List | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Section 'Computer / Hardware'
try {
    Get-CimInstance Win32_ComputerSystem |
        Select-Object Manufacturer, Model, SystemType, NumberOfProcessors,
            @{N='TotalPhysicalMemoryGB';E={ [math]::Round($_.TotalPhysicalMemory/1GB,2) }} |
        Format-List | Out-String | Write-Output
    Get-CimInstance Win32_BIOS |
        Select-Object Manufacturer, SMBIOSBIOSVersion, SerialNumber, ReleaseDate |
        Format-List | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Section 'Logical Disks'
try {
    Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" |
        Select-Object DeviceID, VolumeName, FileSystem,
            @{N='SizeGB';E={ [math]::Round($_.Size/1GB,1) }},
            @{N='FreeGB';E={ [math]::Round($_.FreeSpace/1GB,1) }} |
        Format-Table -AutoSize | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Section 'Network Adapters (IPv4)'
try {
    Get-NetIPConfiguration -ErrorAction Stop |
        Where-Object { $_.IPv4Address } |
        Select-Object InterfaceAlias,
            @{N='IPv4';E={ ($_.IPv4Address.IPAddress -join ', ') }},
            @{N='Gateway';E={ ($_.IPv4DefaultGateway.NextHop -join ', ') }},
            @{N='DNS';E={ ($_.DNSServer.ServerAddresses -join ', ') }} |
        Format-Table -AutoSize | Out-String | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Section 'Currently Logged-on Users'
try {
    (query user) 2>$null | Write-Output
} catch { Write-Output "  [!] $($_.Exception.Message)" }

Write-Output ''
Write-Output "Done."
