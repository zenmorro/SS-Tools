<#
.SYNOPSIS
    ForensicKit :: Rilevamento di possibili DMA card (FPGA tipo PCILeech,
    PCIeScreamer/Squirrel, ZDMA) tramite analisi dei dispositivi PCIe.

.DESCRIPTION
    Sola lettura. Enumera i dispositivi PCIe, li confronta con il database pci.ids
    (fonte di verità per Vendor/Device ID) e con una blocklist configurabile di ID
    stock noti di DMA card pubbliche, applica alcune euristiche aggiuntive e verifica
    lo stato della protezione DMA (VT-d/IOMMU) del sistema.

    NON esegue alcuna azione invasiva o di blocco: solo rilevamento e reporting.

.PARAMETER JsonPath
    Se specificato, esporta il report strutturato in un file JSON.

.PARAMETER BlocklistPath
    Percorso di una blocklist JSON personalizzata. Default:
    %APPDATA%\ForensicKit\dma-blocklist.json (creata con i valori di default al
    primo avvio, così è modificabile senza toccare lo script). Override anche via
    variabile d'ambiente FORENSICKIT_DMA_BLOCKLIST.

.PARAMETER PciIdsPath
    Percorso di un pci.ids locale da usare. Se assente lo script cerca una cache
    in %APPDATA%\ForensicKit\pci.ids, altrimenti prova a scaricarlo.

.PARAMETER OfflineOnly
    Non tentare il download di pci.ids: usa solo cache locale o il subset bundled.

.NOTES
    Ship-in-app: incluso in ForensicKit, non scaricato a runtime → contenuto auditabile.
    Compatibile con Windows PowerShell 5.1. Alcuni controlli (BAR, driver) sono
    approssimazioni: leggere lo spazio di configurazione PCI reale richiederebbe un
    driver kernel, fuori scope per uno strumento di sola lettura user-mode.
#>

[CmdletBinding()]
param(
    [string]$JsonPath,
    [string]$BlocklistPath,
    [string]$PciIdsPath,
    [switch]$OfflineOnly
)

$ErrorActionPreference = 'Continue'

# ------------------------------------------------------------------ costanti
# Vendor ID dei principali produttori di FPGA usati nelle DMA card in commercio.
# Non implicano di per sé malevolenza (sono chip legittimi), ma sono il "mattone"
# di quasi tutte le schede DMA: la loro presenza è un flag informativo.
$FpgaVendors = @{
    '10ee' = 'Xilinx (AMD) — base della maggior parte delle DMA card (PCILeech/Screamer)'
    '1204' = 'Lattice Semiconductor — usato in alcune DMA card'
    '1172' = 'Altera (Intel) — usato in alcune DMA card'
}

# URL ufficiale del database pci.ids e cache locale.
$PciIdsUrl   = 'https://pci-ids.ucw.cz/v2.2/pci.ids'
$AppDataDir  = Join-Path $env:APPDATA 'ForensicKit'
$PciIdsCache = Join-Path $AppDataDir 'pci.ids'

# Soglia (giorni) oltre la quale la cache pci.ids viene considerata "vecchia".
$PciIdsMaxAgeDays = 30

# ------------------------------------------------------------------ utility output
function Write-Section($title) {
    Write-Output ''
    Write-Output ('=' * 66)
    Write-Output "  $title"
    Write-Output ('=' * 66)
}

# Ordine di severità: info(1) < sospetto(2) < alto(3)
$SevRank = @{ 'info' = 1; 'sospetto' = 2; 'alto' = 3 }

