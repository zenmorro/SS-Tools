<#
.SYNOPSIS
    ForensicKit :: Verifica della validita' di token/permessi dei processi ed
    individuazione di processi zombie / in stato anomalo (target: Windows).

.DESCRIPTION
    Sola lettura. Per ogni processo apre il token (OpenProcessToken) e verifica
    Integrity Level, privilegi, tipo di token (Primary/Impersonation), Session ID e
    SID del proprietario; correla il proprietario con quello del processo padre per
    individuare possibili "token elevation/stealing". Rileva inoltre stati anomali
    (thread count = 0, orfani, handle count elevato, incoerenze tra WMI e Get-Process).

    Pensato per l'analisi forense di anomalie da cheat / bypass anticheat, dove i
    pattern tipici sono: SeDebugPrivilege / SeLoadDriverPrivilege su processi utente
    non elevati, processi SYSTEM generati da un utente standard, token manipolati.

    NON esegue alcun kill/termination: solo rilevamento e reporting.

.PARAMETER JsonPath
    Se specificato, esporta il report strutturato in un file JSON.

.PARAMETER HandleThreshold
    Soglia di handle oltre la quale un processo viene segnalato (default 10000).
    Razionale: la maggior parte dei processi legittimi resta ben sotto; valori molto
    alti possono indicare leak o tooling di injection/monitoring.

.NOTES
    Ship-in-app: incluso in ForensicKit, non scaricato a runtime -> contenuto auditabile.
    Compatibile con Windows PowerShell 5.1.
    Elevazione CONSIGLIATA: senza privilegi di amministratore i token dei processi di
    altri utenti / di sistema non sono accessibili (verranno marcati "non accessibile").
#>

[CmdletBinding()]
param(
    [string]$JsonPath,
    [int]$HandleThreshold = 10000
)

$ErrorActionPreference = 'Continue'

# ============================================================================
# Interop Win32: apertura del token di processo e lettura delle informazioni.
# Incapsulato in una classe C# per tenere pulita la logica PowerShell.
# ============================================================================
$cs = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class TokenResult {
    public bool Success;
    public string Error = "";
    public string IntegrityLevel = "";
    public int IntegrityRid = -1;
    public string TokenType = "";
    public int SessionId = -1;
    public bool IsElevated;
    public string OwnerSid = "";
    public string[] AllPrivileges = new string[0];
    public string[] EnabledPrivileges = new string[0];
}

public static class ForensicKitTokenProbe {
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_QUERY = 0x0008;

    // TOKEN_INFORMATION_CLASS
    const int TokenUser = 1;
    const int TokenPrivileges = 3;
    const int TokenTypeClass = 8;
    const int TokenSessionId = 12;
    const int TokenElevation = 20;
    const int TokenIntegrityLevel = 25;

