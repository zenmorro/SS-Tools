using System.Windows.Controls;
using ForensicKit.App.ViewModels;

namespace ForensicKit.App.Views;

public partial class AntiForensicsPage : Page
{
    public AntiForensicsPage(AntiForensicsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
