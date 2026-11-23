using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace BatteryTray
{
    public class WirelessHeadsetProvider : IBatteryProvider
    {
        public string ProviderName => "Wireless Headset HID (SteelSeries/HyperX/Corsair)";

        private const uint HidBatteryStrengthUsage = 0x00060020;

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

        // SteelSeries (VID 0x1038)
        private static readonly HashSet<int> SteelSeriesLegacyPids = new()
        {
            0x1260, 0x12AD, // Arctis 7 (2017 / 2019)
            0x12B3, 0x12B6, // Arctis 1 Wireless / Xbox
            0x12C2,         // Arctis 9
            0x1290          // Arctis Pro Wireless
        };

        private static readonly HashSet<int> SteelSeriesNovaPids = new()
        {
            0x220E, 0x2212, 0x2216, 0x2236, // Arctis 7+, 7P+, 7X+, 7+ Destiny 2
            0x2202, 0x2206, 0x220A, 0x223A, // Arctis Nova 7, 7X, 7P, Diablo IV
            0x2258, 0x225C, 0x225E          // Arctis Nova 5, 5X, 5P
        };

        private static readonly HashSet<int> SteelSeriesNovaProPids = new()
        {
            0x12E0, 0x12E5 // Arctis Nova Pro Wireless / Xbox
        };

        // HyperX (VID 0x0951 Kingston / 0x03F0 HP)
        private static readonly HashSet<int> HyperXCloudFlightPids = new() { 0x16C4, 0x1723 };
        private static readonly HashSet<int> HyperXCloud2Pids = new() { 0x1718, 0x018B, 0x0696 };
        private static readonly HashSet<int> HyperXCloudAlphaPids = new() { 0x098D };
        private static readonly HashSet<int> HyperXCloud3Pids = new() { 0x05B7 };

        // Corsair (VID 0x1B1C)
        private static readonly HashSet<int> CorsairLegacyPids = new()
        {
            0x0A14, 0x0A16, 0x0A1A, 0x0A2B, // Void / Void Pro / SE / RGB
            0x0A38, 0x0A4F,                 // HS70 / HS70 Pro Wireless
            0x0A51, 0x0A55, 0x0A57          // Void Elite Wireless / RGB / Surround
        };

        private static readonly HashSet<int> CorsairSlipstreamPids = new()
        {
            0x0A3E, 0x0A40, 0x0A42, 0x0A44, 0x0A5C, // Virtuoso / SE / XT
            0x0A64, 0x0A6B, 0x0A78,                 // HS80 RGB Wireless / HS80 Max
            0x0A73, 0x0A97                          // HS55 Wireless / Virtuoso Max
        };

        private static readonly Dictionary<string, string> KnownHeadsets = new()
        {
            // SteelSeries
            ["1038:1260"] = "SteelSeries Arctis 7",
            ["1038:12AD"] = "SteelSeries Arctis 7 (2019)",
            ["1038:12B3"] = "SteelSeries Arctis 1 Wireless",
            ["1038:12B6"] = "SteelSeries Arctis 1 Wireless Xbox",
            ["1038:12C2"] = "SteelSeries Arctis 9",
            ["1038:1290"] = "SteelSeries Arctis Pro Wireless",
            ["1038:220E"] = "SteelSeries Arctis 7+",
            ["1038:2212"] = "SteelSeries Arctis 7P+",
            ["1038:2216"] = "SteelSeries Arctis 7X+",
            ["1038:2236"] = "SteelSeries Arctis 7+ Destiny",
            ["1038:2202"] = "SteelSeries Arctis Nova 7",
            ["1038:2206"] = "SteelSeries Arctis Nova 7X",
            ["1038:220A"] = "SteelSeries Arctis Nova 7P",
            ["1038:223A"] = "SteelSeries Arctis Nova 7 Diablo IV",
            ["1038:2258"] = "SteelSeries Arctis Nova 5",
            ["1038:225C"] = "SteelSeries Arctis Nova 5X",
            ["1038:225E"] = "SteelSeries Arctis Nova 5P",
            ["1038:12E0"] = "SteelSeries Arctis Nova Pro Wireless",
            ["1038:12E5"] = "SteelSeries Arctis Nova Pro Wireless X",
            // HyperX
            ["0951:16C4"] = "HyperX Cloud Flight Wireless",
            ["0951:1723"] = "HyperX Cloud Flight Wireless",
            ["0951:1718"] = "HyperX Cloud II Wireless",
            ["03F0:018B"] = "HyperX Cloud II Wireless",
            ["03F0:0696"] = "HyperX Cloud II Core Wireless",
            ["03F0:098D"] = "HyperX Cloud Alpha Wireless",
            ["03F0:05B7"] = "HyperX Cloud III Wireless",
            // Corsair
            ["1B1C:0A14"] = "Corsair Void Wireless",
            ["1B1C:0A16"] = "Corsair Void Pro Wireless",
            ["1B1C:0A1A"] = "Corsair Void Pro SE",
            ["1B1C:0A2B"] = "Corsair Void Pro RGB",
            ["1B1C:0A38"] = "Corsair HS70 Wireless",
            ["1B1C:0A4F"] = "Corsair HS70 Pro Wireless",
            ["1B1C:0A51"] = "Corsair Void Elite Wireless",
            ["1B1C:0A55"] = "Corsair Void Elite RGB",
            ["1B1C:0A57"] = "Corsair Void Elite Surround",
            ["1B1C:0A3E"] = "Corsair Virtuoso Wireless",
            ["1B1C:0A40"] = "Corsair Virtuoso SE",
            ["1B1C:0A42"] = "Corsair Virtuoso RGB",
            ["1B1C:0A44"] = "Corsair Virtuoso SE RGB",
            ["1B1C:0A5C"] = "Corsair Virtuoso XT",
            ["1B1C:0A64"] = "Corsair HS80 RGB Wireless",
            ["1B1C:0A6B"] = "Corsair HS80 Wireless",
            ["1B1C:0A73"] = "Corsair HS55 Wireless",
            ["1B1C:0A78"] = "Corsair HS80 Max Wireless",
            ["1B1C:0A97"] = "Corsair Virtuoso Max"
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

            // Dự phòng: chuẩn HID Battery Strength (UsagePage 0x06, Usage 0x20) cho tai nghe 2.4GHz hỗ trợ chuẩn Windows
            foreach (var dev in DeviceList.Local.GetHidDevices())
            {
                if (SupportedVids.Contains(dev.VendorID)) continue;
                string prod = ResolveName(dev, "");
                if (!LooksLikeHeadset(prod) || !HasHidBatteryUsage(dev)) continue;

                string deviceKey = $"HEADSET_STD_{dev.VendorID:X4}_{dev.ProductID:X4}";
                if (results.ContainsKey(deviceKey)) continue;

                int? pct = ReadStandardHidBattery(dev);
                if (pct.HasValue)
                {
                    _cache[deviceKey] = (pct.Value, prod);
                    results[deviceKey] = new DeviceBatteryInfo
                    {
                        Id = deviceKey,
                        Name = prod,
                        ProviderName = ProviderName,
                        BatteryPercent = pct.Value,
                        IsAsleepOrCached = false,
                        Connection = ConnectionType.UsbWireless24G,
                        Category = DeviceCategory.Headset
                    };
                }
            }

            return results.Values;
        }

        private static (int Percent, bool IsCharging, string Name)? QuerySteelSeries(IEnumerable<HidDevice> endpoints)
        {
            foreach (var dev in endpoints.OrderByDescending(d => d.GetMaxOutputReportLength()))
            {
                bool isNova = SteelSeriesNovaPids.Contains(dev.ProductID);
                bool isNovaPro = SteelSeriesNovaProPids.Contains(dev.ProductID);
                bool isLegacy = SteelSeriesLegacyPids.Contains(dev.ProductID);
                if (!isNova && !isNovaPro && !isLegacy) continue;

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
                        if (isNovaPro)
                        {
                            req[0] = 0x06;
                            req[1] = 0xB0;
                        }
                        else if (isNova)
                        {
                            req[0] = 0x00;
                            req[1] = 0xB0;
                        }
                        else
                        {
                            req[0] = 0x06;
                            req[1] = 0x18;
                        }

                        stream.Write(req, 0, req.Length);

                        byte[] resp = new byte[inLen];
                        int read = stream.Read(resp, 0, resp.Length);
                        if (read < 4) continue;

                        if (isNovaPro)
                        {
                            int offset = (resp[0] == 0x06 && resp[1] == 0xB0) ? 0
                                       : (read > 2 && resp[1] == 0x06 && resp[2] == 0xB0) ? 1 : -1;
                            if (offset < 0 || read <= offset + 15) continue;

                            byte connStatus = resp[offset + 15];
                            if (connStatus == 0x02) return null; // Tai nghe tắt hoặc ngắt kết nối khỏi dock

                            int level = resp[offset + 6]; // Thang 0..8 trên Nova Pro Wireless
                            if (level >= 0 && level <= 8)
                            {
                                int pct = Math.Clamp((int)Math.Round(level * 100.0 / 8.0), 1, 100);
                                return (pct, false, ResolveName(dev, "SteelSeries Arctis Nova Pro"));
                            }
                        }
                        else if (isNova)
                        {
                            int offset = resp[0] == 0xB0 ? 0 : (resp[1] == 0xB0 ? 1 : -1);
                            if (offset < 0 || read <= offset + 3) continue;

                            byte status = resp[offset + 1];
                            if (status == 0x01) return null; // Tai nghe tắt nguồn

                            int rawLevel = resp[offset + 2];
                            bool charging = resp[offset + 3] == 0x01;
                            int pct = rawLevel <= 4 ? Math.Clamp(rawLevel * 25, 1, 100) : Math.Clamp(rawLevel, 1, 100);
                            return (pct, charging, ResolveName(dev, "SteelSeries Headset"));
                        }
                        else
                        {
                            int pct = resp[2];
                            if (pct >= 1 && pct <= 100)
                                return (pct, false, ResolveName(dev, "SteelSeries Headset"));
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
                bool isFlight = HyperXCloudFlightPids.Contains(pid);
                bool isCloud2 = HyperXCloud2Pids.Contains(pid);
                bool isCloudAlpha = HyperXCloudAlphaPids.Contains(pid);
                bool isCloud3 = HyperXCloud3Pids.Contains(pid);
                if (!isFlight && !isCloud2 && !isCloudAlpha && !isCloud3) continue;

                int outLen = dev.GetMaxOutputReportLength();
                int inLen = dev.GetMaxInputReportLength();
                if (outLen < 20 || inLen < 10) continue;

                if (!dev.TryOpen(out HidStream stream)) continue;
                using (stream)
                {
                    stream.ReadTimeout = 150;
                    stream.WriteTimeout = 150;
                    try
                    {
                        byte[] req = new byte[outLen];
                        if (isFlight)
                        {
                            req[0] = 0x21; req[1] = 0xFF; req[2] = 0x05;
                        }
                        else if (isCloud2)
                        {
                            req[0] = 0x06; req[1] = 0xFF; req[2] = 0xBB; req[3] = 0x02;
                        }
                        else if (isCloudAlpha)
                        {
                            req[0] = 0x21; req[1] = 0xBB; req[2] = 0x0B;
                        }
                        else
                        {
                            req[0] = 0x66; req[1] = 0x89;
                        }

                        stream.Write(req, 0, req.Length);

                        byte[] resp = new byte[inLen];
                        int read = stream.Read(resp, 0, resp.Length);
                        if (read < 8) continue;

                        if (isFlight)
                        {
                            int state = resp[3];
                            int raw = resp[4];
                            if (state == 0x10)
                                return (100, true, ResolveName(dev, "HyperX Cloud Flight"));

                            int pct = MapHyperXCloudFlightBattery(state, raw);
                            if (pct > 0)
                                return (pct, false, ResolveName(dev, "HyperX Cloud Flight"));
                        }
                        else
                        {
                            int pct = isCloud2 ? resp[7] : (isCloudAlpha ? resp[3] : resp[4]);
                            if (pct >= 1 && pct <= 100)
                                return (pct, false, ResolveName(dev, "HyperX Wireless Headset"));
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static int MapHyperXCloudFlightBattery(int state, int val)
        {
            if (state == 0x0E)
            {
                if (val <= 89) return 10;
                if (val <= 119) return 15;
                if (val <= 148) return 20;
                if (val <= 159) return 25;
                if (val <= 169) return 30;
                if (val <= 179) return 35;
                if (val <= 189) return 40;
                if (val <= 199) return 45;
                if (val <= 209) return 50;
                if (val <= 219) return 55;
                if (val <= 229) return 60;
                if (val <= 239) return 65;
                return 70;
            }
            if (state == 0x0F)
            {
                if (val <= 19) return 70;
                if (val <= 49) return 75;
                if (val <= 69) return 80;
                if (val <= 99) return 85;
                if (val <= 119) return 90;
                if (val <= 129) return 95;
                return 100;
            }
            return 0;
        }

        private static (int Percent, bool IsCharging, string Name)? QueryCorsair(IEnumerable<HidDevice> endpoints)
        {
            foreach (var dev in endpoints)
            {
                int pid = dev.ProductID;
                string prodLower = ResolveName(dev, "").ToLowerInvariant();

                bool isLegacy = CorsairLegacyPids.Contains(pid) ||
                                prodLower.Contains("void") || prodLower.Contains("hs70");
                bool isSlipstream = CorsairSlipstreamPids.Contains(pid) ||
                                    prodLower.Contains("hs80") || prodLower.Contains("virtuoso") || prodLower.Contains("hs55");

                if (!isLegacy && !isSlipstream) continue;

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
                        if (isLegacy)
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

        private static bool LooksLikeHeadset(string name)
        {
            string n = name.ToLowerInvariant();
            string[] keys = { "headset", "headphone", "audio", "mercury", "neptune", "attack shark", "l80", "g300", "vxe v1", "mchose", "dareu", "edifier", "hecate", "fifine", "inzone", "pulse", "rog", "epos", "jbl", "sony", "audeze" };
            return keys.Any(n.Contains);
        }

        private static bool HasHidBatteryUsage(HidDevice device)
        {
            try
            {
                return device.GetReportDescriptor().DeviceItems
                    .Any(item => item.Usages.GetAllValues().Contains(HidBatteryStrengthUsage));
            }
            catch { return false; }
        }

        private static int? ReadStandardHidBattery(HidDevice device)
        {
            if (!device.TryOpen(out HidStream stream)) return null;
            using (stream)
            {
                stream.ReadTimeout = 100;
                try
                {
                    int len = device.GetMaxInputReportLength();
                    if (len < 2) return null;
                    byte[] buf = new byte[len];
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read >= 2 && buf[1] >= 1 && buf[1] <= 100) return buf[1];
                }
                catch { }
            }
            return null;
        }

        private static string ResolveName(HidDevice device, string fallback)
        {
            string vidPid = $"{device.VendorID:X4}:{device.ProductID:X4}";
            if (KnownHeadsets.TryGetValue(vidPid, out string? mapped))
                return mapped;

            try
            {
                string prod = device.GetProductName();
                if (!string.IsNullOrWhiteSpace(prod))
                    return prod.Trim();
            }
            catch { }

            return string.IsNullOrEmpty(fallback)
                ? $"Wireless Headset ({vidPid})"
                : fallback;
        }
    }
}
