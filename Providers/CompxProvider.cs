using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace BatteryTray
{
    public class CompxProvider : IBatteryProvider
    {
        public string ProviderName => "Compx Wireless HID (VXE/Darmoshark/Ajazz/Zaopin/MCHOSE)";

        // Danh sách VID từ cộng đồng nguồn mở sử dụng khung giao thức Compx CX52850 / CX52650 (ReportID 0x08)
        private static readonly int[] VendorIds =
        {
            0x3554, // Compx chính (VXE R1, Darmoshark, Ajazz, Zaopin, Delux, Scyrox, Attack Shark đời mới)
            0x25A7, // Compx OEM
            0x320F, // OEM Compx (VXE / dòng chuột nội địa)
            0x3537, // OEM Compx
            0x3837, // MCHOSE
            0x2023, // MCHOSE / OEM
            0x36A7, // WLMouse
            0x3710, // Pulsar 4K/8K
            0x3770, // Hitscan
            0x093A  // PixArt / Incott OEM
        };

        private static readonly Dictionary<string, (int Percent, string Name, DeviceCategory Category)> _cache = new();

        private static readonly Dictionary<string, string> KnownDevices = new()
        {
            ["3554:F50E"] = "Darmoshark M3 / M3s Wireless",
            ["3554:F510"] = "Darmoshark N3 / M3 4K Wireless",
            ["3554:F523"] = "Darmoshark M2 Wireless",
            ["3554:F508"] = "Ajazz AJ199 Wireless",
            ["3554:F511"] = "Ajazz AJ139 Pro / AJ159 Wireless",
            ["3554:F515"] = "Zaopin Z1 Pro Wireless",
            ["3554:F520"] = "Zaopin Z2 Wireless",
            ["3554:F50D"] = "Delux M800 Ultra / M600 Wireless",
            ["3554:F58E"] = "VXE Dragonfly R1 Wireless",
            ["3554:F58F"] = "VXE Dragonfly R1 Pro / Pro Max",
            ["3554:F595"] = "VXE / ATK Wireless Mouse (4K/8K)",
            ["3554:F5D5"] = "Scyrox V8 / V6 Wireless",
            ["3554:F52F"] = "Attack Shark X3 Pro / R3 Wireless",
            ["3837:0001"] = "MCHOSE A5 / A7 Wireless",
            ["36A7:0001"] = "WLMouse Beast X Wireless",
            ["3710:5402"] = "Pulsar Wireless Mouse (4K/8K)"
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

                    req[0] = 0x08;
                    req[1] = 0x04;

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

                            if (percent <= 0) return null;

                            string name = ResolveDeviceName(device);
                            return (percent, isCharging, name, DeviceCategory.Mouse);
                        }
                    }
                }
                catch { }
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
