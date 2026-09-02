using System.Text;
using System.Windows;
using ForensicKit.App.Views;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;

namespace ForensicKit.App.Services;

public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);
    bool Confirm(string title, string message);

    /// <summary>
    /// Shows the pre-launch trust dialog (source, hash, homepage, signature). Returns
    /// true if the user chooses to proceed.
    /// </summary>
    bool ConfirmToolTrust(ToolEntry tool, ToolInstallState? state, SignatureInfo signature);

    /// <summary>Prompts for custom arguments before launch; returns null if cancelled.</summary>
    string? PromptForArguments(string toolName, string defaultArgs);

    string? PickFolder(string description);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current.MainWindow;

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public bool ConfirmToolTrust(ToolEntry tool, ToolInstallState? state, SignatureInfo signature)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Stai per eseguire un tool di terze parti: {tool.Name}");
        sb.AppendLine($"Autore: {tool.Author}");
        sb.AppendLine();
        sb.AppendLine($"Fonte ufficiale: {tool.Homepage ?? "(non specificata)"}");
        sb.AppendLine($"Origine download: {state?.SourceUrl ?? tool.DownloadUrl ?? "(GitHub release)"}");
        sb.AppendLine();
        sb.AppendLine("Integrità:");
        sb.AppendLine($"  SHA-256 archivio: {state?.ArchiveSha256 ?? "(sconosciuto)"}");
        if (!string.IsNullOrWhiteSpace(tool.Sha256))
            sb.AppendLine($"  SHA-256 atteso:   {tool.Sha256}");
        sb.AppendLine();
        sb.AppendLine("Firma digitale dell'eseguibile:");
        sb.AppendLine($"  {signature.StatusMessage}");
        if (signature.IsSigned)
        {
            sb.AppendLine($"  Firmatario: {signature.Subject}");
            sb.AppendLine($"  Emittente:  {signature.Issuer}");
        }
        sb.AppendLine();
        if (tool.RequiresElevation)
            sb.AppendLine("⚠ Questo tool verrà avviato con elevazione (UAC).");
        sb.AppendLine();
        sb.AppendLine("ForensicKit non disabilita SmartScreen/Defender e non modifica le protezioni di sistema.");
        sb.AppendLine("Procedere con l'esecuzione?");

        return MessageBox.Show(Owner!, sb.ToString(), $"Conferma esecuzione — {tool.Name}",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public string? PromptForArguments(string toolName, string defaultArgs)
    {
        var dlg = new InputDialog(
            $"Argomenti per {toolName}",
            "Modifica gli argomenti da passare al tool (lascia vuoto per nessuno):",
            defaultArgs)
        {
            Owner = Owner
        };
        return dlg.ShowDialog() == true ? dlg.ResponseText : null;
    }

    public string? PickFolder(string description)
    {
        // Uses the WinForms folder browser to avoid an extra dependency.
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}
