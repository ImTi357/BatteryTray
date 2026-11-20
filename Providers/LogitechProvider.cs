using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HidSharp;
using HidSharp.Reports;

namespace BatteryTray
{
    public class LogitechProvider : IBatteryProvider
    {
        public string ProviderName => "Logitech HID++ 2.0";

        private const int VendorId = 0x046D;
        private const byte SoftwareId = 0x0A; // Định danh gói tin của BatteryTray

        // Mã tính năng chuẩn trong đặc tả Logitech HID++ 2.0
        private const ushort FeatureRoot = 0x0000;
        private const ushort FeatureDeviceName = 0x0005;
        private const ushort FeatureBatteryStatus = 0x1000;   // G304/G305, Unifying
        private const ushort FeatureUnifiedBattery = 0x1004;  // Lightspeed, Bolt, MX series

        private static readonly Dictionary<string, (int Percent, string Name, DeviceCategory Category)> _cache = new();

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var hidDevices = DeviceList.Local.GetHidDevices(VendorId).ToList();
            if (hidDevices.Count == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            var results = new Dictionary<string, DeviceBatteryInfo>();

            // Nhóm theo từng đầu thu vật lý (PID + đường dẫn gốc)
            var receiverGroups = hidDevices
                .Where(IsHidPlusPlusEndpoint)
                .GroupBy(d => $"{d.ProductID:X4}_{GetInterfacePrefix(d.DevicePath)}");

            foreach (var group in receiverGroups)
            {
                // Ưu tiên gói dài 20-byte (ReportID 0x11), dự phòng gói ngắn 7-byte (ReportID 0x10)
                HidDevice? endpoint = group
                    .OrderByDescending(d => d.GetMaxOutputReportLength() >= 20 ? 1 : 0)
                    .FirstOrDefault();

                if (endpoint == null || !endpoint.TryOpen(out HidStream stream))
                    continue;

                using (stream)
                {
                    stream.ReadTimeout = 150;
                    stream.WriteTimeout = 150;

                    byte reportId = endpoint.GetMaxOutputReportLength() >= 20 ? (byte)0x11 : (byte)0x10;
                    int reportLen = reportId == 0x11 ? 20 : 7;

                    // Đầu thu Unifying/Bolt hỗ trợ tối đa 6 thiết bị (slot 1..6), Lightspeed dùng slot 1
                    for (byte devIdx = 1; devIdx <= 6; devIdx++)
                    {
                        string deviceKey = $"LOGI_{endpoint.ProductID:X4}_SLOT{devIdx}";
                        var queried = QuerySlotBattery(stream, reportId, reportLen, devIdx, endpoint.ProductID);

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
                }
            }

            return results.Values;
        }

        private static bool IsHidPlusPlusEndpoint(HidDevice dev)
        {
            int maxOut = dev.GetMaxOutputReportLength();
            int maxIn = dev.GetMaxInputReportLength();
            if (maxOut < 7 || maxIn < 7) return false;

            try
            {
                var outIds = dev.GetReportDescriptor().OutputReports.Select(r => r.ReportID).ToHashSet();
                return outIds.Contains(0x10) || outIds.Contains(0x11);
            }
            catch
            {
                return false;
            }
        }

        private static string GetInterfacePrefix(string path)
        {
            int idx = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? path[..idx] : path;
        }

        private static (int Percent, bool IsCharging, string Name, DeviceCategory Category)? QuerySlotBattery(
            HidStream stream, byte reportId, int reportLen, byte devIdx, int productId)
        {
            // 1. Thử tìm tính năng Unified Battery (0x1004) trước
            byte? unifiedIdx = ResolveFeatureIndex(stream, reportId, reportLen, devIdx, FeatureUnifiedBattery);
            if (unifiedIdx.HasValue && unifiedIdx.Value > 0)
            {
                byte[]? resp = SendHidppCommand(stream, reportId, reportLen, devIdx, unifiedIdx.Value, 0x01);
                if (resp != null && resp.Length >= 7 && resp[4] >= 1 && resp[4] <= 100)
                {
                    bool charging = resp[6] == 1 || resp[6] == 2 || resp[6] == 3;
                    string name = ReadDeviceName(stream, reportId, reportLen, devIdx, productId);
                    return (resp[4], charging, name, DetectCategory(name));
                }
            }

            // 2. Dự phòng tính năng Battery Status (0x1000) trên G304/G305/Unifying
            byte? statusIdx = ResolveFeatureIndex(stream, reportId, reportLen, devIdx, FeatureBatteryStatus);
            if (statusIdx.HasValue && statusIdx.Value > 0)
            {
                byte[]? resp = SendHidppCommand(stream, reportId, reportLen, devIdx, statusIdx.Value, 0x00);
                if (resp != null && resp.Length >= 7 && resp[4] >= 1 && resp[4] <= 100)
                {
                    bool charging = resp[6] == 1 || resp[6] == 2;
                    string name = ReadDeviceName(stream, reportId, reportLen, devIdx, productId);
                    return (resp[4], charging, name, DetectCategory(name));
                }
            }

            return null;
        }

        private static byte? ResolveFeatureIndex(HidStream stream, byte reportId, int reportLen, byte devIdx, ushort featureId)
        {
            byte hi = (byte)(featureId >> 8);
            byte lo = (byte)(featureId & 0xFF);
            byte[]? resp = SendHidppCommand(stream, reportId, reportLen, devIdx, (byte)FeatureRoot, 0x00, hi, lo);
            if (resp != null && resp.Length >= 5 && resp[4] > 0)
                return resp[4];
            return null;
        }

        private static string ReadDeviceName(HidStream stream, byte reportId, int reportLen, byte devIdx, int productId)
        {
            try
            {
                byte[]? lenResp = SendHidppCommand(stream, reportId, reportLen, devIdx, (byte)FeatureDeviceName, 0x00);
                // Nếu chưa biết index của 0x0005, tra qua Root 0x0000
                byte? nameIdx = ResolveFeatureIndex(stream, reportId, reportLen, devIdx, FeatureDeviceName);
                if (!nameIdx.HasValue || nameIdx.Value == 0)
                    return $"Logitech Wireless ({productId:X4})";

                lenResp = SendHidppCommand(stream, reportId, reportLen, devIdx, nameIdx.Value, 0x00);
                if (lenResp == null || lenResp.Length < 5 || lenResp[4] == 0)
                    return $"Logitech Wireless ({productId:X4})";

                int totalLen = Math.Min((int)lenResp[4], 40);
                var sb = new StringBuilder();

                while (sb.Length < totalLen)
                {
                    byte[]? chunk = SendHidppCommand(stream, reportId, reportLen, devIdx, nameIdx.Value, 0x01, (byte)sb.Length);
                    if (chunk == null || chunk.Length <= 4) break;

                    int payloadBytes = chunk.Length - 4;
                    int take = Math.Min(payloadBytes, totalLen - sb.Length);
                    sb.Append(Encoding.ASCII.GetString(chunk, 4, take));
                }

                string parsed = sb.ToString().TrimEnd('\0').Trim();
                return string.IsNullOrWhiteSpace(parsed) ? $"Logitech Wireless ({productId:X4})" : parsed;
            }
            catch
            {
                return $"Logitech Wireless ({productId:X4})";
            }
        }

        private static byte[]? SendHidppCommand(
            HidStream stream, byte reportId, int reportLen, byte devIdx, byte featureIdx, byte functionId, params byte[] args)
        {
            try
            {
                byte[] req = new byte[reportLen];
                req[0] = reportId;
                req[1] = devIdx;
                req[2] = featureIdx;
                req[3] = (byte)((functionId << 4) | SoftwareId);
                for (int i = 0; i < args.Length && (4 + i) < req.Length; i++)
                {
                    req[4 + i] = args[i];
                }

                stream.Write(req, 0, req.Length);

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    byte[] resp = new byte[stream.Device.GetMaxInputReportLength()];
                    int read = stream.Read(resp, 0, resp.Length);
                    if (read < 5) continue;
                    if (resp[1] != devIdx) continue;

                    // Mã lỗi HID++ 2.0 (0x8F) hoặc HID++ 1.0 (0xFF) -> thiết bị đang ngủ hoặc không hỗ trợ
                    if (resp[2] == 0x8F || resp[2] == 0xFF)
                        return null;

                    if (resp[2] == featureIdx && resp[3] == req[3])
                        return resp;
                }
            }
            catch
            {
                // Timeout khi thiết bị đang ngủ hoặc slot trống
            }
            return null;
        }

        private static DeviceCategory DetectCategory(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("keyboard") || n.Contains("mx keys") || n.Contains("k380") || n.Contains("g915") || n.Contains("g715") || n.Contains("pro x tkl"))
                return DeviceCategory.Keyboard;
            if (n.Contains("headset") || n.Contains("g733") || n.Contains("g535") || n.Contains("pro x 2"))
                return DeviceCategory.Audio;
            return DeviceCategory.Mouse;
        }
    }
}
