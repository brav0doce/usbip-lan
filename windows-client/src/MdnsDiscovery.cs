using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Makaretu.Dns;
using UsbIpClientApp.Models;

namespace UsbIpClientApp
{
    /// <summary>
    /// Discovers USB/IP servers on the local network using two methods:
    ///   1. mDNS/DNS-SD  – "_usbip._tcp" service type (instant, zero-config)
    ///   2. TCP port scan – scans subnet on port 3240 as fallback
    /// </summary>
    public class MdnsDiscovery : IDisposable
    {
        public const int UsbIpPort = 3240;
        private static readonly string[] ServiceTypes = { "_usbip._tcp", "_usbip._tcp.local" };

        private MulticastService?     _multicast;
        private ServiceDiscovery?     _sd;
        private bool _disposed;

        public event EventHandler<UsbIpServer>? ServerFound;
        public event EventHandler<string>?      ServerLost;

        // ─────────────────────────────────────────────────────────────────────
        // mDNS discovery
        // ─────────────────────────────────────────────────────────────────────

        public void StartMdns()
        {
            StopMdns();

            _multicast = new MulticastService();
            _sd        = new ServiceDiscovery(_multicast);

            _sd.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
            _sd.ServiceInstanceShutdown  += OnServiceInstanceShutdown;

            _multicast.Start();
            foreach (var serviceType in ServiceTypes)
            {
                _sd.QueryServiceInstances(serviceType);
            }
        }

        public void StopMdns()
        {
            _sd?.Dispose();
            _multicast?.Stop();
            _multicast?.Dispose();
            _sd = null;
            _multicast = null;
        }

        private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
        {
            var name = e.ServiceInstanceName.ToString();

            int port = UsbIpPort;
            foreach (var srv in e.Message.AdditionalRecords.OfType<SRVRecord>())
            {
                port = srv.Port;
                break;
            }

            // Look for A/AAAA records in all sections because some stacks don't
            // place them in AdditionalRecords.
            var addressRecords = e.Message.Answers
                .Concat(e.Message.AuthorityRecords)
                .Concat(e.Message.AdditionalRecords)
                .OfType<AddressRecord>()
                .DistinctBy(r => r.Address.ToString());

            foreach (var addressRecord in addressRecords)
            {
                if (addressRecord.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var server = new UsbIpServer
                {
                    IpAddress = addressRecord.Address.ToString(),
                    Port      = port,
                    Hostname  = name
                };
                ServerFound?.Invoke(this, server);
            }
        }

        private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
        {
            ServerLost?.Invoke(this, e.ServiceInstanceName.ToString());
        }

        // ─────────────────────────────────────────────────────────────────────
        // Fallback: TCP port scan of local subnet
        // ─────────────────────────────────────────────────────────────────────

        public async Task ScanSubnetAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var localIp = GetLocalIpAddress();
            if (localIp == null) return;

            var subnet = GetSubnetBase(localIp);
            progress?.Report($"Escaneando subred {subnet}.0/24 ...");

            var tasks = Enumerable.Range(1, 254)
                .Select(i => ProbeHostAsync($"{subnet}.{i}", cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);
        }

        private async Task ProbeHostAsync(string ip, CancellationToken ct)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.SendTimeout    = 500;
                tcp.ReceiveTimeout = 500;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(500);

                await tcp.ConnectAsync(ip, UsbIpPort, cts.Token);

                // Connected – this might be a USB/IP server
                var server = new UsbIpServer
                {
                    IpAddress = ip,
                    Port      = UsbIpPort,
                    Hostname  = ip
                };
                ServerFound?.Invoke(this, server);
            }
            catch
            {
                // Not reachable or refused – ignore
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static IPAddress? GetLocalIpAddress()
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address;
                }
            }
            return null;
        }

        private static string GetSubnetBase(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopMdns();
        }
    }
}