    const uint SE_PRIVILEGE_ENABLED = 0x2;
    const uint SE_PRIVILEGE_ENABLED_BY_DEFAULT = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr p);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int retlen);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern bool LookupPrivilegeName(string system, ref LUID luid, StringBuilder name, ref int len);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr str);

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    public static TokenResult Query(int pid) {
        var r = new TokenResult();
        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero) {
            r.Error = "OpenProcess (" + Marshal.GetLastWin32Error() + ")";
            return r;
        }
        IntPtr hTok = IntPtr.Zero;
        try {
            if (!OpenProcessToken(hProc, TOKEN_QUERY, out hTok)) {
                r.Error = "OpenProcessToken (" + Marshal.GetLastWin32Error() + ")";
                return r;
            }
            r.IntegrityRid = GetIntegrityRid(hTok);
            r.IntegrityLevel = RidToName(r.IntegrityRid);
            r.TokenType = (GetDword(hTok, TokenTypeClass) == 2) ? "Impersonation" : "Primary";
            r.SessionId = GetDword(hTok, TokenSessionId);
            r.IsElevated = GetDword(hTok, TokenElevation) != 0;
            r.OwnerSid = GetUserSid(hTok);
            string[] all, en;
            GetPrivileges(hTok, out all, out en);
            r.AllPrivileges = all;
            r.EnabledPrivileges = en;
            r.Success = true;
            return r;
        } catch (Exception ex) {
            r.Error = ex.Message;
            return r;
        } finally {
            if (hTok != IntPtr.Zero) CloseHandle(hTok);
            CloseHandle(hProc);
        }
    }

    static int GetDword(IntPtr tok, int cls) {
        int len;
        GetTokenInformation(tok, cls, IntPtr.Zero, 0, out len);
        if (len < 4) len = 4;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try {
            if (GetTokenInformation(tok, cls, buf, len, out len)) return Marshal.ReadInt32(buf);
            return -1;
        } finally { Marshal.FreeHGlobal(buf); }
    }

    static int GetIntegrityRid(IntPtr tok) {
        int len;
        GetTokenInformation(tok, TokenIntegrityLevel, IntPtr.Zero, 0, out len);
        if (len == 0) return -1;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try {
            if (!GetTokenInformation(tok, TokenIntegrityLevel, buf, len, out len)) return -1;
            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label } -> primo campo = puntatore al SID
            IntPtr sid = Marshal.ReadIntPtr(buf);
            int subCount = Marshal.ReadByte(sid, 1);                 // SubAuthorityCount
            int offset = 8 + (subCount - 1) * 4;                     // ultima SubAuthority = integrity RID
            return Marshal.ReadInt32(sid, offset);
        } finally { Marshal.FreeHGlobal(buf); }
    }

    static string RidToName(int rid) {
        if (rid < 0) return "?";
        if (rid >= 0x5000) return "Protected";
        if (rid >= 0x4000) return "System";
        if (rid >= 0x3000) return "High";
        if (rid >= 0x2100) return "MediumPlus";
        if (rid >= 0x2000) return "Medium";
        if (rid >= 0x1000) return "Low";
        return "Untrusted";
    }

    static string GetUserSid(IntPtr tok) {
        int len;
        GetTokenInformation(tok, TokenUser, IntPtr.Zero, 0, out len);
        if (len == 0) return "";
        IntPtr buf = Marshal.AllocHGlobal(len);
        try {
            if (!GetTokenInformation(tok, TokenUser, buf, len, out len)) return "";
            IntPtr sid = Marshal.ReadIntPtr(buf);                   // TOKEN_USER.User.Sid
            IntPtr strSid;
            if (ConvertSidToStringSid(sid, out strSid)) {
                string s = Marshal.PtrToStringUni(strSid);
                LocalFree(strSid);
                return s;
            }
            return "";
        } finally { Marshal.FreeHGlobal(buf); }
    }

    static void GetPrivileges(IntPtr tok, out string[] all, out string[] enabled) {
        var la = new List<string>();
        var le = new List<string>();
        int len;
        GetTokenInformation(tok, TokenPrivileges, IntPtr.Zero, 0, out len);
        if (len > 0) {
            IntPtr buf = Marshal.AllocHGlobal(len);
            try {
                if (GetTokenInformation(tok, TokenPrivileges, buf, len, out len)) {
                    int count = Marshal.ReadInt32(buf);
                    for (int i = 0; i < count; i++) {
                        // TOKEN_PRIVILEGES: DWORD count + LUID_AND_ATTRIBUTES[] (12 byte ciascuno)
                        IntPtr entry = (IntPtr)(buf.ToInt64() + 4 + i * 12);
                        LUID luid;
                        luid.LowPart = (uint)Marshal.ReadInt32(entry, 0);
                        luid.HighPart = Marshal.ReadInt32(entry, 4);
                        uint attr = (uint)Marshal.ReadInt32(entry, 8);
                        var sb = new StringBuilder(256);
                        int nlen = 256;
                        if (LookupPrivilegeName(null, ref luid, sb, ref nlen)) {
                            string name = sb.ToString();
                            la.Add(name);
                            if ((attr & SE_PRIVILEGE_ENABLED) != 0 || (attr & SE_PRIVILEGE_ENABLED_BY_DEFAULT) != 0)
                                le.Add(name);
                        }
                    }
                }
            } finally { Marshal.FreeHGlobal(buf); }
        }
        all = la.ToArray();
        enabled = le.ToArray();
    }
}
'@

