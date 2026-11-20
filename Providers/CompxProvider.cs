using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace BatteryTray
{
    public class CompxProvider : IBatteryProvider
    {
        public string ProviderName => "Compx Wireless HID (Darmoshark/Ajazz/Zaopin)";

        // Mã VID phổ biến của đầu thu không dây sử dụng chip Compx CX52850 / CX52650
        private static readonly int[] VendorIds = { 0x3554, 0x25A7 };

        private static readonly Dictionary<string, (int Percent, string Name, DeviceCategory Category)> _cache = new();

        // Bảng nhận diện một số mẫu đầu thu/chuột phổ biến dùng chip Compx
        private static readonly Dictionary<string, string> KnownDevices = new()
        {
            ["3554:F50E"] = "Darmoshark M3 / M3s Wireless",
            ["3554:F510"] = "Darmoshark N3 / M3 4K Wireless",
            ["3554:F523"] = "Darmoshark M2 Wireless",
            ["3554:F508"] = "Ajazz AJ199 Wireless",
            ["3554:F511"] = "Ajazz AJ139 Pro / AJ159 Wireless",
            ["3554:F515"] = "Zaopin Z1 Pro Wireless",
            ["3554:F520"] = "Zaopin Z2 Wireless",
            ["3554:F50D"] = "Delux M800 Ultra / M600 Wireless"
        };

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var allDevices = new List<HidDevice>();
            foreach (int vid in VendorIds)
            {
                allDevices.AddRange(DeviceList.Local.GetHidDevices(vid));
            }

            if (allDevices.Count == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            var results = new Dictionary<string, DeviceBatteryInfo>();

            var receiverGroups = allDevices
                .Where(IsCompxControlEndpoint)
                .GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}_{GetInterfacePrefix(d.DevicePath)}");

            foreach (var group in receiverGroups)
            {
                HidDevice? endpoint = group
                    .OrderByDescending(d => d.GetMaxOutputReportLength())
                    .FirstOrDefault();

                if (endpoint == null) continue;

                string deviceKey = $"COMPX_{endpoint.VendorID:X4}_{endpoint.ProductID:X4}";
                var queried = QueryCompxBattery(endpoint);

                if (queried.HasValue)
                {
                    var info = queried.Value;
                    _cache[deviceKey] = (info.Percent, info.Name, info.Category);

                    results[deviceKey] = new DeviceBatteryInfo
                    {
                        Id = deviceKey,
                        Name = info.IsCharging ? $"{info.Name} (Đang sạc)" : info.Name,
                        ProviderName = ProviderName,
                        BatteryPercent = info.Percent,
                        IsAsleepOrCached = false,
                        Connection = ConnectionType.UsbWireless24G,
                        Category = info.Category
                    };
                }
                else if (_cache.TryGetValue(deviceKey, out var cached))
                {
                    results[deviceKey] = new DeviceBatteryInfo
                    {
                        Id = deviceKey,
                        Name = cached.Name,
                        ProviderName = ProviderName,
                        BatteryPercent = cached.Percent,
                        IsAsleepOrCached = true,
                        Connection = ConnectionType.UsbWireless24G,
                        Category = cached.Category
                    };
                }
            }

            return results.Values;
        }

        private static bool IsCompxControlEndpoint(HidDevice dev)
        {
            int maxOut = dev.GetMaxOutputReportLength();
            int maxIn = dev.GetMaxInputReportLength();
            if (maxOut < 17 || maxIn < 17) return false;

            try
            {
                var outIds = dev.GetReportDescriptor().OutputReports.Select(r => r.ReportID).ToHashSet();
                return outIds.Contains(0x08);
            }
            catch
            {
                return false;
            }
        }

        private static (int Percent, bool IsCharging, string Name, DeviceCategory Category)? QueryCompxBattery(HidDevice device)
        {
            if (!device.TryOpen(out HidStream stream))
                return null;

            using (stream)
            {
                stream.ReadTimeout = 150;
                stream.WriteTimeout = 150;

                try
                {
                    int outLen = Math.Max(17, device.GetMaxOutputReportLength());
                    byte[] req = new byte[outLen];

                    // Khung lệnh đọc pin chuẩn Compx: ReportID 0x08, Command 0x04
                    req[0] = 0x08;
                    req[1] = 0x04;

                    // Checksum 17-byte đặc trưng của firmware Compx: (0x55 - sum(byte[0..15])) & 0xFF
                    int sum = 0;
                    for (int i = 0; i < 16; i++) sum += req[i];
                    req[16] = (byte)((0x55 - (sum & 0xFF)) & 0xFF);

                    stream.Write(req, 0, req.Length);

                    int inLen = Math.Max(17, device.GetMaxInputReportLength());
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        byte[] resp = new byte[inLen];
                        int read = stream.Read(resp, 0, resp.Length);
                        if (read < 8) continue;

                        if (resp[0] == 0x08 && resp[1] == 0x04)
                        {
                            // Tùy bản firmware 1K hay 4K/8K, % pin nằm ở byte 5 hoặc byte 6
                            int percent = 0;
                            bool isCharging = false;

                            if (resp[5] >= 1 && resp[5] <= 100)
                            {
                                percent = resp[5];
                                isCharging = resp[6] == 0x01;
                            }
                            else if (resp[6] >= 1 && resp[6] <= 100)
                            {
                                percent = resp[6];
                                isCharging = resp[5] == 0x01;
                            }

                            if (percent <= 0)
                                return null;

                            string name = ResolveDeviceName(device);
                            return (percent, isCharging, name, DeviceCategory.Mouse);
                        }
                    }
                }
                catch
                {
                    // Timeout khi chuột đang ở chế độ ngủ sâu hoặc tắt nguồn
                }
            }

            return null;
        }

        private static string ResolveDeviceName(HidDevice device)
        {
            string vidPid = $"{device.VendorID:X4}:{device.ProductID:X4}";
            if (KnownDevices.TryGetValue(vidPid, out string? mapped))
                return mapped;

            try
            {
                string prod = device.GetProductName();
                if (!string.IsNullOrWhiteSpace(prod) &&
                    !prod.Equals("Wireless Receiver", StringComparison.OrdinalIgnoreCase) &&
                    !prod.Equals("2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase))
                {
                    return prod.Trim();
                }
            }
            catch { }

            return $"Compx Wireless Mouse ({vidPid})";
        }

        private static string GetInterfacePrefix(string path)
        {
            int idx = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? path[..idx] : path;
        }
    }
}
