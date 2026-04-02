using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using UsbIpClientApp.Models;

namespace UsbIpClientApp
{
    /// <summary>
    /// Main application window: auto-discovers USB/IP servers on the LAN,
    /// lists their exported devices and allows the user to attach/detach them.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<UsbIpServer> _servers = new();
        private readonly ObservableCollection<UsbDevice>   _devices = new();
        private readonly MdnsDiscovery _discovery = new();
        private readonly UsbIpClient   _client    = new();
        private UsbIpServer? _selectedServer;
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();

            LstServers.ItemsSource = _servers;
            LstDevices.ItemsSource = _devices;

            TxtVersion.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";

            // Hook mDNS events
            _discovery.ServerFound += OnServerFound;
            _discovery.ServerLost  += OnServerLost;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Start mDNS discovery immediately on launch
            StartAutoDiscovery();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _discovery.Dispose();
            _client.Dispose();
            base.OnClosed(e);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Auto-discovery
        // ─────────────────────────────────────────────────────────────────────

        private void StartAutoDiscovery()
        {
            SetStatus("Buscando servidores USB/IP en la red …");
            try
            {
                _discovery.StartMdns();
            }
            catch (Exception ex)
            {
                SetStatus($"mDNS no disponible ({ex.Message}). Usa 'Buscar Servidores' para escanear manualmente.");
            }
        }

        private void OnServerFound(object? sender, UsbIpServer server)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var existing = _servers.FirstOrDefault(s => s.IpAddress == server.IpAddress);
                if (existing == null)
                {
                    _servers.Add(server);
                    if (_selectedServer == null)
                    {
                        LstServers.SelectedItem = server;
                    }
                    SetStatus($"Servidor encontrado: {server}");
                    return;
                }

                existing.Port = server.Port;
                if (!string.IsNullOrWhiteSpace(server.Hostname))
                    existing.Hostname = server.Hostname;

                if (_selectedServer?.IpAddress == existing.IpAddress)
                {
                    TxtServerInfo.Text = $"Servidor: {existing.IpAddress}:{existing.Port}";
                }