try {
    Add-Type -TypeDefinition $cs -ErrorAction Stop
}
catch {
    Write-Output "[!] Impossibile compilare l'interop Win32: $($_.Exception.Message)"
    return
}

# ============================================================================
# Tabelle di riferimento e utility
# ============================================================================

# Privilegi che di norma appartengono solo a SYSTEM: se presenti su un token il cui
# proprietario NON e' un account di sistema, sono un segnale ad alto rischio.
$SystemOnlyPrivs = @('SeTcbPrivilege', 'SeCreateTokenPrivilege', 'SeAssignPrimaryTokenPrivilege')

# Privilegi "pericolosi" nel contesto anticheat (injection / accesso kernel).
$DangerousPrivs = @{
    'SeDebugPrivilege'      = 'Debug/lettura memoria di altri processi (injection)'
    'SeLoadDriverPrivilege' = 'Caricamento driver (accesso kernel)'
    'SeTakeOwnershipPrivilege' = 'Take ownership di oggetti protetti'
}

# SID di sistema noti (proprietari legittimi di processi ad alta integrita').
function Test-SystemOwner([string]$sid) {
    if (-not $sid) { return $false }
    if ($sid -in @('S-1-5-18', 'S-1-5-19', 'S-1-5-20')) { return $true }   # SYSTEM/LocalService/NetworkService
    if ($sid -like 'S-1-5-80-*') { return $true }                          # Service SIDs
    if ($sid -like 'S-1-5-90-*') { return $true }                          # Window Manager\DWM
    if ($sid -like 'S-1-5-96-*') { return $true }                          # Font Driver Host
    return $false
}

# Un vero account utente interattivo ha un SID di dominio/locale S-1-5-21-...
function Test-RealUser([string]$sid) { return ($sid -like 'S-1-5-21-*') }

$SidCache = @{}
function Resolve-Sid([string]$sid) {
    if (-not $sid) { return $null }
    if ($SidCache.ContainsKey($sid)) { return $SidCache[$sid] }
    $acct = $null
    try {
        $o = New-Object System.Security.Principal.SecurityIdentifier($sid)
        $acct = $o.Translate([System.Security.Principal.NTAccount]).Value
    }
    catch { $acct = $null }   # SID non risolvibile -> possibile SID orfano
    $SidCache[$sid] = $acct
    return $acct
}

$SevRank = @{ 'info' = 1; 'sospetto' = 2; 'alto' = 3 }
function New-Flag($sev, $reason) { [pscustomobject]@{ Severity = $sev; Reason = $reason } }

# ============================================================================
# MAIN
# ============================================================================
Write-Output "ForensicKit :: Verifica token/permessi processi + rilevamento anomalie"
Write-Output ("Generato : {0}" -f (Get-Date).ToString('u'))
Write-Output ("Host     : {0}   Utente: {1}\{2}" -f $env:COMPUTERNAME, $env:USERDOMAIN, $env:USERNAME)

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Output "[i] Esecuzione NON elevata: i token dei processi di altri utenti/di sistema"
    Write-Output "    non saranno accessibili. Per un'analisi completa eseguire come Amministratore."
}

# ---- 1) Enumerazione processi (WMI per PPID/handle/thread/sessione/path) ----
Write-Output ""
Write-Output "Enumerazione processi in corso..."
try {
    $wmiProcs = Get-CimInstance Win32_Process -ErrorAction Stop
}
catch {
    Write-Output "[!] Enumerazione Win32_Process fallita: $($_.Exception.Message)"
    return
}

# Insiemi di supporto per i controlli di coerenza.
$pidSet = @{}
foreach ($p in $wmiProcs) { $pidSet[[int]$p.ProcessId] = $true }
$getProcSet = @{}
foreach ($gp in (Get-Process -ErrorAction SilentlyContinue)) { $getProcSet[[int]$gp.Id] = $true }