# ================================================================== 1) pci.ids
# Restituisce @{ Vendors=@{vid=name}; Devices=@{"vid:did"=name}; Source='...' }
function Import-PciIds {
    param([string]$ExplicitPath, [switch]$Offline)

    $path = $null
    $source = ''

    # a) percorso esplicito
    if ($ExplicitPath -and (Test-Path $ExplicitPath)) {
        $path = $ExplicitPath; $source = "file: $ExplicitPath"
    }

    # b) cache locale valida
    if (-not $path -and (Test-Path $PciIdsCache)) {
        $ageDays = ((Get-Date) - (Get-Item $PciIdsCache).LastWriteTime).TotalDays
        if ($Offline -or $ageDays -le $PciIdsMaxAgeDays) {
            $path = $PciIdsCache
            $source = "cache locale ($([math]::Round($ageDays,1)) giorni)"
        }
    }

    # c) download (se consentito)
    if (-not $path -and -not $Offline) {
        try {
            if (-not (Test-Path $AppDataDir)) { New-Item -ItemType Directory -Path $AppDataDir -Force | Out-Null }
            Write-Output "  Scarico pci.ids da $PciIdsUrl ..."
            # TLS 1.2 per compatibilità con il server su Windows PowerShell 5.1
            try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
            Invoke-WebRequest -Uri $PciIdsUrl -OutFile $PciIdsCache -UseBasicParsing -TimeoutSec 40
            $path = $PciIdsCache; $source = 'scaricato ora'
        }
        catch {
            Write-Output "  [!] Download pci.ids fallito: $($_.Exception.Message)"
            if (Test-Path $PciIdsCache) { $path = $PciIdsCache; $source = 'cache locale (download fallito)' }
        }
    }

    $vendors = @{}
    $devices = @{}

    if ($path -and (Test-Path $path)) {
        # Parsing del formato pci.ids:
        #   vvvv<2 spazi>Vendor              (riga vendor, colonna 0)
        #   <tab>dddd<2 spazi>Device         (riga device)
        #   <tab><tab>ssss ssss<2 spazi>...  (riga subsystem, ignorata)
        # La sezione classi inizia con "C xx ..." a colonna 0: da lì stop vendor.
        $currentVendor = $null
        $inClasses = $false
        try {
            foreach ($line in [System.IO.File]::ReadLines($path)) {
                if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }

                if ($line[0] -eq "`t") {
                    if ($inClasses -or -not $currentVendor) { continue }
                    if ($line[1] -eq "`t") { continue }          # subsystem → skip
                    if ($line -match '^\t([0-9a-fA-F]{4})\s\s+(.+)$') {
                        $devices["$currentVendor`:$($matches[1].ToLower())"] = $matches[2].Trim()
                    }
                }
                else {
                    if ($line -match '^C\s') { $inClasses = $true; $currentVendor = $null; continue }
                    if ($line -match '^([0-9a-fA-F]{4})\s\s+(.+)$') {
                        $currentVendor = $matches[1].ToLower()
                        $vendors[$currentVendor] = $matches[2].Trim()
                        $inClasses = $false
                    }
                }
            }
        }
        catch {
            Write-Output "  [!] Errore nel parsing di pci.ids: $($_.Exception.Message)"
        }
    }

    # d) fallback: subset bundled (rete assente e nessuna cache).
    if ($vendors.Count -eq 0) {
        Write-Output "  [!] pci.ids non disponibile: uso il subset minimo incluso nello script."
        $source = 'subset bundled (limitato)'
        # Solo i vendor più comuni + i vendor FPGA, per non marcare tutto come "sconosciuto".
        $bundled = @{
            '8086' = 'Intel Corporation'; '1022' = 'Advanced Micro Devices, Inc. [AMD]'
            '10de' = 'NVIDIA Corporation'; '1002' = 'Advanced Micro Devices, Inc. [AMD/ATI]'
            '10ec' = 'Realtek Semiconductor Co., Ltd.'; '14e4' = 'Broadcom Inc.'
            '8087' = 'Intel Corporation'; '1179' = 'Toshiba'; '144d' = 'Samsung Electronics'
            '1b4b' = 'Marvell Technology Group Ltd.'; '1cc1' = 'ADATA Technology'
            '10ee' = 'Xilinx Corporation'; '1204' = 'Lattice Semiconductor'; '1172' = 'Altera Corporation'
        }
        foreach ($k in $bundled.Keys) { $vendors[$k] = $bundled[$k] }
    }

    return [pscustomobject]@{ Vendors = $vendors; Devices = $devices; Source = $source }
}

