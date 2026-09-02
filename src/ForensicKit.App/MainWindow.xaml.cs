using System.Windows.Controls;
using ForensicKit.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace ForensicKit.App;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, Page> _cache = new();

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();

        Loaded += (_, _) => NavDashboard.IsChecked = true;
    }

    private void Nav_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string key })
            return;

        if (!_cache.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "dashboard" => _services.GetRequiredService<DashboardPage>(),
                "scripts" => _services.GetRequiredService<ScriptsPage>(),
                "timeline" => _services.GetRequiredService<AntiForensicsPage>(),
                "logs" => _services.GetRequiredService<LogsPage>(),
                "settings" => _services.GetRequiredService<SettingsPage>(),
                _ => _services.GetRequiredService<DashboardPage>()
            };
            _cache[key] = page;
        }

        ContentFrame.Navigate(page);
    }
}