# ---- Pass 1: interroga il token di ogni processo una sola volta -------------
$tokens = @{}
foreach ($p in $wmiProcs) {
    $tokens[[int]$p.ProcessId] = [ForensicKitTokenProbe]::Query([int]$p.ProcessId)
}
# Mappa PID -> SID proprietario (per il confronto padre/figlio "token stealing").
$ownerByPid = @{}
foreach ($k in $tokens.Keys) {
    if ($tokens[$k].Success) { $ownerByPid[$k] = $tokens[$k].OwnerSid }
}

# ---- Pass 2: applica i controlli e calcola la severita' --------------------
$report = @()
$now = Get-Date

foreach ($p in $wmiProcs) {
    $procId = [int]$p.ProcessId
    $ppid = [int]$p.ParentProcessId
    $tok = $tokens[$procId]
    $flags = @()

    # Eta' del processo (per orfani/anomalie).
    $ageMin = $null
    if ($p.CreationDate) { $ageMin = [math]::Round(($now - $p.CreationDate).TotalMinutes, 1) }

    # ---- Controlli su stato/anomalie (validi anche senza token) ------------

    # 4) Thread count = 0 su processo ancora listato: stato inconsistente.
    #    Razionale: un processo vivo ha almeno un thread; 0 indica terminazione
    #    incompleta o manipolazione (equivalente Windows dello "zombie").
    #    Eccezione: alcuni pseudo-processi del kernel/VBS espongono 0 thread in WMI
    #    per design (non sono anomalie) -> li escludiamo dal controllo.
    $pseudoProcs = @('Secure System', 'Registry', 'Memory Compression', 'System Idle Process', 'System')
    if ($null -ne $p.ThreadCount -and [int]$p.ThreadCount -eq 0 -and $p.Name -notin $pseudoProcs) {
        $flags += New-Flag 'sospetto' 'Thread count = 0 (processo in stato inconsistente / non "reaped")'
    }

    # 4) Orfano: il processo padre non esiste piu'. Su Windows e' molto comune
    #    (i padri terminano spesso) -> severita' info, come da specifica.
    $parentAlive = $pidSet.ContainsKey($ppid)
    if (-not $parentAlive -and $ppid -gt 0) {
        $flags += New-Flag 'info' "Orfano: processo padre PID $ppid non piu' attivo (nessun reaper)"
    }

    # 4) Incoerenza tra WMI e Get-Process: possibile occultamento.
    if (-not $getProcSet.ContainsKey($procId)) {
        $flags += New-Flag 'sospetto' 'Visibile in WMI ma non in Get-Process (possibile occultamento)'
    }

    # Handle count elevato (soglia configurabile): possibile leak o tooling.
    if ($null -ne $p.HandleCount -and [int]$p.HandleCount -gt $HandleThreshold) {
        $flags += New-Flag 'info' "Handle count elevato ($($p.HandleCount) > $HandleThreshold)"
    }

    # ---- Controlli sul token (solo se accessibile) -------------------------
    $ownerSid = ''
    $ownerAccount = $null
    $integrity = ''
    $tokenType = ''
    $elevated = $false
    $tokenErr = ''

    if ($tok.Success) {
        $ownerSid = $tok.OwnerSid
        $integrity = $tok.IntegrityLevel
        $tokenType = $tok.TokenType
        $elevated = $tok.IsElevated
        $ownerAccount = Resolve-Sid $ownerSid
        $isSys = Test-SystemOwner $ownerSid
        $isUser = Test-RealUser $ownerSid

        # 2e) SID proprietario non risolvibile (orfano) -> sospetto.
        if ($ownerSid -and -not $ownerAccount) {
            $flags += New-Flag 'sospetto' "SID proprietario non risolvibile (orfano): $ownerSid"
        }

        # 2a) Integrity System su processo il cui owner NON e' un account di sistema.
        #     Razionale: solo processi di sistema dovrebbero girare a integrita' System.
        if ($tok.IntegrityRid -ge 0x4000 -and -not $isSys) {
            $flags += New-Flag 'alto' "Integrity Level 'System' ma owner non di sistema ($ownerAccount)"
        }

        # 2b) Privilegi SYSTEM-only su owner non di sistema -> alto rischio.
        foreach ($sp in $SystemOnlyPrivs) {
            if ($tok.AllPrivileges -contains $sp -and -not $isSys) {
                $flags += New-Flag 'alto' "Privilegio SYSTEM-only '$sp' su processo non di sistema"
            }
        }

        # 2b) Privilegi pericolosi su processo utente NON elevato.
        #     Razionale: un token medium-integrity di utente standard non dovrebbe
        #     possedere SeDebug/SeLoadDriver; la loro presenza indica manipolazione
        #     del token o escalation (classico nei bypass anticheat).
        if ($isUser -and $tok.IntegrityRid -lt 0x3000) {
            foreach ($dp in $DangerousPrivs.Keys) {
                if ($tok.AllPrivileges -contains $dp) {
                    $flags += New-Flag 'alto' ("Privilegio '{0}' su processo non elevato di utente standard - {1}" -f $dp, $DangerousPrivs[$dp])
                }
            }
        }
        # SeDebugPrivilege ATTIVO su processo utente NON elevato -> sospetto.
        #    Razionale: un processo admin elevato (High integrity) ha legittimamente
        #    SeDebug abilitato; e' l'uso su un processo NON elevato ad essere anomalo.
        if ($isUser -and -not $elevated -and ($tok.EnabledPrivileges -contains 'SeDebugPrivilege')) {
            $flags += New-Flag 'sospetto' 'SeDebugPrivilege ATTIVO su processo utente non elevato'
        }

        # 2c) Il primary token del processo e' di tipo Impersonation -> anomalo.
        if ($tokenType -eq 'Impersonation') {
            $flags += New-Flag 'sospetto' 'Primary token del processo di tipo Impersonation (anomalo)'
        }

        # 2d) Session ID del token diverso da quello del processo (WMI).
        if ($tok.SessionId -ge 0 -and $null -ne $p.SessionId -and $tok.SessionId -ne [int]$p.SessionId) {
            $flags += New-Flag 'sospetto' "Session ID token ($($tok.SessionId)) != sessione processo ($($p.SessionId))"
        }

        # 2) Token "rubato"/elevation: processo SYSTEM il cui padre e' un utente
        #    standard. Razionale: ottenere SYSTEM richiede un servizio o un exploit;
        #    un processo SYSTEM generato da un processo utente e' altamente sospetto.
        #    (Possibili eccezioni legittime: alcuni servizi/helper -> verificare.)
        if ($isSys -and $parentAlive -and $ownerByPid.ContainsKey($ppid)) {
            $parentSid = $ownerByPid[$ppid]
            if (Test-RealUser $parentSid) {
                $flags += New-Flag 'alto' "Processo SYSTEM con padre di utente standard (PID $ppid) - possibile token elevation/stealing"
            }
        }
    }
    else {
        # Token non accessibile: atteso per processi di sistema/protetti o di altri
        # utenti se non si e' elevati. NON e' di per se' un'anomalia.
        $tokenErr = $tok.Error
    }

    # ---- Severita' complessiva --------------------------------------------
    $severity = 'ok'
    foreach ($f in $flags) { if ($SevRank[$f.Severity] -gt $SevRank[$severity]) { $severity = $f.Severity } }

    $report += [pscustomobject]@{
        PID          = $procId
        Name         = $p.Name
        PPID         = $ppid
        ParentAlive  = $parentAlive
        OwnerSid     = $ownerSid
        Owner        = if ($ownerAccount) { $ownerAccount } elseif ($ownerSid) { $ownerSid } else { '(n/d)' }
        Session      = $p.SessionId
        Integrity    = if ($integrity) { $integrity } else { '-' }
        TokenType    = if ($tokenType) { $tokenType } else { '-' }
        Elevated     = $elevated
        Threads      = $p.ThreadCount
        Handles      = $p.HandleCount
        AgeMin       = $ageMin
        Path         = $p.ExecutablePath
        TokenError   = $tokenErr
        EnabledPrivs = if ($tok.Success) { ($tok.EnabledPrivileges -join ',') } else { '' }
        Severity     = $severity
        Flags        = $flags
    }
}

