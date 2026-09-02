using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForensicKit.App.Services;
using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Services;
using Wpf.Ui.Appearance;

namespace ForensicKit.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly AppPaths _paths;

    public SettingsViewModel(ISettingsService settings, IDialogService dialog, AppPaths paths)
    {
        _settings = settings;
        _dialog = dialog;
        _paths = paths;

        var s = settings.Current;
        _downloadRoot = string.IsNullOrWhiteSpace(s.DownloadRoot) ? paths.DefaultToolsRoot : s.DownloadRoot;
        _manifestUrl = s.ManifestUrl;
        _autoUpdateCatalog = s.AutoUpdateCatalog;
        _proxyUrl = s.ProxyUrl;
        _accentColor = s.AccentColor;
        _isDarkTheme = !string.Equals(s.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _rememberTrust = s.RememberTrustAcknowledgement;
    }

    [ObservableProperty] private string _downloadRoot;
    [ObservableProperty] private string _manifestUrl;
    [ObservableProperty] private bool _autoUpdateCatalog;
    [ObservableProperty] private string _proxyUrl;
    [ObservableProperty] private string _accentColor;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _rememberTrust;
    [ObservableProperty] private string _saveStatus = "";

    public string AppDataFolder => _paths.Root;

    [RelayCommand]
    private void BrowseDownloadFolder()
    {
        var picked = _dialog.PickFolder("Seleziona la cartella di destinazione dei download");
        if (!string.IsNullOrWhiteSpace(picked))
            DownloadRoot = picked;
    }

    [RelayCommand]
    private void OpenAppDataFolder()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.Root}\"") { UseShellExecute = true });
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplicationThemeManager.Apply(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _settings.Current;
        s.DownloadRoot = DownloadRoot;
        s.ManifestUrl = ManifestUrl;
        s.AutoUpdateCatalog = AutoUpdateCatalog;
        s.ProxyUrl = ProxyUrl;
        s.AccentColor = AccentColor;
        s.Theme = IsDarkTheme ? "Dark" : "Light";
        s.RememberTrustAcknowledgement = RememberTrust;

        await _settings.SaveAsync();
        SaveStatus = $"Impostazioni salvate alle {DateTime.Now:HH:mm:ss}.";
    }
}
