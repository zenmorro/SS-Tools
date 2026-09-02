using System.Windows.Controls;
using ForensicKit.App.ViewModels;

namespace ForensicKit.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
