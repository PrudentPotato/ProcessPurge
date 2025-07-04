// SettingsWindow.xaml.cs
using System.Windows;

namespace ProcessPurge
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;

            // Load current settings into checkboxes
            StartWithWindowsCheck.IsChecked = currentSettings.StartWithWindows;
            MinimizeToTrayCheck.IsChecked = currentSettings.MinimizeToTrayOnClose;
            PoliteKillCheck.IsChecked = currentSettings.PoliteKill;
            BlockCriticalCheck.IsChecked = currentSettings.BlockCriticalProcesses; // <-- ADD THIS
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}