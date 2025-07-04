// EulaWindow.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace ProcessPurge
{
    public partial class EulaWindow : Window
    {
        public EulaWindow()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}