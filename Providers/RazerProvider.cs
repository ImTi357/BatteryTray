using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HidSharp;

namespace BatteryTray
{
    public class RazerProvider : IBatteryProvider
    {
        public string ProviderName => "Razer OpenRazer HID";

        private const int VendorId = 0x1532;
        private const byte CommandClassPower = 0x07;
        private const byte CmdGetBatteryLevel = 0x80;
        private const byte CmdGetChargingStatus = 0x84;

        private static readonly Dictionary<string, (int Percent, string Name, DeviceCategory Category)> _cache = new();

        // Bảng ánh xạ một số mã PID đầu thu/chuột không dây Razer phổ biến từ OpenRazer
        private static readonly Dictionary<int, string> KnownDevices = new()
        {
            [0x007B] = "Razer Viper Ultimate (Wireless)",
            [0x007D] = "Razer DeathAdder V2 Pro (Wireless)",
            [0x0090] = "Razer Naga Pro (Wireless)",
            [0x0094] = "Razer Orochi V2",
            [0x00A6] = "Razer Viper V2 Pro (Wireless)",
            [0x00AB] = "Razer Basilisk V3 Pro (Wireless)",
            [0x00B7] = "Razer DeathAdder V3 Pro (Wireless)",
            [0x00B9] = "Razer Viper V3 HyperSpeed",
            [0x00C0] = "Razer Cobra Pro (Wireless)",
            [0x00C1] = "Razer Viper V3 Pro (Wireless)",
            [0x00C3] = "Razer DeathAdder V3 HyperSpeed (Wireless)",
            [0x025A] = "Razer BlackWidow V3 Mini HyperSpeed",
            [0x0296] = "Razer DeathStalker V2 Pro"
        };

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var hidDevices = DeviceList.Local.GetHidDevices(VendorId).ToList();
            if (hidDevices.Count == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            var results = new Dictionary<string, DeviceBatteryInfo>();

            // Nhóm theo từng đầu thu/thiết bị vật lý có Feature Report >= 90 byte
            var groups = hidDevices
                .Where(d => d.GetMaxFeatureReportLength() >= 90)
                .GroupBy(d => $"{d.ProductID:X4}_{GetInterfacePrefix(d.DevicePath)}");

            foreach (var group in groups)
            {
                HidDevice? endpoint = group.FirstOrDefault();
                if (endpoint == null) continue;

                string deviceKey = $"RAZER_{endpoint.ProductID:X4}";
                var queried = QueryRazerBattery(endpoint);

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

        private static (int Percent, bool IsCharging, string Name, DeviceCategory Category)? QueryRazerBattery(HidDevice device)
        {
            if (!device.TryOpen(out HidStream stream))
                return null;

            using (stream)
            {
                int maxFeatureLen = device.GetMaxFeatureReportLength();
                int offset = maxFeatureLen >= 91 ? 1 : 0; // Byte 0 là ReportID 0x00 trên Windows

                // Các dòng Razer dùng Transaction ID 0x1F hoặc 0x3F tùy thế hệ đầu thu
                byte[] transactionIds = { 0x1F, 0x3F };

                foreach (byte txId in transactionIds)
                {
                    byte[]? batResp = SendRazerFeature(stream, maxFeatureLen, offset, txId, CommandClassPower, CmdGetBatteryLevel);
                    if (batResp == null) continue;

                    // arguments[1] nằm ở byte thứ 9 của gói 90-byte (offset + 9)
                    byte rawBattery = batResp[offset + 9];
                    if (rawBattery == 0)
                    {
                        // Khi chuột tắt hoặc ngủ sâu, dongle trả về 0 -> giữ trạng thái ngủ
                        return null;
                    }

                    int percent = Math.Clamp((int)Math.Round(rawBattery * 100.0 / 255.0), 1, 100);

                    bool isCharging = false;
                    byte[]? chgResp = SendRazerFeature(stream, maxFeatureLen, offset, txId, CommandClassPower, CmdGetChargingStatus);
                    if (chgResp != null && chgResp[offset + 9] == 0x01)
                    {
                        isCharging = true;
                    }

                    string name = ResolveDeviceName(device);
                    return (percent, isCharging, name, DetectCategory(name));
                }
            }

            return null;
        }

        private static byte[]? SendRazerFeature(
            HidStream stream, int bufferLen, int offset, byte txId, byte commandClass, byte commandId)
        {
            try
            {
                byte[] req = new byte[bufferLen];
                if (offset == 1) req[0] = 0x00; // Report ID

                req[offset + 0] = 0x00;         // Status: New Command
                req[offset + 1] = txId;         // Transaction ID
                req[offset + 2] = 0x00;         // Remaining packets (High)
                req[offset + 3] = 0x00;         // Remaining packets (Low)
                req[offset + 4] = 0x00;         // Protocol type
                req[offset + 5] = 0x02;         // Data size (2 bytes arguments)
                req[offset + 6] = commandClass; // Command class (0x07 = Power)
                req[offset + 7] = commandId;    // Command ID (0x80 / 0x84)
                req[offset + 88] = CalculateCrc(req, offset);

                stream.SetFeature(req);
                Thread.Sleep(15); // Độ trễ phản hồi chuẩn của đầu thu Razer (10-20ms)

                byte[] resp = new byte[bufferLen];
                if (offset == 1) resp[0] = 0x00;
                stream.GetFeature(resp);

                // Status 0x02 = Successful response
                if (resp[offset + 0] == 0x02 &&
                    resp[offset + 6] == commandClass &&
                    resp[offset + 7] == commandId)
                {
                    return resp;
                }
            }
            catch
            {
                // Bỏ qua nếu endpoint không hỗ trợ Feature Report này
            }

            return null;
        }

        // Tính checksum XOR từ byte 2 đến byte 87 của cấu trúc razer_report 90-byte
        private static byte CalculateCrc(byte[] report, int offset)
        {
            byte crc = 0;
            for (int i = 2; i < 88; i++)
            {
                crc ^= report[offset + i];
            }
            return crc;
        }

        private static string ResolveDeviceName(HidDevice device)
        {
            if (KnownDevices.TryGetValue(device.ProductID, out string? mapped))
                return mapped;

            try
            {
                string prod = device.GetProductName();
                if (!string.IsNullOrWhiteSpace(prod) && !prod.Contains("Control", StringComparison.OrdinalIgnoreCase))
                    return prod.Trim();
            }
            catch { }

            return $"Razer Wireless ({device.ProductID:X4})";
        }

        private static string GetInterfacePrefix(string path)
        {
            int idx = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? path[..idx] : path;
        }

        private static DeviceCategory DetectCategory(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("keyboard") || n.Contains("blackwidow") || n.Contains("deathstalker") || n.Contains("huntsman"))
                return DeviceCategory.Keyboard;
            if (n.Contains("headset") || n.Contains("blackshark") || n.Contains("barracuda") || n.Contains("kraken"))
                return DeviceCategory.Audio;
            return DeviceCategory.Mouse;
        }
    }
}
