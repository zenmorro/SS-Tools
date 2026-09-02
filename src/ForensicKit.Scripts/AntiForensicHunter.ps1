<#
.SYNOPSIS
    ScriptZ :: AntiForensicHunter - Raccolta di indicatori di attivita' anti-forense
    e tracce di sistema, con output a TABELLA (nessun file CSV/JSON).

.DESCRIPTION
    Usa Get-WinEvent mappando ogni Event ID al log corretto, piu' controlli aggiuntivi
    che gli Event ID da soli non coprono (USN Journal, Zone.Identifier/MOTW, prefetch,
    USBSTOR registry, restart servizi, modifica orario). I risultati vengono mostrati
    a schermo in forma tabellare, raggruppati per severita'.

.NOTES
    Ship-in-app: incluso in ForensicKit, non scaricato a runtime -> contenuto auditabile.
    Eseguire come Amministratore per accedere al log Security e a USN/MFT.
    Sola lettura: nessuna azione invasiva, nessun file scritto su disco.
#>

[CmdletBinding()]
param(
    [int]$MaxEventsPerQuery = 50,
    [datetime]$StartTime = (Get-Date).AddDays(-14),
    # Se valorizzato, scrive i risultati come JSON in questo file (usato dalla GUI
    # per popolare la tabella/timeline). Non genera comunque CSV/JSON "di report".
    [string]$JsonOut
)

$ErrorActionPreference = 'Continue'

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Output "[!] Non eseguito come Amministratore: il log Security e alcuni controlli"
    Write-Output "    (USN, MFT) potrebbero fallire o risultare incompleti."
}

Write-Output "ScriptZ :: AntiForensicHunter"
Write-Output ("Generato : {0}" -f (Get-Date).ToString('u'))
Write-Output ("Finestra : dal {0}" -f $StartTime.ToString('u'))

# ------------------------------------------------------------------
# MAPPA LOG -> EVENT ID (ogni ID associato SOLO al log in cui esiste)
# ------------------------------------------------------------------
$LogEventMap = @{
    "Security" = @{
        1102 = "Log di sicurezza cancellato (indicatore forte di anti-forensics)"
        4616 = "Modifica dell'orario di sistema"
        4688 = "Creazione di un nuovo processo"
        4689 = "Terminazione di un processo"
        4660 = "Eliminazione di un oggetto (auditing attivo)"
        4663 = "Tentativo di accesso a un oggetto (auditing attivo)"
        4720 = "Creazione di un account utente"
        4728 = "Aggiunta membro a gruppo globale (privilege escalation)"
        4732 = "Aggiunta membro a gruppo locale (privilege escalation)"
        4672 = "Assegnazione privilegi speciali a nuovo logon"
    }
    "System" = @{
        7     = "Errore hardware/disco rilevato"
        104   = "Log cancellato (System/Application)"
        1074  = "Sistema riavviato/spento da un processo"
        6005  = "Avvio del sistema (Event Log service started)"
        6006  = "Spegnimento pulito del sistema"
        6008  = "Spegnimento inatteso (crash)"
        7036  = "Servizio Running/Stopped"
        7040  = "Modifica start type di un servizio"
        7045  = "Installazione di un nuovo servizio"
        20001 = "Installazione driver plug-and-play (USB)"
        20003 = "Rimozione driver plug-and-play"
    }
    "Application" = @{
        1000 = "Application Error (crash)"
        1001 = "Application Hang / Windows Error Reporting"
    }
    "Microsoft-Windows-NTFS/Operational" = @{
        142 = "Possibile riferimento a modifica USN Journal"
    }
    "Microsoft-Windows-Kernel-EventTracing/Admin" = @{
        2 = "Sessione ETW arrestata (possibile silenziamento telemetria)"
    }
    "Windows PowerShell" = @{
        400 = "Avvio host PowerShell"
        403 = "Chiusura host PowerShell"
    }
    "Microsoft-Windows-PowerShell/Operational" = @{
        4104 = "Esecuzione ScriptBlock PowerShell (ScriptBlock logging)"
    }
    "Microsoft-Windows-DriverFrameworks-UserMode/Operational" = @{
        2003 = "Connessione dispositivo USB (driver usermode)"
        2004 = "Rimozione dispositivo USB (driver usermode)"
    }
    "Microsoft-Windows-Partition/Diagnostic" = @{
        1006 = "Nuova partizione/volume (inserimento USB/disco esterno)"
    }
}

# Collezione risultati.
$AllResults = New-Object System.Collections.Generic.List[Object]
function Add-Result {
    param($Category, $Source, $Timestamp, $Detail, $Severity = "Info")
    $AllResults.Add([PSCustomObject]@{
            Timestamp = $Timestamp
            Category  = $Category
            Severity  = $Severity
            Source    = $Source
            Detail    = $Detail
        })
}

# Estrae la prima riga "pulita" di un messaggio evento.
function Get-FirstLine([string]$msg) {
    if (-not $msg) { return "" }
    return (($msg -split "`r?`n")[0]).Trim()
}