# ================================================================== 4) blocklist
function Import-Blocklist {
    param([string]$ExplicitPath)

    # Default DMA card note (stock). L'elenco è volutamente conservativo: la fonte
    # di verità restano pci.ids + euristiche. Estendibile via file esterno.
    $default = @(
        [pscustomobject]@{ vendor = '10ee'; device = '0666'; name = 'PCIeScreamer / PCILeech (Device ID di default 0x0666)' }
        [pscustomobject]@{ vendor = '10ee'; device = '0007'; name = 'Xilinx 0x0007 — usato da alcune build PCILeech' }
        [pscustomobject]@{ vendor = '10ee'; device = '8011'; name = 'Xilinx 0x8011 — visto su alcune DMA card' }
        [pscustomobject]@{ vendor = '1204'; device = '0001'; name = 'Lattice 0x0001 — DMA card economiche' }
    )

    $path = $ExplicitPath
    if (-not $path -and $env:FORENSICKIT_DMA_BLOCKLIST) { $path = $env:FORENSICKIT_DMA_BLOCKLIST }
    if (-not $path) { $path = Join-Path $AppDataDir 'dma-blocklist.json' }

    if (Test-Path $path) {
        try {
            $json = Get-Content -Raw -Path $path | ConvertFrom-Json
            $list = @()
            foreach ($e in $json) {
                if ($e.vendor -and $e.device) {
                    $list += [pscustomobject]@{
                        vendor = ([string]$e.vendor).ToLower().Replace('0x', '')
                        device = ([string]$e.device).ToLower().Replace('0x', '')
                        name   = if ($e.name) { $e.name } else { 'blocklist entry' }
                    }
                }
            }
            return @{ List = $list; Source = "file: $path" }
        }
        catch {
            Write-Output "  [!] Blocklist non leggibile ($($_.Exception.Message)); uso i default."
        }
    }
    else {
        # Crea il file di default così l'utente può modificarlo senza toccare il codice.
        try {
            if (-not (Test-Path $AppDataDir)) { New-Item -ItemType Directory -Path $AppDataDir -Force | Out-Null }
            $default | ConvertTo-Json | Set-Content -Path $path -Encoding UTF8
            Write-Output "  Blocklist di default creata in: $path (modificabile)."
            return @{ List = $default; Source = "default (scritto in $path)" }
        }
        catch {
            # Es. permessi insufficienti: procedi comunque con i default in memoria.
        }
    }

    return @{ List = $default; Source = 'default (in memoria)' }
}

# ================================================================== BAR/memoria
# Mappa DeviceID -> numero di range di memoria assegnati (proxy dei BAR di memoria).
# Nota: il layout preciso dei BAR richiede la lettura del config space PCI (driver
# kernel). Qui usiamo le risorse di memoria allocate come approssimazione utile.
function Get-MemoryRangeMap {
    $map = @{}
    try {
        $allocs = Get-CimInstance -ClassName Win32_PnPAllocatedResource -ErrorAction Stop
        foreach ($a in $allocs) {
            $dep = $a.Dependent
            $ant = $a.Antecedent
            if (-not $dep -or -not $ant) { continue }
            $devId = $dep.DeviceID
            # Consideriamo solo le risorse di memoria (Win32_DeviceMemoryAddress ha
            # StartingAddress come chiave, quindi è presente nel riferimento).
            $start = $null
            try { $start = $ant.StartingAddress } catch {}
            if ($devId -and $null -ne $start) {
                if (-not $map.ContainsKey($devId)) { $map[$devId] = 0 }
                $map[$devId] = $map[$devId] + 1
            }
        }
    }
    catch {
        # In caso di errore (permessi/WMI) la mappa resta vuota → BAR "n/d".
    }
    return $map
}

# ================================================================== 5) protezioni
function Get-DmaProtectionStatus {
    $result = [ordered]@{
        DmaProtectionAvailable = $null   # VT-d/IOMMU abilitato nel firmware
        VbsRunning             = $null
        Detail                 = ''
    }
    try {
        $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' `
            -ClassName Win32_DeviceGuard -ErrorAction Stop
        # AvailableSecurityProperties: 3 = "DMA Protection" (VT-d/IOMMU presente e
        # ABILITATO nel firmware/BIOS, non solo supportato dall'hardware).
        $avail = @($dg.AvailableSecurityProperties)
        $result.DmaProtectionAvailable = ($avail -contains 3)
        # SecurityServicesRunning: 2 = HVCI (indice di VBS attiva).
        $running = @($dg.SecurityServicesRunning)
        $result.VbsRunning = ($running -contains 1 -or $running -contains 2)
        $result.Detail = "AvailableSecurityProperties = [$($avail -join ',')]; SecurityServicesRunning = [$($running -join ',')]"
    }
    catch {
        $result.Detail = "Impossibile leggere Win32_DeviceGuard: $($_.Exception.Message)"
    }
    return [pscustomobject]$result
}

# ================================================================== helper flag
function New-Flag($severity, $reason) {
    [pscustomobject]@{ Severity = $severity; Reason = $reason }
}

# ================================================================== MAIN
Write-Output "ForensicKit :: Rilevamento possibili DMA card (analisi PCIe)"
Write-Output ("Generato : {0}" -f (Get-Date).ToString('u'))
Write-Output ("Host     : {0}   Utente: {1}\{2}" -f $env:COMPUTERNAME, $env:USERDOMAIN, $env:USERNAME)

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Output "[i] Esecuzione non elevata: l'enumerazione funziona comunque, ma alcuni"
    Write-Output "    dettagli (BAR/driver/protezioni) sono più completi con elevazione."
}

