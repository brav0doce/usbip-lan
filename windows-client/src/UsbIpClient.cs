using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Runtime.InteropServices;
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
        private const short UsbIpVersion = 0x0111;
        private const short OP_REQ_DEVLIST = unchecked((short)0x8005);
        private const short OP_REP_DEVLIST = 0x0005;
        private const short OP_REQ_IMPORT  = unchecked((short)0x8003);
        private const short OP_REP_IMPORT  = 0x0003;
        private const int   ST_OK = 0;

        private static string? _usbipExePath;

        private bool _disposed;

        // ─────────────────────────────────────────────────────────────────────
        // Device list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Connects to the server and retrieves the exported device list.
        /// </summary>
        public async Task<List<UsbDevice>> GetDeviceListAsync(
            UsbIpServer server,
            CancellationToken ct = default)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(server.IpAddress, server.Port, ct);
            tcp.NoDelay = true;

            await using var stream = tcp.GetStream();
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

            // Send OP_REQ_DEVLIST
            WriteHeader(writer, OP_REQ_DEVLIST);
            writer.Flush();

            // Read OP_REP_DEVLIST header (16 bytes)
            ReadBigEndianInt16(reader); // protocol version
            var code    = ReadBigEndianInt16(reader);
            var status  = ReadBigEndianInt32(reader);
            var count   = ReadBigEndianInt32(reader);

            if (code != OP_REP_DEVLIST)
                throw new InvalidOperationException($"Unexpected USB/IP reply code: 0x{(ushort)code:X4}");

            if (status != ST_OK)
                throw new InvalidOperationException($"Server returned status {status}");

            var devices = new List<UsbDevice>(count);
            for (int i = 0; i < count; i++)
            {
                var dev = ReadDeviceEntry(reader, server);
                devices.Add(dev);
            }
            return devices;
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

        private static string? ResolveUsbipExePath()
        {
            if (_usbipExePath != null && File.Exists(_usbipExePath))
                return _usbipExePath;

            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usbip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip", "usbip.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "USBip", "usbip.exe")
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

        private static UsbDevice ReadDeviceEntry(BinaryReader reader, UsbIpServer server)
        {
            // path: 256 bytes
            var pathBytes = ReadExact(reader, 256);
            var path = Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');

            // busId: 32 bytes
            var busIdBytes = ReadExact(reader, 32);
            var busId = Encoding.ASCII.GetString(busIdBytes).TrimEnd('\0');

            var busNum   = ReadBigEndianInt32(reader);
            var devNum   = ReadBigEndianInt32(reader);
            var speed    = ReadBigEndianInt32(reader);
            var vendorId = (ushort)ReadBigEndianInt16(reader);
            var prodId   = (ushort)ReadBigEndianInt16(reader);
            var bcd      = (ushort)ReadBigEndianInt16(reader);

            var devClass    = reader.ReadByte();
            var devSubClass = reader.ReadByte();
            var devProto    = reader.ReadByte();
            var configVal   = reader.ReadByte();
            var numConfigs  = reader.ReadByte();
            var numIfaces   = reader.ReadByte();

            // Read interface entries (4 bytes each)
            for (int i = 0; i < numIfaces; i++)
                ReadExact(reader, 4);

            return new UsbDevice
            {
                Path            = path,
                BusId           = busId,
                BusNum          = busNum,
                DevNum          = devNum,
                Speed           = speed,
                VendorId        = vendorId,
                ProductId       = prodId,
                BcdDevice       = bcd,
                DeviceClass     = devClass,
                DeviceSubClass  = devSubClass,
                DeviceProtocol  = devProto,
                NumInterfaces   = numIfaces,
                Server          = server
            };
        }

        private static void WriteHeader(BinaryWriter writer, short opCode, int status = 0)
        {
            writer.Write(ReverseBytes(UsbIpVersion));
            writer.Write(ReverseBytes(opCode));
            writer.Write(ReverseBytes(status));
        }

        private static short ReadBigEndianInt16(BinaryReader r)
        {
            var bytes = ReadExact(r, 2);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }

        private static int ReadBigEndianInt32(BinaryReader r)
        {
            var bytes = ReadExact(r, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException($"Expected {count} bytes but received {bytes.Length}.");
            return bytes;
        }

        private static short ReverseBytes(short value)
        {
            var bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }

        private static int ReverseBytes(int value)
        {
            var bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
