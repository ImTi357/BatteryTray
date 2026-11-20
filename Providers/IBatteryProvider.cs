using System.Collections.Generic;

namespace BatteryTray
{
    public interface IBatteryProvider
    {
        string ProviderName { get; }
        IEnumerable<DeviceBatteryInfo> GetDevices();
    }
}