Write-Section '1-2) Database pci.ids'
$pci = Import-PciIds -ExplicitPath $PciIdsPath -Offline:$OfflineOnly
Write-Output ("  Fonte pci.ids : {0}" -f $pci.Source)
Write-Output ("  Vendor noti   : {0}   Device noti: {1}" -f $pci.Vendors.Count, $pci.Devices.Count)

Write-Section '4) Blocklist DMA card'
$bl = Import-Blocklist -ExplicitPath $BlocklistPath
Write-Output ("  Fonte blocklist : {0}   voci: {1}" -f $bl.Source, $bl.List.Count)

# Precalcolo mappe di supporto.
$memMap = Get-MemoryRangeMap

Write-Section '3) Enumerazione dispositivi PCIe'

$pciDevices = @()
try {
    $pciDevices = Get-PnpDevice -PresentOnly -ErrorAction Stop |
        Where-Object { $_.InstanceId -like 'PCI\*' }
}
catch {
    Write-Output "[!] Impossibile enumerare i dispositivi PnP: $($_.Exception.Message)"
    Write-Output "    Verificare i permessi o eseguire come amministratore."
    return
}

if (-not $pciDevices -or $pciDevices.Count -eq 0) {
    Write-Output "[!] Nessun dispositivo PCIe rilevato (o accesso negato)."
    return
}

$report = @()

