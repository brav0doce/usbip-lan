using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UsbIpClientApp.Models;

namespace UsbIpClientApp
{
    /// <summary>
    /// Implements the client side of the USB/IP protocol (Linux kernel spec).
    ///
    /// Workflow:
    ///   1. Connect to the server (TCP port 3240).
    ///   2. Send OP_REQ_DEVLIST → receive device list.
    ///   3. Send OP_REQ_IMPORT  → server exports a device.
    ///   4. The OS-level binding (kernel driver) is delegated to usbip-win2 CLI.
    ///
    /// For attach/detach this class invokes the usbip.exe tool from usbip-win2
    /// so we benefit from its kernel driver without re-implementing it.
    /// Reference: https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html
    /// </summary>
    public class UsbIpClient : IDisposable
    {
        private static string? _usbipExePath;

        private bool _disposed;

        // ─────────────────────────────────────────────────────────────────────
        // Device list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Queries the server using usbip-win2 and parses the exported device list.
        /// </summary>
        public async Task<List<UsbDevice>> GetDeviceListAsync(
            UsbIpServer server,
            CancellationToken ct = default)
        {
            var usbipExe = ResolveUsbipExePath();
            if (usbipExe == null)
                throw new InvalidOperationException("usbip.exe no encontrado. Instala usbip-win2 primero.");

            var output = await RunUsbipCaptureAsync(usbipExe, $"list -r {server.IpAddress}", ct);
            return ParseUsbipListOutput(output, server);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Attach / Detach via usbip-win2 CLI
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attaches a remote USB device using the usbip-win2 CLI tool.
        /// The kernel driver handles the actual URB forwarding.
        /// </summary>
        public async Task<(bool Success, string Output)> AttachAsync(
            UsbDevice device,
            CancellationToken ct = default)
        {
            if (device.Server == null)
                return (false, "El dispositivo no tiene un servidor asociado.");

            var usbipExe = ResolveUsbipExePath();
            if (usbipExe == null)
                return (false, "usbip.exe no encontrado. Instala usbip-win2 desde Install.ps1 o el instalador oficial.");

            // Use the command format documented by usbip-win2.
            var args = $"attach -r {device.Server.IpAddress} -b {device.BusId}";
            return await RunUsbipAsync(usbipExe, args, ct);
        }

        /// <summary>
        /// Detaches a previously attached USB device.
        /// </summary>
        public async Task<(bool Success, string Output)> DetachAsync(
            int portNumber,
            CancellationToken ct = default)
        {
            var usbipExe = ResolveUsbipExePath();
            if (usbipExe == null)
                return (false, "usbip.exe no encontrado. Instala usbip-win2 primero.");

            var args = $"detach -p {portNumber}";
            return await RunUsbipAsync(usbipExe, args, ct);
        }

        /// <summary>
        /// Lists currently attached USB/IP ports.
        /// </summary>
        public async Task<(bool Success, string Output)> ListPortsAsync(
            CancellationToken ct = default)
        {
            var usbipExe = ResolveUsbipExePath();
            if (usbipExe == null)
                return (false, "usbip.exe no encontrado. Instala usbip-win2 primero.");

            return await RunUsbipAsync(usbipExe, "port", ct);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private static async Task<(bool, string)> RunUsbipAsync(
            string usbipExe,
            string args,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = usbipExe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var sb = new StringBuilder();

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode == 0, sb.ToString().Trim());
        }

        private static async Task<string> RunUsbipCaptureAsync(
            string usbipExe,
            string args,
            CancellationToken ct)
        {
            var (ok, output) = await RunUsbipAsync(usbipExe, args, ct);
            if (!ok && string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("No se pudo ejecutar usbip.exe.");

            return output;
        }

        private static string? ResolveUsbipExePath()
        {
            if (_usbipExePath != null && File.Exists(_usbipExePath))
                return _usbipExePath;

            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usbip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip", "usbip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "usbip-win2", "usbip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "USBip", "usbip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "usbip-win2", "usbip.exe")
            };

            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(entry.Trim(), "usbip.exe"));
            }

            foreach (var candidate in candidates.Where(File.Exists))
            {
                _usbipExePath = candidate;
                return candidate;
            }

            // If execution reaches here and the app runs under a shell where usbip is resolvable
            // only by command name, let ProcessStartInfo resolve it.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _usbipExePath = "usbip.exe";
                return _usbipExePath;
            }

            return null;
        }

        private static List<UsbDevice> ParseUsbipListOutput(string output, UsbIpServer server)
        {
            var devices = new List<UsbDevice>();
            UsbDevice? current = null;

            var deviceLine = new Regex(@"^\s*(?<busid>[0-9]+(?:[\-.][0-9]+)*):\s*(?<vendor>.*?)\s*:\s*(?<product>.*?)\s*\((?<vid>[0-9A-Fa-f]{4}):(?<pid>[0-9A-Fa-f]{4})\)\s*$", RegexOptions.Compiled);
            var pathLine = new Regex(@"^\s*:\s*(?<path>.+?)\s*$", RegexOptions.Compiled);
            var classLine = new Regex(@"^\s*:\s*\((?<label>.+?)\)\s*\((?<cls>[0-9A-Fa-f]{2})/(?<sub>[0-9A-Fa-f]{2})/(?<proto>[0-9A-Fa-f]{2})\)\s*$", RegexOptions.Compiled);

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    current = null;
                    continue;
                }

                var deviceMatch = deviceLine.Match(line);
                if (deviceMatch.Success)
                {
                    try {
                        current = new UsbDevice
                        {
                            BusId = deviceMatch.Groups["busid"].Value.Trim(),
                            VendorId = ushort.Parse(deviceMatch.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                            ProductId = ushort.Parse(deviceMatch.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                            Server = server
                        };
                        devices.Add(current);
                    } catch (Exception) { current = null; }
                    continue;
                }

                if (current == null)
                    continue;

                var pathMatch = pathLine.Match(line);
                if (pathMatch.Success && string.IsNullOrWhiteSpace(current.Path))
                {
                    current.Path = pathMatch.Groups["path"].Value.Trim();
                    continue;
                }

                var classMatch = classLine.Match(line);
                if (classMatch.Success)
                {
                    current.DeviceClass = byte.Parse(classMatch.Groups["cls"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    current.DeviceSubClass = byte.Parse(classMatch.Groups["sub"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    current.DeviceProtocol = byte.Parse(classMatch.Groups["proto"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }

            return devices;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
