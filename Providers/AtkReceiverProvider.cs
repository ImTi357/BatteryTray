using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HidSharp;

namespace BatteryTray
{
    public class AtkReceiverProvider : IBatteryProvider
    {
        public string ProviderName => "ATK 8K Nearlink USB";

        private const int VendorId = 0x373B;
        private const int ProductId8K = 0x108C;

        private static int? _lastKnownBattery;
        private static string _cachedDeviceName = "ATK X1 Nearlink 8k";

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var hidDevices = DeviceList.Local.GetHidDevices(VendorId, ProductId8K).ToList();
            if (hidDevices.Count == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            var status = QueryAtkBattery(hidDevices);
            if (status.Battery.HasValue)
                _lastKnownBattery = status.Battery.Value;

            int displayBattery = status.Battery ?? _lastKnownBattery ?? -1;
            bool isAsleep = !status.Battery.HasValue && _lastKnownBattery.HasValue;

            return new[]
            {
                new DeviceBatteryInfo
                {
                    Id = $"ATK_{VendorId:X4}_{ProductId8K:X4}",
                    Name = _cachedDeviceName,
                    ProviderName = ProviderName,
                    BatteryPercent = displayBattery,
                    IsAsleepOrCached = isAsleep,
                    Connection = ConnectionType.UsbWireless24G,
                    Category = DeviceCategory.Mouse
                }
            };
        }

        private static (int? Battery, bool IsCharging) QueryAtkBattery(List<HidDevice> hidDevices)
        {
            HidDevice? col05 = hidDevices.FirstOrDefault(d =>
            {
                if (d.GetMaxOutputReportLength() < 64 || d.GetMaxInputReportLength() < 64)
                    return false;
                try { return d.GetReportDescriptor().OutputReports.Any(r => r.ReportID == 0x05); }
                catch { return d.DevicePath.Contains("col05", StringComparison.OrdinalIgnoreCase); }
            });

            if (col05 == null || !col05.TryOpen(out HidStream stream))
                return (null, false);

            using (stream)
            {
                stream.ReadTimeout = 250;
                stream.WriteTimeout = 250;

                try
                {
                    byte[] nameReq = new byte[64];
                    nameReq[0] = 0x05; nameReq[3] = 0x73; nameReq[4] = 0x38;
                    stream.Write(nameReq, 0, nameReq.Length);

                    byte[]? nameResp = ReadMatchingReport(stream, 0x00, 0x73);
                    if (nameResp != null)
                    {
                        string parsedName = Encoding.ASCII.GetString(nameResp, 8, 40).TrimEnd('\0').Trim();
                        if (!string.IsNullOrWhiteSpace(parsedName))
                            _cachedDeviceName = parsedName;
                    }

                    byte[] batReq = new byte[64];
                    batReq[0] = 0x05; batReq[1] = 0x1D; batReq[3] = 0x1A;
                    stream.Write(batReq, 0, batReq.Length);

                    byte[]? batResp = ReadMatchingReport(stream, 0x1D, 0x1A);
                    if (batResp != null && batResp[8] >= 1 && batResp[8] <= 100)
                    {
                        return (batResp[8], batResp[9] == 0x01);
                    }
                }
                catch { }
            }

            return (null, false);
        }

        private static byte[]? ReadMatchingReport(HidStream stream, byte expectedCmd, byte expectedSubCmd)
        {
            for (int i = 0; i < 5; i++)
            {
                byte[] buf = new byte[64];
                int read = stream.Read(buf, 0, buf.Length);
                if (read >= 10 && buf[0] == 0x05 && buf[1] == expectedCmd && buf[3] == expectedSubCmd)
                    return buf;
            }
            return null;
        }
    }
}
