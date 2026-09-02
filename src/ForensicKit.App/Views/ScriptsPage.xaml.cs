using System.Windows.Controls;
using ForensicKit.App.ViewModels;

namespace ForensicKit.App.Views;

public partial class ScriptsPage : Page
{
    public ScriptsPage(ScriptsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        // Auto-scroll the console to the newest output.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScriptsViewModel.ConsoleOutput))
                OutputScroll.ScrollToEnd();
        };
    }
}
