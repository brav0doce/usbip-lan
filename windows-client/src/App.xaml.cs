using System.Windows;

namespace UsbIpClientApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handler
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show(
                    $"Error inesperado:\n{args.Exception.Message}",
                    "USB/IP Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
