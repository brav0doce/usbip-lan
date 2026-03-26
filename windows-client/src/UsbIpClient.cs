using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
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

        private static readonly string UsbipExePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "usbip.exe");

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
            var version = ReadBigEndianInt16(reader);
            var code    = ReadBigEndianInt16(reader);
            var status  = ReadBigEndianInt32(reader);
            var count   = ReadBigEndianInt32(reader);

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
            if (!File.Exists(UsbipExePath))
                return (false, $"usbip.exe not found at {UsbipExePath}.\nInstall usbip-win2 first (see README).");

            var args = $"attach --remote={device.Server!.IpAddress} --busid={device.BusId}";
            return await RunUsbipAsync(args, ct);
        }

        /// <summary>
        /// Detaches a previously attached USB device.
        /// </summary>
        public async Task<(bool Success, string Output)> DetachAsync(
            int portNumber,
            CancellationToken ct = default)
        {
            if (!File.Exists(UsbipExePath))
                return (false, "usbip.exe not found. Install usbip-win2 first.");

            var args = $"detach --port={portNumber}";
            return await RunUsbipAsync(args, ct);
        }

        /// <summary>
        /// Lists currently attached USB/IP ports.
        /// </summary>
        public async Task<(bool Success, string Output)> ListPortsAsync(
            CancellationToken ct = default)
        {
            if (!File.Exists(UsbipExePath))
                return (false, "usbip.exe not found. Install usbip-win2 first.");

            return await RunUsbipAsync("port", ct);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private static async Task<(bool, string)> RunUsbipAsync(
            string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = UsbipExePath,
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

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode == 0, sb.ToString().Trim());
        }

        private static UsbDevice ReadDeviceEntry(BinaryReader reader, UsbIpServer server)
        {
            // path: 256 bytes
            var pathBytes = reader.ReadBytes(256);
            var path = Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');

            // busId: 32 bytes
            var busIdBytes = reader.ReadBytes(32);
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
                reader.ReadBytes(4);

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
            var bytes = r.ReadBytes(2);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt16(bytes, 0);
        }

        private static int ReadBigEndianInt32(BinaryReader r)
        {
            var bytes = r.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
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
