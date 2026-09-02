using System.Windows;
using System.Windows.Threading;
using ForensicKit.App.Services;
using ForensicKit.App.ViewModels;
using ForensicKit.App.Views;
using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Appearance;

namespace ForensicKit.App;

public partial class App : Application
{
    private IServiceProvider _services = null!;

    public IServiceProvider Services => _services;
    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var paths = new AppPaths();
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton(paths);
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHttpClientFactoryLite, HttpClientFactoryLite>();

        // Core services
        services.AddSingleton<IHashService, HashService>();
        services.AddSingleton<ISignatureService, SignatureService>();
        services.AddSingleton<IExtractionService, ExtractionService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IGitHubReleaseService, GitHubReleaseService>();
        services.AddSingleton<IExecutionService, ExecutionService>();
        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<IToolInstallService, ToolInstallService>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddSingleton<IManifestService>(sp => new ManifestService(
            sp.GetRequiredService<AppPaths>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IHttpClientFactoryLite>(),
            EmbeddedResources.GetBundledManifestJson));

        // UI services
        services.AddSingleton<IDialogService, DialogService>();

        // View models
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ScriptsViewModel>();
        services.AddSingleton<AntiForensicsViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Pages (constructed by the NavigationView via the service provider)
        services.AddTransient<DashboardPage>();
        services.AddTransient<ScriptsPage>();
        services.AddTransient<AntiForensicsPage>();
        services.AddTransient<LogsPage>();
        services.AddTransient<SettingsPage>();

        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        // Load settings synchronously before the UI comes up so theme/proxy apply.
        var settings = _services.GetRequiredService<ISettingsService>();
        settings.LoadAsync().GetAwaiter().GetResult();

        var theme = string.Equals(settings.Current.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme);

        // Apply the accent color: user override if set, otherwise the IMPERO purple.
        var accentHex = string.IsNullOrWhiteSpace(settings.Current.AccentColor)
            ? "#7B39ED"
            : settings.Current.AccentColor;
        try
        {
            var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentHex);
            ApplicationAccentColorManager.Apply(accent, theme);
        }
        catch
        {
            // Ignore an invalid custom accent and keep the theme default.
        }

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Si è verificato un errore imprevisto:\n\n{e.Exception.Message}",
            "ForensicKit", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