# ---- 1) Eventi da Event Log --------------------------------------
Write-Output ""
Write-Output "=== 1. Raccolta eventi (Get-WinEvent) ==="
foreach ($logName in $LogEventMap.Keys) {
    $idMap = $LogEventMap[$logName]
    $ids = $idMap.Keys

    $logExists = Get-WinEvent -ListLog $logName -ErrorAction SilentlyContinue
    if (-not $logExists) {
        Write-Output "  [skip] log non trovato/non abilitato: $logName"
        continue
    }

    try {
        $events = Get-WinEvent -FilterHashtable @{ LogName = $logName; Id = $ids; StartTime = $StartTime } `
            -MaxEvents $MaxEventsPerQuery -ErrorAction Stop
    }
    catch {
        if ($_.Exception.Message -notmatch "No events were found") {
            Write-Output "  [!] Errore su '$logName': $($_.Exception.Message)"
        }
        continue
    }

    foreach ($ev in $events) {
        $meaning = $idMap[[int]$ev.Id]
        $severity = "Info"
        if ($ev.Id -in @(1102, 104, 4616)) { $severity = "Alto" }          # log/tempo alterati
        elseif ($ev.Id -in @(4728, 4732, 4660, 7045)) { $severity = "Sospetto" }

        Add-Result -Category "EventLog" -Source "$logName/$($ev.Id)" -Timestamp $ev.TimeCreated `
            -Severity $severity -Detail "$meaning | $(Get-FirstLine $ev.Message)"
    }
    Write-Output ("  [{0}] eventi: {1}" -f $logName, $events.Count)
}

# ---- 2) Log cancellati (dettaglio autore) ------------------------
Write-Output ""
Write-Output "=== 2. Log cancellati ==="
$cleared = Get-WinEvent -FilterHashtable @{ LogName = 'Security', 'System', 'Application'; Id = 1102, 104; StartTime = $StartTime } -ErrorAction SilentlyContinue
foreach ($ev in $cleared) {
    $who = try { $ev.Properties[1].Value } catch { "(sconosciuto)" }
    Add-Result -Category "LogTampering" -Source $ev.LogName -Timestamp $ev.TimeCreated -Severity "Alto" `
        -Detail "Log cancellato da: $who"
}
Write-Output ("  eventi di cancellazione log: {0}" -f @($cleared).Count)

# ---- 3) Modifiche orario di sistema ------------------------------
Write-Output ""
Write-Output "=== 3. Modifiche orario di sistema ==="
$timeChanges = Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4616; StartTime = $StartTime } -ErrorAction SilentlyContinue
foreach ($ev in $timeChanges) {
    Add-Result -Category "TimeManipulation" -Source "Security/4616" -Timestamp $ev.TimeCreated -Severity "Alto" `
        -Detail (Get-FirstLine $ev.Message)
}
Write-Output ("  modifiche orario: {0}" -f @($timeChanges).Count)

# ---- 4) USN Journal ----------------------------------------------
Write-Output ""
Write-Output "=== 4. USN Journal (C:) ==="
try {
    $usnInfo = fsutil usn queryjournal C: 2>&1
    if ($LASTEXITCODE -ne 0 -or ($usnInfo -match "not found|non trovato")) {
        Add-Result -Category "USNJournal" -Source "fsutil" -Timestamp (Get-Date) -Severity "Alto" `
            -Detail "USN Journal assente su C: - possibile cancellazione (fsutil usn deletejournal) o mai creato"
    }
    else {
        Add-Result -Category "USNJournal" -Source "fsutil" -Timestamp (Get-Date) -Severity "Info" `
            -Detail ("USN Journal presente: " + (($usnInfo | Select-Object -First 3) -join ' | '))
    }
}
catch {
    Add-Result -Category "USNJournal" -Source "fsutil" -Timestamp (Get-Date) -Severity "Sospetto" `
        -Detail "Impossibile interrogare lo USN Journal (permessi insufficienti)"
}

# ---- 5) Attivita' USB (registro + eventi) ------------------------
Write-Output ""
Write-Output "=== 5. Attivita' USB ==="
try {
    $usbDevices = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR\*\*" -ErrorAction SilentlyContinue
    foreach ($dev in $usbDevices) {
        Add-Result -Category "USBActivity" -Source "Registry USBSTOR" -Timestamp (Get-Date) -Severity "Info" `
            -Detail "Dispositivo: $($dev.FriendlyName) | Serial: $($dev.PSChildName)"
    }
}
catch {
    Write-Output "  [!] Impossibile leggere USBSTOR dal registro."
}
$usbEvents = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-DriverFrameworks-UserMode/Operational'; Id = 2003, 2004; StartTime = $StartTime } -ErrorAction SilentlyContinue
foreach ($ev in $usbEvents) {
    Add-Result -Category "USBActivity" -Source "DriverFrameworks/$($ev.Id)" -Timestamp $ev.TimeCreated -Severity "Info" `
        -Detail (Get-FirstLine $ev.Message)
}

# ---- 6) Zone.Identifier / MOTW -----------------------------------
Write-Output ""
Write-Output "=== 6. Zone.Identifier / Mark of the Web ==="
$searchPaths = @("$env:USERPROFILE\Downloads", "$env:USERPROFILE\Desktop", "$env:TEMP")
foreach ($path in $searchPaths) {
    if (Test-Path $path) {
        Get-ChildItem -Path $path -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
            $zoneStream = Get-Item -Path $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
            if ($zoneStream) {
                $zoneContent = Get-Content -Path "$($_.FullName):Zone.Identifier" -ErrorAction SilentlyContinue
                Add-Result -Category "ZoneIdentifier" -Source $_.Name -Timestamp $_.LastWriteTime -Severity "Info" `
                    -Detail ("MOTW: " + (($zoneContent -join '; ') -replace '\s+', ' '))
            }
        }
    }
}

