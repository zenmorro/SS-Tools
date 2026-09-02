// WinForms is enabled only for the folder-browser dialog. Alias the handful of type
// names that clash between System.Windows (WPF) and System.Windows.Forms so the rest
// of the app can use the WPF types without fully qualifying them everywhere.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Color = System.Windows.Media.Color;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using RadioButton = System.Windows.Controls.RadioButton;