                if (_selectedServer == null && LstServers.SelectedItem == null)
                {
                    LstServers.SelectedItem = existing;
                }
            });
        }

        private void OnServerLost(object? sender, string hostname)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var normalized = hostname.TrimEnd('.');
                var s = _servers.FirstOrDefault(x =>
                    x.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase) ||
                    x.Hostname.TrimEnd('.').Equals(normalized, StringComparison.OrdinalIgnoreCase));
                if (s == null)
                    return;

                _servers.Remove(s);

                if (_selectedServer?.IpAddress == s.IpAddress)
                {
                    _selectedServer = null;
                    _devices.Clear();
                    TxtServerInfo.Text = "Selecciona un servidor de la lista";
                    UpdateButtonState();
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button handlers
        // ─────────────────────────────────────────────────────────────────────

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            BtnScan.IsEnabled = false;
            ShowProgress(true);
            SetStatus("Escaneando la red LAN …");
            _servers.Clear();
            _devices.Clear();
            _selectedServer = null;
            TxtServerInfo.Text = "Selecciona un servidor de la lista";
            UpdateButtonState();

            try
            {
                // mDNS
                _discovery.StopMdns();
                _discovery.StartMdns();

                // TCP subnet scan (parallel)
                await _discovery.ScanSubnetAsync(
                    new Progress<string>(SetStatus),
                    _cts.Token);

                SetStatus($"Búsqueda completada – {_servers.Count} servidor(es) encontrado(s).");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Búsqueda cancelada.");
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                ShowProgress(false);
                BtnScan.IsEnabled = true;
            }
        }

        private async void BtnListDevices_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedServer == null)
            {
                MessageBox.Show("Selecciona primero un servidor.", "USB/IP Client",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await RefreshDevicesAsync(_selectedServer);
        }

        private void LstServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _selectedServer = LstServers.SelectedItem as UsbIpServer;
            if (_selectedServer != null)
            {
                TxtServerInfo.Text = $"Servidor: {_selectedServer.IpAddress}:{_selectedServer.Port}";
                _ = RefreshDevicesAsync(_selectedServer);
            }
            UpdateButtonState();
        }

        private void LstDevices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateButtonState();
        }

        private async void BtnAttach_Click(object sender, RoutedEventArgs e)
        {
            if (LstDevices.SelectedItem is not UsbDevice dev) return;

            ShowProgress(true);
            SetStatus($"Conectando {dev.BusId} …");

            var (ok, output) = await _client.AttachAsync(dev);
            if (ok)
            {
                dev.IsAttached = true;
                RefreshList();
                SetStatus($"Dispositivo {dev.BusId} conectado correctamente.");
            }
            else
            {
                SetStatus($"Error al conectar: {output}");
                MessageBox.Show(output, "Error al conectar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ShowProgress(false);
            UpdateButtonState();
        }

        private async void BtnDetach_Click(object sender, RoutedEventArgs e)
        {
            if (LstDevices.SelectedItem is not UsbDevice dev || !dev.IsAttached) return;

            // Query active ports to find the correct port number to detach
            var (ok, portsOutput) = await _client.ListPortsAsync();
            int portNumber = -1;
            if (ok && !string.IsNullOrWhiteSpace(portsOutput))
            {
                // Parse port number from usbip port output (lines like "Port 00: ... busid N-N ...")
                foreach (var line in portsOutput.Split('\n'))
                {
                    if (line.Contains(dev.BusId) || line.Contains($"{dev.Server?.IpAddress}"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"Port\s+(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var pn))
                        {
                            portNumber = pn;
                            break;
                        }
                    }
                }
            }

            if (portNumber < 0)
            {
                MessageBox.Show(
                    "No se encontró el puerto USB/IP activo para este dispositivo.\n" +
                    "Usa 'Puertos activos' para ver los puertos y desconecta manualmente con:\n" +
                    "  usbip detach -p <N>",
                    "Puerto no encontrado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var (detachOk, output) = await _client.DetachAsync(portNumber);
            if (detachOk)
            {
                dev.IsAttached = false;
                RefreshList();
                SetStatus($"Dispositivo {dev.BusId} desconectado (puerto {portNumber}).");
            }
            else
            {
                SetStatus($"Error al desconectar: {output}");
                MessageBox.Show(output, "Error al desconectar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateButtonState();
        }

        private async void BtnListPorts_Click(object sender, RoutedEventArgs e)
        {
            var (ok, output) = await _client.ListPortsAsync();
            MessageBox.Show(
                string.IsNullOrWhiteSpace(output) ? "No hay puertos USB/IP activos." : output,
                "Puertos USB/IP activos",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void BtnAddManual_Click(object sender, RoutedEventArgs e)
        {
            var ip = TxtManualIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("Introduce una dirección IP.", "USB/IP Client",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_servers.Any(s => s.IpAddress == ip))
            {
                SetStatus($"El servidor {ip} ya está en la lista.");
                return;
            }

            var server = new UsbIpServer { IpAddress = ip, Hostname = ip };
            _servers.Add(server);
            LstServers.SelectedItem = server;
            TxtManualIp.Clear();
            SetStatus($"Servidor añadido manualmente: {ip}");
        }

        private void BtnInstallDriver_Click(object sender, RoutedEventArgs e)
        {
            var installScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Install.ps1");

            if (!File.Exists(installScript))
            {
                MessageBox.Show(
                    "No se encontró Install.ps1 junto al ejecutable.\n" +
                    "Ejecuta el instalador distribuido en release/windows o instala usbip-win2 manualmente.",
                    "Driver no encontrado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments       = $"-ExecutionPolicy Bypass -File \"{installScript}\"",
                Verb            = "runas",   // request UAC elevation
                UseShellExecute = true
            };
            try { Process.Start(psi); }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al instalar driver: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private async Task RefreshDevicesAsync(UsbIpServer server)
        {
            _devices.Clear();
            BtnListDevices.IsEnabled = false;
            ShowProgress(true);
            SetStatus($"Obteniendo dispositivos de {server.IpAddress} …");

            try
            {
                var devices = await _client.GetDeviceListAsync(server);
                server.DeviceCount = devices.Count;
                foreach (var d in devices)
                    _devices.Add(d);

                if (_selectedServer?.IpAddress == server.IpAddress)
                {
                    TxtServerInfo.Text = $"Servidor: {server.IpAddress}:{server.Port} · {devices.Count} dispositivo(s)";
                }

                SetStatus($"{devices.Count} dispositivo(s) exportado(s) por {server.IpAddress}.");
            }
            catch (Exception ex)
            {
                SetStatus($"No se pudo conectar a {server.IpAddress}: {ex.Message}");
            }
            finally
            {
                ShowProgress(false);
                BtnListDevices.IsEnabled = true;
                UpdateButtonState();
            }
        }

        private void UpdateButtonState()
        {
            var dev = LstDevices.SelectedItem as UsbDevice;
            BtnAttach.IsEnabled = dev != null && !dev.IsAttached;
            BtnDetach.IsEnabled = dev != null && dev.IsAttached;
        }

        private void SetStatus(string message)
        {
            if (!Dispatcher.CheckAccess())
                Dispatcher.InvokeAsync(() => TxtStatus.Text = message);
            else
                TxtStatus.Text = message;
        }

        private void ShowProgress(bool visible)
        {
            ProgressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshList()
        {
            var items = _devices.ToList();
            _devices.Clear();
            foreach (var item in items) _devices.Add(item);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Value converters used in XAML
    // ─────────────────────────────────────────────────────────────────────────

    public class BoolToAttachedConverter : IValueConverter
    {
        public static readonly BoolToAttachedConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? "Conectado" : "Disponible";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))   // green
                : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));  // grey

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
