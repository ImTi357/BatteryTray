using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HidSharp;

namespace BatteryTray
{
    public class AttackSharkProvider : IBatteryProvider
    {
        public string ProviderName => "Attack Shark Wireless HID (Beken OEM)";

        private const int VidBekenOem = 0x1D57;

        private static readonly Dictionary<string, string> KnownDevices = new()
        {
            ["1D57:FA60"] = "Attack Shark / Kysona Mouse (2.4GHz)",
            ["1D57:FA55"] = "Attack Shark Mouse (Wired)",
            ["1D57:FA61"] = "Attack Shark Wireless Mouse",
            ["1D57:FA65"] = "Attack Shark / Kysona Wireless"
        };

        private static readonly ConcurrentDictionary<string, (int Percent, string Name, DateTime LastSeenUtc)> _cache = new();
        private static readonly ConcurrentDictionary<string, byte> _activeListeners = new();

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var devices = DeviceList.Local.GetHidDevices(VidBekenOem).ToList();
            if (devices.Count == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            var endpoints = devices
                .Where(IsBekenBatteryEndpoint)
                .GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}");

            var results = new List<DeviceBatteryInfo>();

            foreach (var group in endpoints)
            {
                var endpoint = group.First();
                string deviceKey = $"ATTACKSHARK_{endpoint.VendorID:X4}_{endpoint.ProductID:X4}";

                EnsureListenerRunning(deviceKey, endpoint);

                if (_cache.TryGetValue(deviceKey, out var info))
                {
                    bool stale = (DateTime.UtcNow - info.LastSeenUtc).TotalMinutes > 10;
                    results.Add(new DeviceBatteryInfo
                    {
                        Id = deviceKey,
                        Name = info.Name,
                        ProviderName = ProviderName,
                        BatteryPercent = info.Percent,
                        IsAsleepOrCached = stale,
                        Connection = ConnectionType.UsbWireless24G,
                        Category = DeviceCategory.Mouse
                    });
                }
            }

            return results;
        }

        private static bool IsBekenBatteryEndpoint(HidDevice dev)
        {
            if (dev.GetMaxInputReportLength() < 5)
                return false;

            try
            {
                return dev.GetReportDescriptor().InputReports.Any(r => r.ReportID == 0x03);
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureListenerRunning(string deviceKey, HidDevice device)
        {
            if (!_activeListeners.TryAdd(deviceKey, 1))
                return;

            var thread = new Thread(() => ListenLoop(deviceKey, device))
            {
                IsBackground = true,
                Name = $"BatteryTray_{deviceKey}"
            };
            thread.Start();
        }

        private static void ListenLoop(string deviceKey, HidDevice device)
        {
            try
            {
                if (!device.TryOpen(out HidStream stream))
                    return;

                using (stream)
                {
                    stream.ReadTimeout = 2000;
                    int inLen = Math.Max(5, device.GetMaxInputReportLength());
                    byte[] buf = new byte[inLen];
                    string name = ResolveName(device);

                    while (true)
                    {
                        try
                        {
                            int read = stream.Read(buf, 0, buf.Length);
                            if (read >= 5 && buf[0] == 0x03 && buf[1] == 0x55 && buf[2] == 0x40 && buf[3] == 0x01)
                            {
                                int pct = buf[4];
                                if (pct >= 1 && pct <= 100)
                                {
                                    _cache[deviceKey] = (pct, name, DateTime.UtcNow);
                                }
                            }
                        }
                        catch (TimeoutException) { }
                    }
                }
            }
            catch { }
            finally
            {
                _activeListeners.TryRemove(deviceKey, out _);
            }
        }

        private static string ResolveName(HidDevice device)
        {
            string vidPid = $"{device.VendorID:X4}:{device.ProductID:X4}";
            if (KnownDevices.TryGetValue(vidPid, out string? mapped))
                return mapped;

            try
            {
                string prod = device.GetProductName();
                if (!string.IsNullOrWhiteSpace(prod) &&
                    !prod.Equals("2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase) &&
                    !prod.Equals("USB Receiver", StringComparison.OrdinalIgnoreCase))
                {
                    return prod.Trim();
                }
            }
            catch { }

            return $"Attack Shark Mouse ({device.ProductID:X4})";
        }
    }
}
