using System.Windows;

namespace ForensicKit.App.Views;

public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ResponseBox.Text = defaultValue;
        Loaded += (_, _) =>
        {
            ResponseBox.Focus();
            ResponseBox.SelectAll();
        };
    }

    public string ResponseText => ResponseBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