# ============================================================================
# 6) OUTPUT
# ============================================================================
$order = @{ 'alto' = 0; 'sospetto' = 1; 'info' = 2; 'ok' = 3 }
$sorted = $report | Sort-Object @{ Expression = { $order[$_.Severity] } }, Name

$nHigh = ($report | Where-Object Severity -eq 'alto').Count
$nSusp = ($report | Where-Object Severity -eq 'sospetto').Count
$nInfo = ($report | Where-Object Severity -eq 'info').Count
$nInacc = ($report | Where-Object { $_.TokenError -ne '' }).Count

Write-Output ""
Write-Output ('=' * 70)
Write-Output "  Anomalie rilevate (raggruppate per severita')"
Write-Output ('=' * 70)

$flagged = $sorted | Where-Object { $_.Severity -ne 'ok' }
if (-not $flagged) {
    Write-Output "  Nessuna anomalia rilevata dai controlli attuali."
}
else {
    foreach ($sev in @('alto', 'sospetto', 'info')) {
        $group = $flagged | Where-Object Severity -eq $sev
        if (-not $group) { continue }
        Write-Output ""
        Write-Output ("--- {0} ({1}) ---" -f $sev.ToUpper(), $group.Count)
        foreach ($d in $group) {
            Write-Output ("  PID {0,-6} {1,-28} owner: {2}  [{3}]" -f $d.PID, $d.Name, $d.Owner, $d.Integrity)
            foreach ($f in $d.Flags) {
                Write-Output ("        - ({0}) {1}" -f $f.Severity, $f.Reason)
            }
        }
    }
}

