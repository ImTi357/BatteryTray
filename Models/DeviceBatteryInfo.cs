namespace BatteryTray
{
    public enum DeviceCategory { Keyboard, Mouse, Audio, Gamepad, Other }
    public enum ConnectionType { Bluetooth, UsbWireless24G }

    public class DeviceBatteryInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public int BatteryPercent { get; set; }
        public bool IsAsleepOrCached { get; set; }
        public ConnectionType Connection { get; set; } = ConnectionType.Bluetooth;
        public DeviceCategory Category { get; set; } = DeviceCategory.Other;

        public bool HasBatteryReading => BatteryPercent >= 0 && BatteryPercent <= 100;

        public string ShortBadge => Category switch
        {
            DeviceCategory.Keyboard => "K",
            DeviceCategory.Mouse => "M",
            DeviceCategory.Audio => "H",
            DeviceCategory.Gamepad => "G",
            _ => "•"
        };

        public string ConnectionLabel => Connection switch
        {
            ConnectionType.Bluetooth => "Bluetooth",
            ConnectionType.UsbWireless24G => "USB 2.4GHz",
            _ => "Wireless"
        };
    }
}
