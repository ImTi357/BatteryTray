using System;
using System.Collections.Generic;
using System.Linq;

namespace BatteryTray
{
    public class BatteryAggregator
    {
        private readonly IReadOnlyList<IBatteryProvider> _providers;

        public BatteryAggregator(IEnumerable<IBatteryProvider> providers)
        {
            _providers = providers.ToList();
        }

        public List<DeviceBatteryInfo> CollectAllDevices()
        {
            var merged = new Dictionary<string, DeviceBatteryInfo>();

            foreach (var provider in _providers)
            {
                try
                {
                    foreach (var device in provider.GetDevices())
                    {
                        merged[device.Id] = device;
                    }
                }
                catch
                {
                    // Cô lập lỗi: 1 provider gặp sự cố không làm sập toàn bộ ứng dụng
                }
            }

            return merged.Values
                .OrderByDescending(d => d.HasBatteryReading)
                .ThenBy(d => d.Category)
                .ThenBy(d => d.Name)
                .ToList();
        }
    }
}