# ---- 7) Restart servizi / explorer -------------------------------
Write-Output ""
Write-Output "=== 7. Restart servizi / explorer ==="
$serviceEvents = Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 7036; StartTime = $StartTime } -ErrorAction SilentlyContinue
foreach ($ev in $serviceEvents) {
    $msg = Get-FirstLine $ev.Message
    $sev = "Info"
    if ($msg -match "explorer|Defender|EventLog|Update|Sysmon") { $sev = "Sospetto" }
    Add-Result -Category "ServiceStateChange" -Source "System/7036" -Timestamp $ev.TimeCreated -Severity $sev -Detail $msg
}
$explorerProc = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue
foreach ($p in $explorerProc) {
    Add-Result -Category "ExplorerRestart" -Source "Win32_Process" -Timestamp $p.CreationDate -Severity "Info" `
        -Detail "explorer.exe PID $($p.ProcessId) avviato (confrontare con l'orario di boot)"
}

# ---- 8) Integrita' Prefetch --------------------------------------
Write-Output ""
Write-Output "=== 8. Prefetch ==="
$prefetchPath = "$env:SystemRoot\Prefetch"
if (Test-Path $prefetchPath) {
    $prefetchCount = @(Get-ChildItem -Path $prefetchPath -Filter "*.pf" -ErrorAction SilentlyContinue).Count
    $sev = if ($prefetchCount -eq 0) { "Sospetto" } else { "Info" }
    Add-Result -Category "PrefetchIntegrity" -Source $prefetchPath -Timestamp (Get-Date) -Severity $sev `
        -Detail "File .pf presenti: $prefetchCount (0 su un sistema con uptime lungo e' sospetto)"
}
else {
    Add-Result -Category "PrefetchIntegrity" -Source $prefetchPath -Timestamp (Get-Date) -Severity "Sospetto" `
        -Detail "Cartella Prefetch assente (prefetch disabilitato o rimosso)"
}

# ==================================================================
# OUTPUT A TABELLA (nessun file CSV/JSON)
# ==================================================================
$SevRank = @{ 'Alto' = 0; 'Sospetto' = 1; 'Info' = 2 }

Write-Output ""
Write-Output "================================ RIEPILOGO ================================"
$AllResults | Group-Object Severity |
    Sort-Object @{ Expression = { $SevRank[$_.Name] } } |
    Select-Object @{N = 'Severita'; E = { $_.Name } }, @{N = 'Conteggio'; E = { $_.Count } } |
    Format-Table -AutoSize | Out-String -Width 200 | Write-Output

Write-Output "=============================== RISULTATI ================================"
if ($AllResults.Count -eq 0) {
    Write-Output "  Nessun indicatore raccolto."
}
else {
    $AllResults |
        Sort-Object @{ Expression = { $SevRank[$_.Severity] } }, @{ Expression = 'Timestamp'; Descending = $true } |
        Select-Object `
            @{N = 'Sev'; E = { $_.Severity } },
            @{N = 'Quando'; E = { if ($_.Timestamp) { (Get-Date $_.Timestamp -Format 'yyyy-MM-dd HH:mm:ss') } else { '-' } } },
            @{N = 'Categoria'; E = { $_.Category } },
            @{N = 'Sorgente'; E = { $_.Source } },
            @{N = 'Dettaglio'; E = { if ($_.Detail.Length -gt 90) { $_.Detail.Substring(0, 90) + '...' } else { $_.Detail } } } |
        Format-Table -AutoSize -Wrap | Out-String -Width 320 | Write-Output
}

# ---- Output JSON strutturato per la GUI (solo se richiesto) --------------
if ($JsonOut) {
    $arr = @($AllResults |
            Sort-Object @{ Expression = { $SevRank[$_.Severity] } }, @{ Expression = 'Timestamp'; Descending = $true } |
            Select-Object `
                @{N = 'TimestampIso'; E = { if ($_.Timestamp) { (Get-Date $_.Timestamp).ToString('o') } else { '' } } },
                Severity, Category, Source, Detail)
    $json = $arr | ConvertTo-Json -Depth 5
    if (-not $json) { $json = '[]' }
    # ConvertTo-Json (PS 5.1) collassa un array di 1 elemento in oggetto: forziamo l'array.
    elseif ($json.TrimStart()[0] -ne '[') { $json = "[$json]" }
    Set-Content -Path $JsonOut -Value $json -Encoding UTF8
}

Write-Output ""
Write-Output "Done."