foreach ($dev in $pciDevices) {
    $id = $dev.InstanceId

    # ---- Parsing degli identificativi dall'InstanceId -----------------------
    $ven = $null; $devId = $null; $subVen = $null; $subDev = $null; $rev = $null
    if ($id -match 'VEN_([0-9A-Fa-f]{4})') { $ven = $matches[1].ToLower() }
    if ($id -match 'DEV_([0-9A-Fa-f]{4})') { $devId = $matches[1].ToLower() }
    if ($id -match 'SUBSYS_([0-9A-Fa-f]{8})') {
        $subsys = $matches[1].ToLower()
        $subDev = $subsys.Substring(0, 4)   # high word = subsystem device
        $subVen = $subsys.Substring(4, 4)   # low  word = subsystem vendor
    }
    if ($id -match 'REV_([0-9A-Fa-f]{2})') { $rev = $matches[1].ToLower() }

    # ---- Proprietà aggiuntive (class code, driver, location, problema) ------
    $classCode = $null; $driverInf = $null; $problem = $null; $location = $null
    try {
        $props = Get-PnpDeviceProperty -InstanceId $id -KeyName `
            'DEVPKEY_Device_CompatibleIds', 'DEVPKEY_Device_DriverInfPath', `
            'DEVPKEY_Device_ProblemCode', 'DEVPKEY_Device_LocationInfo' -ErrorAction SilentlyContinue

        foreach ($p in $props) {
            switch ($p.KeyName) {
                'DEVPKEY_Device_CompatibleIds' {
                    # Cerca la classe PCI CC_CCSSPP nei compatible IDs (prende la più lunga).
                    foreach ($cid in @($p.Data)) {
                        if ($cid -match 'CC_([0-9A-Fa-f]{6})') { $classCode = $matches[1].ToLower(); break }
                        elseif ($cid -match 'CC_([0-9A-Fa-f]{4})' -and -not $classCode) { $classCode = $matches[1].ToLower() }
                    }
                }
                'DEVPKEY_Device_DriverInfPath' { $driverInf = $p.Data }
                'DEVPKEY_Device_ProblemCode'   { $problem = $p.Data }
                'DEVPKEY_Device_LocationInfo'  { $location = $p.Data }
            }
        }
    }
    catch {}

    $baseClass = if ($classCode -and $classCode.Length -ge 2) { $classCode.Substring(0, 2) } else { $null }
    $memRanges = if ($ven -and $memMap.ContainsKey($id)) { $memMap[$id] } else { 0 }
    $hasDriver = -not [string]::IsNullOrWhiteSpace([string]$driverInf)

    # ---- Risoluzione nomi da pci.ids ----------------------------------------
    $vendorKnown = $ven -and $pci.Vendors.ContainsKey($ven)
    $vendorName = if ($vendorKnown) { $pci.Vendors[$ven] } else { $null }
    $deviceKnown = $ven -and $devId -and $pci.Devices.ContainsKey("$ven`:$devId")
    $deviceName = if ($deviceKnown) { $pci.Devices["$ven`:$devId"] } else { $null }

    # ---- Applicazione dei controlli (ogni controllo aggiunge un flag) -------
    $flags = @()

    # 4) Blocklist (priorità massima)
    $blMatch = $bl.List | Where-Object { $_.vendor -eq $ven -and $_.device -eq $devId } | Select-Object -First 1
    if ($blMatch) { $flags += New-Flag 'alto' "Corrispondenza con blocklist DMA card nota: $($blMatch.name)" }

    # 2a) Vendor ID non presente in pci.ids → sospetto alto
    if ($ven -and -not $vendorKnown) {
        $flags += New-Flag 'alto' "Vendor ID 0x$ven non presente in pci.ids (non registrato/sconosciuto)"
    }
    # 2b) Device ID non riconosciuto per un vendor noto → sospetto medio-alto
    elseif ($vendorKnown -and $devId -and -not $deviceKnown) {
        $flags += New-Flag 'sospetto' "Device ID 0x$devId non riconosciuto per il vendor '$vendorName'"
    }

    # 3) Vendor FPGA noto (flag informativo anche se il device è valido)
    if ($ven -and $FpgaVendors.ContainsKey($ven)) {
        $flags += New-Flag 'info' "Vendor FPGA: $($FpgaVendors[$ven])"
    }

    # 3) Subsystem Vendor ID assente o 0x0000 (tipico di FPGA non "brandizzate")
    if (-not $subVen -or $subVen -eq '0000') {
        $sev = if ($ven -and $FpgaVendors.ContainsKey($ven)) { 'sospetto' } else { 'info' }
        $flags += New-Flag $sev 'Subsystem Vendor ID assente o 0x0000'
    }

    # 3) Assenza di driver / problema di installazione (chip FPGA "adattato")
    if (-not $hasDriver -or $problem -eq 28) {
        $sev = if ($ven -and $FpgaVendors.ContainsKey($ven)) { 'alto' } else { 'info' }
        $flags += New-Flag $sev "Nessun driver associato (problem code: $problem)"
    }

    # 3) Class code incoerente con la descrizione risolta da pci.ids (euristica)
    if ($deviceName -and $baseClass) {
        $n = $deviceName.ToLower()
        $mismatch = $false
        if (($n -match 'ethernet|network') -and $baseClass -ne '02') { $mismatch = $true }
        elseif (($n -match 'vga|display|graphics') -and $baseClass -ne '03') { $mismatch = $true }
        elseif (($n -match 'nvme|sata|ahci|storage') -and $baseClass -ne '01') { $mismatch = $true }
        if ($mismatch) {
            $flags += New-Flag 'sospetto' "Class code 0x$classCode incoerente con la descrizione '$deviceName'"
        }
    }

    # 3) BAR/memoria anomala: classi che normalmente usano memoria (display/rete/
    #    storage) ma senza alcun range di memoria assegnato → possibile spoofing.
    if ($baseClass -in @('01', '02', '03') -and $memRanges -eq 0 -and $memMap.Count -gt 0) {
        $flags += New-Flag 'sospetto' "Class code 0x$baseClass atteso con BAR di memoria, ma nessun range assegnato"
    }

    # ---- Severità complessiva del dispositivo -------------------------------
    $severity = 'info'
    foreach ($f in $flags) { if ($SevRank[$f.Severity] -gt $SevRank[$severity]) { $severity = $f.Severity } }
    if ($flags.Count -eq 0) { $severity = 'ok' }

    $report += [pscustomobject]@{
        Location    = if ($location) { $location } else { '-' }
        Vendor      = "0x$ven"
        Device      = "0x$devId"
        VendorName  = if ($vendorName) { $vendorName } else { '(sconosciuto)' }
        DeviceName  = if ($deviceName) { $deviceName } else { '(non riconosciuto)' }
        Class       = if ($classCode) { "0x$classCode" } else { '-' }
        SubVendor   = if ($subVen) { "0x$subVen" } else { '-' }
        SubDevice   = if ($subDev) { "0x$subDev" } else { '-' }
        Rev         = if ($rev) { "0x$rev" } else { '-' }
        MemRanges   = $memRanges
        Driver      = if ($hasDriver) { 'sì' } else { 'no' }
        Severity    = $severity
        Flags       = $flags
        InstanceId  = $id
    }
}

