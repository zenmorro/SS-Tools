using System.Windows.Controls;
using ForensicKit.App.ViewModels;

namespace ForensicKit.App.Views;

public partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;
    private bool _initialized;

    public DashboardPage(DashboardViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            await _viewModel.InitializeAsync();
        };
    }
}
