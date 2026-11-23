using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using HidSharp;

namespace BatteryTray
{
    public static class DiagnosticExporter
    {
        private const string MenuItemTag = "ExportDiagnosticLog";
        private static bool _isExporting = false;

        public static void EnsureMenuItem(
            ContextMenuStrip menu,
            Func<List<DeviceBatteryInfo>> getCurrentDevices,
            Control uiInvoker)
        {
            if (menu.Items.Cast<ToolStripItem>().Any(i => Equals(i.Tag, MenuItemTag)))
                return;

            var exportItem = new ToolStripMenuItem("Export device diagnostic log (GitHub)")
            {
                Tag = MenuItemTag
            };

            exportItem.Click += (_, _) => TriggerExportAsync(getCurrentDevices(), uiInvoker);

            // Chèn ngay sau mục "Refresh now" (hoặc trước "Start with Windows" / "Exit")
            int insertIndex = menu.Items.Count;
            for (int i = 0; i < menu.Items.Count; i++)
            {
                string txt = menu.Items[i].Text ?? string.Empty;
                if (txt.Contains("Refresh", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = i + 1;
                    break;
                }
                if (txt.Contains("Start with Windows", StringComparison.OrdinalIgnoreCase) ||
                    txt.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = i;
                    break;
                }
            }

            menu.Items.Insert(insertIndex, exportItem);
        }

        private static void TriggerExportAsync(List<DeviceBatteryInfo> snapshotDevices, Control uiInvoker)
        {
            if (_isExporting) return;
            _isExporting = true;

            Task.Run(() =>
            {
                try
                {
                    string report = BuildDiagnosticReport(snapshotDevices);
                    string filePath = SaveReportFile(report);

                    void NotifyOnUi()
                    {
                        try { Clipboard.SetText(report); } catch { }

                        try
                        {
                            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{filePath}\"")
                            {
                                UseShellExecute = true
                            });
                        }
                        catch { }

                        MessageBox.Show(
                            $"Đã xuất log chẩn đoán thiết bị thành công!\n\n" +
                            $"Đường dẫn: {filePath}\n\n" +
                            $"Nội dung log đã được sao chép sẵn vào Clipboard và mở bằng Notepad.\n" +
                            $"Bạn chỉ cần dán (Ctrl + V) vào mục Issues trên GitHub để yêu cầu hỗ trợ thiết bị mới.",
                            "BatteryTray - Diagnostic Log",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }

                    if (uiInvoker.IsHandleCreated && !uiInvoker.IsDisposed)
                        uiInvoker.BeginInvoke((Action)NotifyOnUi);
                    else
                        NotifyOnUi();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Không thể xuất log chẩn đoán: {ex.Message}",
                        "BatteryTray",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                finally
                {
                    _isExporting = false;
                }
            });
        }

        private static string BuildDiagnosticReport(List<DeviceBatteryInfo> currentDevices)
        {
            var sb = new StringBuilder();
            string appVer = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

            sb.AppendLine("=== BatteryTray Device Diagnostic Log ===");
            sb.AppendLine($"Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"App Version    : {appVer}");
            sb.AppendLine($"OS             : {Environment.OSVersion}");
            sb.AppendLine();

            sb.AppendLine("--- 1. Currently Detected Devices ---");
            if (currentDevices.Count == 0)
            {
                sb.AppendLine("(None)");
            }
            else
            {
                foreach (var d in currentDevices)
                {
                    sb.AppendLine($"- [{d.Category}] {d.Name} | {d.BatteryPercent}% | {d.Connection} | Provider: {d.ProviderName} | Cached: {d.IsAsleepOrCached}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("--- 2. Connected HID Devices (Grouped by VID:PID) ---");
            var hidDevices = DeviceList.Local.GetHidDevices()
                .OrderBy(d => d.VendorID)
                .ThenBy(d => d.ProductID)
                .ToList();

            var grouped = hidDevices.GroupBy(d => $"{d.VendorID:X4}:{d.ProductID:X4}");
            foreach (var group in grouped)
            {
                var first = group.First();
                string mfg = SafeCall(() => first.GetManufacturer());
                string prod = SafeCall(() => first.GetProductName());

                sb.AppendLine($"[VID:PID = {group.Key}] Manufacturer=\"{mfg}\" | Product=\"{prod}\" | Interfaces={group.Count()}");

                int idx = 0;
                foreach (var dev in group)
                {
                    idx++;
                    int maxIn = SafeCall(() => dev.GetMaxInputReportLength(), -1);
                    int maxOut = SafeCall(() => dev.GetMaxOutputReportLength(), -1);
                    int maxFeat = SafeCall(() => dev.GetMaxFeatureReportLength(), -1);

                    string inIds = "-";
                    string outIds = "-";
                    string featIds = "-";
                    string usages = "-";

                    try
                    {
                        var desc = dev.GetReportDescriptor();
                        inIds = FormatReportIds(desc.InputReports.Select(r => r.ReportID));
                        outIds = FormatReportIds(desc.OutputReports.Select(r => r.ReportID));
                        featIds = FormatReportIds(desc.FeatureReports.Select(r => r.ReportID));

                        var usagePairs = desc.DeviceItems
                            .SelectMany(item => item.Usages.GetAllValues())
                            .Distinct()
                            .Take(8)
                            .Select(u => $"0x{(u >> 16):X4}:0x{(u & 0xFFFF):X4}");

                        string joined = string.Join(", ", usagePairs);
                        if (!string.IsNullOrEmpty(joined)) usages = joined;
                    }
                    catch
                    {
                        usages = "(Descriptor restricted)";
                    }

                    string shortPath = SanitizeDevicePath(dev.DevicePath);
                    sb.AppendLine($"  #{idx} {shortPath}");
                    sb.AppendLine($"      Len(In/Out/Feat)={maxIn}/{maxOut}/{maxFeat} | ReportIDs(In:[{inIds}] Out:[{outIds}] Feat:[{featIds}])");
                    sb.AppendLine($"      Usages(Page:Id)=[{usages}]");
                }
            }
            sb.AppendLine();

            sb.AppendLine("--- 3. Connected Wireless Audio / Bluetooth PnP Summary ---");
            sb.AppendLine(CollectAudioAndBluetoothPnpSummary());

            return sb.ToString();
        }

        private static string CollectAudioAndBluetoothPnpSummary()
        {
            try
            {
                string psScript =
                    "Get-PnpDevice -PresentOnly | " +
                    "Where-Object { ($_.Class -in @('MEDIA','AudioEndpoint','Bluetooth')) -and ($_.InstanceId -match 'VID_[0-9A-F]{4}&PID_[0-9A-F]{4}|BTHENUM|BTHLE') } | " +
                    "Select-Object -First 40 Class, FriendlyName, InstanceId | " +
                    "ForEach-Object { \"[$($_.Class)] $($_.FriendlyName) | $($_.InstanceId -replace '\\\\[^\\\\]+$', '')\" }";

                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(4000);

                return string.IsNullOrWhiteSpace(output) ? "(No wireless audio/Bluetooth entries found)" : output.Trim();
            }
            catch
            {
                return "(Skipped PnP audio summary)";
            }
        }

        private static string SaveReportFile(string content)
        {
            string fileName = $"BatteryTray_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string targetDir = Directory.Exists(desktop) ? desktop : Path.GetTempPath();
            string fullPath = Path.Combine(targetDir, fileName);

            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return fullPath;
        }

        private static string FormatReportIds(IEnumerable<byte> ids)
        {
            var list = ids.Distinct().OrderBy(b => b).Select(b => $"0x{b:X2}").ToList();
            return list.Count == 0 ? "-" : string.Join(",", list);
        }

        private static string SanitizeDevicePath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "(unknown)";
            var match = Regex.Match(path, @"(vid_[0-9a-f]{4}&pid_[0-9a-f]{4}[^#]*)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "(hid-endpoint)";
        }

        private static string SafeCall(Func<string> getter)
        {
            try
            {
                string val = getter();
                return string.IsNullOrWhiteSpace(val) ? "" : val.Trim();
            }
            catch { return ""; }
        }

        private static int SafeCall(Func<int> getter, int fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }
    }
}
