using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UsbIpClientApp.Models
{
    /// <summary>
    /// Represents a USB device exported by an Android USB/IP server.
    /// </summary>
    public class UsbDevice
    {
        public string BusId      { get; set; } = string.Empty;
        public string Path       { get; set; } = string.Empty;
        public int    BusNum     { get; set; }
        public int    DevNum     { get; set; }
        public int    Speed      { get; set; }
        public ushort VendorId   { get; set; }
        public ushort ProductId  { get; set; }
        public ushort BcdDevice  { get; set; }
        public byte   DeviceClass    { get; set; }
        public byte   DeviceSubClass { get; set; }
        public byte   DeviceProtocol { get; set; }
        public byte   NumInterfaces  { get; set; }

        /// <summary>Parent server that exports this device.</summary>
        public UsbIpServer? Server { get; set; }

        public string DisplayName => $"{VendorIdHex}:{ProductIdHex}  –  {ClassName}";

        public string VendorIdHex  => VendorId.ToString("X4");
        public string ProductIdHex => ProductId.ToString("X4");

        public string SpeedText => Speed switch
        {
            1 => "Low Speed (1.5 Mbps)",
            2 => "Full Speed (12 Mbps)",
            3 => "High Speed (480 Mbps)",
            5 => "Super Speed (5 Gbps)",
            _ => $"Speed {Speed}"
        };

        public string ClassName => DeviceClass switch
        {
            0x00 => "Composite Device",
            0x01 => "Audio",
            0x02 => "Communications",
            0x03 => "HID",
            0x05 => "Physical",
            0x06 => "Still Image",
            0x07 => "Printer",
            0x08 => "Mass Storage",
            0x09 => "Hub",
            0x0A => "CDC Data",
            0x0B => "Smart Card",
            0x0D => "Content Security",
            0x0E => "Video",
            0xE0 => "Wireless Controller",
            0xEF => "Miscellaneous",
            0xFF => "Vendor-Specific",
            _    => $"Class 0x{DeviceClass:X2}"
        };

        public bool IsAttached { get; set; }
    }

    /// <summary>
    /// Represents a discovered Android USB/IP server on the LAN.
    /// </summary>
    public class UsbIpServer : INotifyPropertyChanged
    {
        private string _ipAddress = string.Empty;
        private int _port = 3240;
        private string _hostname = string.Empty;
        private int _deviceCount;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string IpAddress
        {
            get => _ipAddress;
            set => SetField(ref _ipAddress, value);
        }

        public int Port
        {
            get => _port;
            set => SetField(ref _port, value);
        }

        public string Hostname
        {
            get => _hostname;
            set => SetField(ref _hostname, value);
        }

        public int DeviceCount
        {
            get => _deviceCount;
            set => SetField(ref _deviceCount, value);
        }

        public override string ToString() => $"{Hostname} ({IpAddress}:{Port})";

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