# ================================================================== 6) OUTPUT
Write-Section '6) Riepilogo dispositivi PCIe'

# Ordina: prima i più rischiosi.
$order = @{ 'alto' = 0; 'sospetto' = 1; 'info' = 2; 'ok' = 3 }
$sorted = $report | Sort-Object @{ Expression = { $order[$_.Severity] } }, Vendor

$sorted |
    Select-Object @{N = 'Sev'; E = { $_.Severity.ToUpper() } }, Location, Vendor, Device,
        @{N = 'Vendor (pci.ids)'; E = { $_.VendorName } },
        @{N = 'Device (pci.ids)'; E = { $_.DeviceName } }, Class, Driver |
    Format-Table -AutoSize | Out-String -Width 300 | Write-Output

# Dettaglio dei soli dispositivi con almeno un flag.
$flagged = $sorted | Where-Object { $_.Severity -ne 'ok' -and $_.Flags.Count -gt 0 }

Write-Section 'Dettaglio anomalie'
if (-not $flagged) {
    Write-Output "  Nessuna anomalia rilevata dai controlli attuali."
}
else {
    foreach ($d in $flagged) {
        Write-Output ""
        Write-Output ("  [{0}] {1} {2}  —  {3} / {4}" -f `
                $d.Severity.ToUpper(), $d.Vendor, $d.Device, $d.VendorName, $d.DeviceName)
        Write-Output ("        Location: {0} | SubVen: {1} SubDev: {2} | BAR mem: {3} | Driver: {4}" -f `
                $d.Location, $d.SubVendor, $d.SubDevice, $d.MemRanges, $d.Driver)
        foreach ($f in $d.Flags) {
            Write-Output ("        - ({0}) {1}" -f $f.Severity, $f.Reason)
        }
    }
}

# ---- 5) Stato protezioni DMA di sistema ----------------------------------
Write-Section '5) Protezione DMA di sistema (VT-d / IOMMU)'
$prot = Get-DmaProtectionStatus
switch ($prot.DmaProtectionAvailable) {
    $true  { Write-Output "  DMA Protection (VT-d/IOMMU) ABILITATA nel firmware: SÌ" }
    $false { Write-Output "  DMA Protection (VT-d/IOMMU) ABILITATA nel firmware: NO  <-- rischio più alto per attacchi DMA" }
    default { Write-Output "  DMA Protection: stato non determinabile" }
}
Write-Output ("  Virtualization-Based Security attiva: {0}" -f `
    (@{ $true = 'sì'; $false = 'no' }[[bool]$prot.VbsRunning]))
Write-Output ("  Dettaglio: {0}" -f $prot.Detail)
Write-Output "  Nota: lo stato 'Kernel DMA Protection' (hot-plug Thunderbolt) è visibile"
Write-Output "        anche in msinfo32; qui riportiamo la protezione DMA VT-d/IOMMU."

# ---- Conteggi finali ------------------------------------------------------
$nHigh = ($report | Where-Object Severity -eq 'alto').Count
$nSusp = ($report | Where-Object Severity -eq 'sospetto').Count
Write-Section 'Esito'
Write-Output ("  Dispositivi PCIe analizzati : {0}" -f $report.Count)
Write-Output ("  Alto rischio                : {0}" -f $nHigh)
Write-Output ("  Sospetti                    : {0}" -f $nSusp)
if ($nHigh -gt 0) {
    Write-Output "  >> Verificare manualmente i dispositivi ad ALTO rischio elencati sopra."
}
elseif ($nSusp -gt 0) {
    Write-Output "  >> Nessun alto rischio, ma alcuni dispositivi meritano una verifica."
}
else {
    Write-Output "  >> Nessun indicatore evidente di DMA card. (Non è una garanzia assoluta.)"
}

# ---- 6) Export JSON opzionale --------------------------------------------
if ($JsonPath) {
    try {
        $export = [pscustomobject]@{
            generatedUtc      = (Get-Date).ToUniversalTime().ToString('o')
            host              = $env:COMPUTERNAME
            pciIdsSource      = $pci.Source
            blocklistSource   = $bl.Source
            dmaProtection     = $prot
            deviceCount       = $report.Count
            highRisk          = $nHigh
            suspect           = $nSusp
            devices           = $report
        }
        $export | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding UTF8
        Write-Output ""
        Write-Output "  Report JSON esportato in: $JsonPath"
    }
    catch {
        Write-Output "  [!] Export JSON fallito: $($_.Exception.Message)"
    }
}

Write-Output ""
Write-Output "Done."
