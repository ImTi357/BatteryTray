using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace BatteryTray
{
    public class WirelessHeadsetProvider : IBatteryProvider
    {
        public string ProviderName => "Wireless Headset HID (SteelSeries/HyperX/Corsair)";

        private const int VidSteelSeries = 0x1038;
        private const int VidHyperXLegacy = 0x0951;
        private const int VidHyperXHp = 0x03F0;
        private const int VidCorsair = 0x1B1C;

        private static readonly int[] SupportedVids =
        {
            VidSteelSeries,
            VidHyperXLegacy,
            VidHyperXHp,
            VidCorsair
        };

        private static readonly HashSet<int> SteelSeriesNovaPids = new()
        {
            0x2202, 0x2206, 0x220A, 0x220E, // Arctis 7+, 7X+, 7P+, Nova 7
            0x2232, 0x223A,                 // Arctis Nova 7X, 7P
            0x2258, 0x225C, 0x225E,         // Arctis Nova 5, 5X, 5P
            0x12E0                          // Arctis Nova Pro Wireless
        };

        private static readonly HashSet<int> SteelSeriesLegacyPids = new()
        {
            0x1260, 0x12AD, // Arctis 7 (2017/2019)
            0x12B3, 0x12B6, // Arctis 1 Wireless
            0x12C2,         // Arctis 9
            0x1290          // Arctis Pro Wireless
        };

        private static readonly Dictionary<string, (int Percent, string Name)> _cache = new();

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var results = new Dictionary<string, DeviceBatteryInfo>();

            foreach (int vid in SupportedVids)
            {
                var devices = DeviceList.Local.GetHidDevices(vid).ToList();
                if (devices.Count == 0) continue;

                var groups = devices
                    .Where(d => d.GetMaxOutputReportLength() >= 2 && d.GetMaxInputReportLength() >= 2)
                    .GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}");

                foreach (var group in groups)
                {
                    var sample = group.First();
                    string deviceKey = $"HEADSET_{sample.VendorID:X4}_{sample.ProductID:X4}";

                    (int Percent, bool IsCharging, string Name)? queried = vid switch
                    {
                        VidSteelSeries => QuerySteelSeries(group),
                        VidHyperXLegacy or VidHyperXHp => QueryHyperX(group),
                        VidCorsair => QueryCorsair(group),
                        _ => null
                    };

                    if (queried.HasValue)
                    {
                        var info = queried.Value;
                        _cache[deviceKey] = (info.Percent, info.Name);

                        results[deviceKey] = new DeviceBatteryInfo
                        {
                            Id = deviceKey,
                            Name = info.IsCharging ? $"{info.Name} (Đang sạc)" : info.Name,
                            ProviderName = ProviderName,
                            BatteryPercent = info.Percent,
                            IsAsleepOrCached = false,
                            Connection = ConnectionType.UsbWireless24G,
                            Category = DeviceCategory.Headset
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
                            Category = DeviceCategory.Headset
                        };
                    }
                }
            }

            return results.Values;
        }

        private static (int Percent, bool IsCharging, string Name)? QuerySteelSeries(IEnumerable<HidDevice> endpoints)
        {
            foreach (var dev in endpoints.OrderByDescending(d => d.GetMaxOutputReportLength()))
            {
                bool isNova = SteelSeriesNovaPids.Contains(dev.ProductID);
                bool isLegacy = SteelSeriesLegacyPids.Contains(dev.ProductID);
                if (!isNova && !isLegacy) continue;

                int outLen = dev.GetMaxOutputReportLength();
                int inLen = dev.GetMaxInputReportLength();
                if (outLen < 31 || inLen < 8) continue;

                if (!dev.TryOpen(out HidStream stream)) continue;
                using (stream)
                {
                    stream.ReadTimeout = 150;
                    stream.WriteTimeout = 150;
                    try
                    {
                        byte[] req = new byte[outLen];
                        if (isNova)
                        {
                            // Lệnh 0xB0 đọc trạng thái pin dòng Arctis 7+ / Nova 5 / Nova 7 / Nova Pro
                            req[0] = 0x00;
                            req[1] = 0xB0;
                        }
                        else
                        {
                            // Lệnh 0x06 0x18 đọc pin dòng Arctis 1 / 7 / 9 / Pro Wireless
                            req[0] = 0x06;
                            req[1] = 0x18;
                        }

                        stream.Write(req, 0, req.Length);

                        byte[] resp = new byte[inLen];
                        int read = stream.Read(resp, 0, resp.Length);
                        if (read < 4) continue;

                        if (isNova)
                        {
                            // resp[1] hoặc resp[2] tùy việc Windows có kèm ReportID 0x00 đầu gói hay không
                            int offset = resp[0] == 0xB0 ? 0 : (resp[1] == 0xB0 ? 1 : -1);
                            if (offset < 0 || read <= offset + 3) continue;

                            byte status = resp[offset + 1];
                            if (status == 0x01) return null; // Tai nghe đang tắt nguồn

                            int rawLevel = resp[offset + 2];
                            bool charging = resp[offset + 3] == 0x01;
                            int pct = rawLevel <= 4 ? Math.Clamp(rawLevel * 25, 1, 100) : Math.Clamp(rawLevel, 1, 100);
                            return (pct, charging, ResolveName(dev, "SteelSeries Headset"));
                        }
                        else
                        {
                            int pct = resp[2];
                            if (pct >= 1 && pct <= 100)
                            {
                                return (pct, false, ResolveName(dev, "SteelSeries Headset"));
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static (int Percent, bool IsCharging, string Name)? QueryHyperX(IEnumerable<HidDevice> endpoints)
        {
            foreach (var dev in endpoints.OrderByDescending(d => d.GetMaxOutputReportLength()))
            {
                int pid = dev.ProductID;
                int outLen = dev.GetMaxOutputReportLength();
                int inLen = dev.GetMaxInputReportLength();

                // Chỉ gửi lệnh tới các PID tai nghe không dây HyperX đã xác định rõ giao thức
                bool isCloud2 = pid is 0x1718 or 0x018B or 0x0696;
                bool isCloudAlpha = pid is 0x098D;
                bool isCloud3 = pid is 0x05B7;
                if (!isCloud2 && !isCloudAlpha && !isCloud3) continue;

                if (outLen < 20 || inLen < 10) continue;
                if (!dev.TryOpen(out HidStream stream)) continue;

                using (stream)
                {
                    stream.ReadTimeout = 150;
                    stream.WriteTimeout = 150;
                    try
                    {
                        byte[] req = new byte[outLen];
                        if (isCloud2)
                        {
                            req[0] = 0x06; req[1] = 0xFF; req[2] = 0xBB; req[3] = 0x02;
                        }
                        else if (isCloudAlpha)
                        {
                            req[0] = 0x21; req[1] = 0xBB; req[2] = 0x0B;
                        }
                        else if (isCloud3)
                        {
                            req[0] = 0x66; req[1] = 0x89;
                        }

                        stream.Write(req, 0, req.Length);

                        byte[] resp = new byte[inLen];
                        int read = stream.Read(resp, 0, resp.Length);
                        if (read < 8) continue;

                        int pct = isCloud2 ? resp[7] : (isCloudAlpha ? resp[3] : resp[4]);
                        if (pct >= 1 && pct <= 100)
                        {
                            return (pct, false, ResolveName(dev, "HyperX Wireless Headset"));
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static (int Percent, bool IsCharging, string Name)? QueryCorsair(IEnumerable<HidDevice> endpoints)
        {
            foreach (var dev in endpoints)
            {
                string prodLower = ResolveName(dev, "").ToLowerInvariant();
                if (!prodLower.Contains("void") && !prodLower.Contains("hs70") &&
                    !prodLower.Contains("hs80") && !prodLower.Contains("virtuoso") &&
                    !prodLower.Contains("headset"))
                {
                    continue;
                }

                int outLen = dev.GetMaxOutputReportLength();
                int inLen = dev.GetMaxInputReportLength();
                if (outLen < 2 || inLen < 5) continue;

                if (!dev.TryOpen(out HidStream stream)) continue;
                using (stream)
                {
                    stream.ReadTimeout = 150;
                    stream.WriteTimeout = 150;
                    try
                    {
                        // Nhóm Void / HS70 dùng gói tin ReportID 0xC9 -> phản hồi 0xCA
                        if (prodLower.Contains("void") || prodLower.Contains("hs70"))
                        {
                            byte[] req = new byte[outLen];
                            req[0] = 0xC9;
                            req[1] = 0x64;
                            stream.Write(req, 0, req.Length);

                            byte[] resp = new byte[inLen];
                            int read = stream.Read(resp, 0, resp.Length);
                            if (read >= 5 && resp[0] == 0xCA && resp[4] != 0x00)
                            {
                                bool charging = (resp[2] & 0x80) != 0;
                                int pct = resp[2] & 0x7F;
                                if (pct >= 1 && pct <= 100)
                                    return (pct, charging, ResolveName(dev, "Corsair Headset"));
                            }
                        }
                        else if (outLen >= 64 && inLen >= 64)
                        {
                            // Nhóm Slipstream (HS80 / Virtuoso): lệnh 0x02 0x08 0x0F đọc pin dạng 0..1000
                            byte[] req = new byte[outLen];
                            int o = outLen >= 65 ? 1 : 0;
                            req[o] = 0x02;
                            req[o + 1] = 0x08;
                            req[o + 2] = 0x0F;
                            stream.Write(req, 0, req.Length);

                            byte[] resp = new byte[inLen];
                            int read = stream.Read(resp, 0, resp.Length);
                            int iOff = read >= 65 ? 1 : 0;
                            if (read >= iOff + 6 && resp[iOff + 2] == 0x0F)
                            {
                                int raw = resp[iOff + 4] | (resp[iOff + 5] << 8);
                                int pct = raw / 10;
                                if (pct >= 1 && pct <= 100)
                                    return (pct, false, ResolveName(dev, "Corsair Headset"));
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string ResolveName(HidDevice device, string fallback)
        {
            try
            {
                string prod = device.GetProductName();
                if (!string.IsNullOrWhiteSpace(prod))
                    return prod.Trim();
            }
            catch { }

            return string.IsNullOrEmpty(fallback)
                ? $"Wireless Headset ({device.VendorID:X4}:{device.ProductID:X4})"
                : fallback;
        }
    }
}
