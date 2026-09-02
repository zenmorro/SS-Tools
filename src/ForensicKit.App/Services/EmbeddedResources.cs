using System.IO;
using System.Reflection;
using ForensicKit.Core.Models;

namespace ForensicKit.App.Services;

/// <summary>
/// Reads resources embedded in the application assembly: the bundled catalog and the
/// PowerShell scripts. Keeping the scripts embedded (rather than downloaded) is a
/// deliberate security choice — their contents cannot be tampered with at runtime.
/// </summary>
public static class EmbeddedResources
{
    private static readonly Assembly Asm = typeof(EmbeddedResources).Assembly;

    /// <summary>Returns the bundled tools.json shipped inside the app.</summary>
    public static string GetBundledManifestJson()
    {
        using var stream = Asm.GetManifestResourceStream("ForensicKit.App.tools.json")
            ?? throw new InvalidOperationException("Bundled tools.json resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Returns the raw text of an embedded script by resource name.</summary>
    public static string GetScriptContent(string resourceName)
    {
        using var stream = Asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded script '{resourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The catalog of embedded scripts exposed in the "Script PowerShell" section.
    /// Resource names follow the "ForensicKit.Scripts.&lt;file&gt;.ps1" convention set in the csproj.
    /// </summary>
    public static IReadOnlyList<ScriptDefinition> GetScriptCatalog() => new List<ScriptDefinition>
    {
        new()
        {
            Id = "system-info",
            Name = "Raccolta informazioni di sistema",
            Description = "Snapshot di OS, hardware, dischi, rete e utenti collegati. Sola lettura.",
            ResourceName = "ForensicKit.Scripts.Collect-SystemInfo.ps1",
            RequiresElevation = false
        },
        new()
        {
            Id = "suspicious-processes",
            Name = "Processi e servizi sospetti",
            Description = "Evidenzia processi in percorsi insoliti, immagini non firmate e servizi auto-start non Microsoft.",
            ResourceName = "ForensicKit.Scripts.List-SuspiciousProcesses.ps1",
            RequiresElevation = false
        },
        new()
        {
            Id = "usb-history",
            Name = "Storico dispositivi USB",
            Description = "Recupera dalla chiave USBSTOR i dispositivi di archiviazione USB collegati storicamente.",
            ResourceName = "ForensicKit.Scripts.Get-UsbDeviceHistory.ps1",
            RequiresElevation = false
        },
        new()
        {
            Id = "security-events",
            Name = "Eventi di sicurezza recenti",
            Description = "Dump di logon/logon falliti/lockout e nuovi servizi. Richiede elevazione per risultati completi.",
            ResourceName = "ForensicKit.Scripts.Get-RecentSecurityEvents.ps1",
            RequiresElevation = true
        },
        new()
        {
            Id = "dma-detect",
            Name = "Rilevamento DMA card (PCILeech / PCIeScreamer)",
            Description = "Analizza i dispositivi PCIe, li confronta con pci.ids e una blocklist e verifica la protezione DMA (VT-d/IOMMU) per rilevare possibili schede FPGA per attacchi DMA.",
            ResourceName = "ForensicKit.Scripts.Detect-DmaDevices.ps1",
            RequiresElevation = false
        },
        new()
        {
            Id = "process-tokens",
            Name = "Verifica token/permessi processi (anticheat)",
            Description = "Apre i token dei processi e verifica Integrity Level, privilegi, tipo token, sessione e SID owner; rileva processi in stato anomalo (thread 0, orfani) e possibili token elevation/injection.",
            ResourceName = "ForensicKit.Scripts.Test-ProcessTokens.ps1",
            RequiresElevation = false
        },
        new()
        {
            Id = "scriptz",
            Name = "ScriptZ - Anti-Forensic Hunter",
            Description = "Raccoglie indicatori di attivita' anti-forense (log cancellati, modifica orario, USN Journal, USB, Zone.Identifier/MOTW, prefetch, restart servizi) e mostra tutto in una tabella a schermo.",
            ResourceName = "ForensicKit.Scripts.AntiForensicHunter.ps1",
            RequiresElevation = true
        }
    };
}
