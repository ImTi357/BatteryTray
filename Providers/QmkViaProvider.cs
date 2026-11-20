using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace BatteryTray
{
    public class QmkViaProvider : IBatteryProvider
    {
        public string ProviderName => "QMK / VIA Wireless HID";

        // Usage mở rộng 32-bit trong HidSharp: (UsagePage << 16) | Usage
        private const uint ViaRawHidUsage = 0xFF600061;       // QMK/VIA Raw HID (UsagePage 0xFF60, Usage 0x61)
        private const uint HidBatteryStrengthUsage = 0x00060020; // Generic Device Controls -> Battery Strength

        // Một số Vendor ID phổ biến của bàn phím/đầu thu không dây QMK/VIA
        private static readonly HashSet<int> KnownQmkVendorIds = new()
        {
            0x3434, // Keychron / Lemokey
            0x362D, // NuPhy
            0xFEED  // QMK mặc định
        };

        private static readonly Dictionary<string, (int Percent, string Name, DeviceCategory Category)> _cache = new();

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var allHid = DeviceList.Local.GetHidDevices().ToList();
            if (allHid.Count == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            var results = new Dictionary<string, DeviceBatteryInfo>();

            // Nhóm theo từng thiết bị/đầu thu vật lý có kênh QMK/VIA Raw HID (0xFF60:0x61)
            var viaGroups = allHid
                .Where(IsViaRawHidEndpoint)
                .GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}_{GetInterfacePrefix(d.DevicePath)}");

            foreach (var group in viaGroups)
            {
                HidDevice? endpoint = group.FirstOrDefault();
                if (endpoint == null) continue;

                string deviceKey = $"QMKVIA_{endpoint.VendorID:X4}_{endpoint.ProductID:X4}";
                var queried = QueryViaBattery(endpoint);

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

            // Dự phòng: đọc báo cáo chuẩn HID Battery Strength (0x06:0x20) cho các dongle Keychron/NuPhy đời mới
            var stdBatGroups = allHid
                .Where(d => KnownQmkVendorIds.Contains(d.VendorID) && HasHidBatteryUsage(d))
                .GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}_{GetInterfacePrefix(d.DevicePath)}");

            foreach (var group in stdBatGroups)
            {
                HidDevice? endpoint = group.FirstOrDefault();
                if (endpoint == null) continue;

                string deviceKey = $"QMKVIA_{endpoint.VendorID:X4}_{endpoint.ProductID:X4}";
                if (results.ContainsKey(deviceKey)) continue;

                int? pct = ReadStandardHidBattery(endpoint);
                if (pct.HasValue && pct.Value >= 1 && pct.Value <= 100)
                {
                    string name = ResolveDeviceName(endpoint);
                    var cat = DetectCategory(name);
                    _cache[deviceKey] = (pct.Value, name, cat);

                    results[deviceKey] = new DeviceBatteryInfo
                    {
                        Id = deviceKey,
                        Name = name,
                        ProviderName = ProviderName,
                        BatteryPercent = pct.Value,
                        IsAsleepOrCached = false,
                        Connection = ConnectionType.UsbWireless24G,
                        Category = cat
                    };
                }
            }

            return results.Values;
        }

        private static bool IsViaRawHidEndpoint(HidDevice dev)
        {
            if (dev.GetMaxOutputReportLength() < 32 || dev.GetMaxInputReportLength() < 32)
                return false;

            try
            {
                return dev.GetReportDescriptor().DeviceItems
                    .Any(item => item.Usages.GetAllValues().Contains(ViaRawHidUsage));
            }
            catch
            {
                return false;
            }
        }

        private static bool HasHidBatteryUsage(HidDevice dev)
        {
            try
            {
                return dev.GetReportDescriptor().DeviceItems
                    .Any(item => item.Usages.GetAllValues().Contains(HidBatteryStrengthUsage));
            }
            catch
            {
                return false;
            }
        }

        private static (int Percent, bool IsCharging, string Name, DeviceCategory Category)? QueryViaBattery(HidDevice device)
        {
            if (!device.TryOpen(out HidStream stream))
                return null;

            using (stream)
            {
                stream.ReadTimeout = 150;
                stream.WriteTimeout = 150;

                int outLen = Math.Max(33, device.GetMaxOutputReportLength());
                int inLen = Math.Max(33, device.GetMaxInputReportLength());
                int outOffset = outLen >= 33 ? 1 : 0;

                // 1. Gửi lệnh VIA id_get_protocol_version (0x01) để xác nhận bàn phím đang thức và phản hồi
                byte[]? verResp = SendViaReadCommand(stream, outLen, inLen, outOffset, 0x01);
                if (verResp == null)
                    return null;

                // 2. Thử chuẩn VIA id_get_keyboard_value (0x02) -> sub-id 0x06 (id_battery_level trên QMK wireless)
                byte[]? batResp = SendViaReadCommand(stream, outLen, inLen, outOffset, 0x02, 0x06);
                if (batResp != null && batResp.Length >= 3 && batResp[2] >= 1 && batResp[2] <= 100)
                {
                    bool charging = batResp.Length >= 4 && batResp[3] == 0x01;
                    string name = ResolveDeviceName(device);
                    return (batResp[2], charging, name, DetectCategory(name));
                }

                // 3. Dự phòng VIA id_custom_get_value (0x08) trên kênh trạng thái không dây (channel 0x00, value 0x01)
                byte[]? customResp = SendViaReadCommand(stream, outLen, inLen, outOffset, 0x08, 0x00, 0x01);
                if (customResp != null && customResp.Length >= 4 && customResp[3] >= 1 && customResp[3] <= 100)
                {
                    bool charging = customResp.Length >= 5 && customResp[4] == 0x01;
                    string name = ResolveDeviceName(device);
                    return (customResp[3], charging, name, DetectCategory(name));
                }
            }

            return null;
        }

        private static byte[]? SendViaReadCommand(
            HidStream stream, int outLen, int inLen, int outOffset, params byte[] payload)
        {
            try
            {
                byte[] req = new byte[outLen];
                if (outOffset == 1) req[0] = 0x00; // ReportID mặc định cho QMK Raw HID

                for (int i = 0; i < payload.Length && (outOffset + i) < req.Length; i++)
                {
                    req[outOffset + i] = payload[i];
                }

                stream.Write(req, 0, req.Length);

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    byte[] raw = new byte[inLen];
                    int read = stream.Read(raw, 0, raw.Length);
                    if (read < 32) continue;

                    int inOffset = read >= 33 ? 1 : 0;
                    if (raw[inOffset] == 0xFF) return null; // VIA trả về 0xFF nếu lệnh không được hỗ trợ

                    bool match = true;
                    for (int i = 0; i < payload.Length; i++)
                    {
                        if (raw[inOffset + i] != payload[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        byte[] trimmed = new byte[read - inOffset];
                        Array.Copy(raw, inOffset, trimmed, 0, trimmed.Length);
                        return trimmed;
                    }
                }
            }
            catch
            {
                // Timeout khi bàn phím đang ở chế độ ngủ tiết kiệm pin
            }

            return null;
        }

        private static int? ReadStandardHidBattery(HidDevice device)
        {
            if (!device.TryOpen(out HidStream stream))
                return null;

            using (stream)
            {
                stream.ReadTimeout = 100;
                try
                {
                    int len = device.GetMaxInputReportLength();
                    if (len < 2) return null;

                    byte[] buf = new byte[len];
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read >= 2 && buf[1] >= 1 && buf[1] <= 100)
                        return buf[1];
                }
                catch { }
            }
            return null;
        }

        private static string ResolveDeviceName(HidDevice device)
        {
            try
            {
                string prod = device.GetProductName();
                if (!string.IsNullOrWhiteSpace(prod) &&
                    !prod.Equals("Keychron Link", StringComparison.OrdinalIgnoreCase) &&
                    !prod.Equals("2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase))
                {
                    return prod.Trim();
                }
            }
            catch { }

            if (device.VendorID == 0x3434)
                return $"Keychron Wireless ({device.ProductID:X4})";
            if (device.VendorID == 0x362D)
                return $"NuPhy Wireless ({device.ProductID:X4})";

            return $"QMK/VIA Keyboard ({device.VendorID:X4}:{device.ProductID:X4})";
        }

        private static string GetInterfacePrefix(string path)
        {
            int idx = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? path[..idx] : path;
        }

        private static DeviceCategory DetectCategory(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("mouse") || n.Contains("m1") || n.Contains("m2") || n.Contains("m3") || n.Contains("m4") || n.Contains("m6") || n.Contains("lemokey g"))
                return DeviceCategory.Mouse;
            return DeviceCategory.Keyboard;
        }
    }
}