# Tabella riassuntiva dei processi con almeno un flag.
Write-Output ""
Write-Output ('=' * 70)
Write-Output "  Riepilogo processi segnalati"
Write-Output ('=' * 70)
if ($flagged) {
    $flagged |
        Select-Object @{N = 'Sev'; E = { $_.Severity.ToUpper() } }, PID, Name, Owner, Integrity,
            TokenType, @{N = 'Thr'; E = { $_.Threads } }, @{N = 'PPID'; E = { $_.PPID } },
            @{N = 'PadreVivo'; E = { $_.ParentAlive } } |
        Format-Table -AutoSize | Out-String -Width 300 | Write-Output
}

Write-Output ('=' * 70)
Write-Output "  Esito"
Write-Output ('=' * 70)
Write-Output ("  Processi analizzati        : {0}" -f $report.Count)
Write-Output ("  Alto rischio               : {0}" -f $nHigh)
Write-Output ("  Sospetti                   : {0}" -f $nSusp)
Write-Output ("  Info                       : {0}" -f $nInfo)
Write-Output ("  Token non accessibili      : {0} (atteso senza elevazione / processi protetti)" -f $nInacc)
if ($nHigh -gt 0) {
    Write-Output "  >> Verificare i processi ad ALTO rischio: possibile injection/elevation (cheat/bypass)."
}
elseif ($nSusp -gt 0) {
    Write-Output "  >> Nessun alto rischio, ma alcuni processi meritano una verifica manuale."
}
else {
    Write-Output "  >> Nessuna anomalia evidente. (Non e' una garanzia assoluta; ripetere elevati.)"
}

# ---- 6) Export JSON opzionale ---------------------------------------------
if ($JsonPath) {
    try {
        $export = [pscustomobject]@{
            generatedUtc  = (Get-Date).ToUniversalTime().ToString('o')
            host          = $env:COMPUTERNAME
            elevated      = $isAdmin
            processCount  = $report.Count
            highRisk      = $nHigh
            suspect       = $nSusp
            info          = $nInfo
            inaccessible  = $nInacc
            processes     = $report
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
