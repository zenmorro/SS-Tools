using System.Windows.Controls;
using ForensicKit.App.ViewModels;

namespace ForensicKit.App.Views;

public partial class LogsPage : Page
{
    private readonly LogsViewModel _viewModel;

    public LogsPage(LogsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
}
