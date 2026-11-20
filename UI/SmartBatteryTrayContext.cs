using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BatteryTray
{
    public class SmartBatteryTrayContext : ApplicationContext
    {
        private readonly BatteryAggregator _aggregator;
        private readonly AppSettings _settings;
        private readonly ContextMenuStrip _menu;
        private readonly TaskbarBatteryBar _taskbarBar;

        private readonly Dictionary<string, NotifyIcon> _trayIcons = new();
        private readonly Dictionary<string, IntPtr> _iconHandles = new();
        private readonly HashSet<string> _alertedLowBattery = new();
        private NotifyIcon? _anchorTrayIcon;

        private List<DeviceBatteryInfo> _allDevices = new();
        private readonly System.Windows.Forms.Timer _pollTimer;

        private const int LowBatteryThreshold = 20;
        private const string StartupAppName = "BatteryTrayMonitor";

        public SmartBatteryTrayContext(BatteryAggregator aggregator)
        {
            _aggregator = aggregator;
            _settings = AppSettings.Load();
            _menu = new ContextMenuStrip();
            _taskbarBar = new TaskbarBatteryBar(_menu);

            _pollTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _pollTimer.Tick += (_, _) => RefreshDevices();
            _pollTimer.Start();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            RefreshDevices();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                ApplyVisualState();
        }

        private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                await Task.Delay(3500);
                RefreshDevices();
            }
        }

        private void RefreshDevices()
        {
            _allDevices = _aggregator.CollectAllDevices()
                .Where(d => d.HasBatteryReading)
                .ToList();

            RebuildContextMenu();
            ApplyVisualState();
        }

        private void RebuildContextMenu()
        {
            _menu.Items.Clear();

            // Tiêu đề hướng dẫn trên cùng giống hệt ảnh mẫu
            var headerItem = new ToolStripMenuItem("Click a device to show or hide it")
            {
                Enabled = false
            };
            _menu.Items.Add(headerItem);

            // Danh sách từng thiết bị (tích chọn để ẩn/hiện)
            var candidateDevices = _settings.HideAsleepDevices
                ? _allDevices.Where(d => !d.IsAsleepOrCached).ToList()
                : _allDevices;

            if (candidateDevices.Count == 0)
            {
                _menu.Items.Add(new ToolStripMenuItem("No active devices detected") { Enabled = false });
            }
            else
            {
                foreach (var dev in candidateDevices)
                {
                    bool isShown = !_settings.HiddenDeviceIds.Contains(dev.Id);
                    string stateSuffix = dev.IsAsleepOrCached ? " (Asleep)" : string.Empty;
                    string label = $"{dev.Name} :  {dev.BatteryPercent}%   [{dev.ConnectionLabel}]{stateSuffix}";

                    var devItem = new ToolStripMenuItem(label)
                    {
                        Checked = isShown
                    };
                    devItem.Click += (_, _) =>
                    {
                        if (_settings.HiddenDeviceIds.Contains(dev.Id))
                            _settings.HiddenDeviceIds.Remove(dev.Id);
                        else
                            _settings.HiddenDeviceIds.Add(dev.Id);

                        _settings.Save();
                        RebuildContextMenu();
                        ApplyVisualState();
                    };
                    _menu.Items.Add(devItem);
                }
            }

            // Tùy chọn ẩn thiết bị đang ngủ / bộ nhớ đệm
            var hideAsleepItem = new ToolStripMenuItem("Hide last known / asleep devices")
            {
                Checked = _settings.HideAsleepDevices
            };
            hideAsleepItem.Click += (_, _) =>
            {
                _settings.HideAsleepDevices = !_settings.HideAsleepDevices;
                _settings.Save();
                RebuildContextMenu();
                ApplyVisualState();
            };
            _menu.Items.Add(hideAsleepItem);

            _menu.Items.Add(new ToolStripSeparator());

            // Chế độ 1: Hiện trực tiếp trên Taskbar (icon + phần trăm)
            var showTaskbarItem = new ToolStripMenuItem("Show on the taskbar (icon + percentage)")
            {
                Checked = _settings.ShowOnTaskbar
            };
            showTaskbarItem.Click += (_, _) =>
            {
                _settings.ShowOnTaskbar = !_settings.ShowOnTaskbar;
                _settings.Save();
                RebuildContextMenu();
                ApplyVisualState();
            };
            _menu.Items.Add(showTaskbarItem);

            // Chế độ 2: Hiện dưới dạng icon trong khay hệ thống (System Tray)
            var showTrayItem = new ToolStripMenuItem("Show as tray icons")
            {
                Checked = !_settings.ShowOnTaskbar
            };
            showTrayItem.Click += (_, _) =>
            {
                _settings.ShowOnTaskbar = false;
                _settings.Save();
                RebuildContextMenu();
                ApplyVisualState();
            };
            _menu.Items.Add(showTrayItem);

            // Tùy chọn nền mờ phía sau chữ (cho Taskbar trong suốt)
            var bgPillItem = new ToolStripMenuItem("Background behind taskbar text (for transparent taskbars)")
            {
                Checked = _settings.ShowBackgroundPill
            };
            bgPillItem.Click += (_, _) =>
            {
                _settings.ShowBackgroundPill = !_settings.ShowBackgroundPill;
                _settings.Save();
                RebuildContextMenu();
                ApplyVisualState();
            };
            _menu.Items.Add(bgPillItem);

            _menu.Items.Add(new ToolStripSeparator());

            // Làm mới ngay
            _menu.Items.Add("Refresh now", null, (_, _) => RefreshDevices());

            // Khởi động cùng Windows
            var startupItem = new ToolStripMenuItem("Start with Windows")
            {
                Checked = IsStartupEnabled()
            };
            startupItem.Click += (_, _) =>
            {
                SetStartup(!IsStartupEnabled());
                RebuildContextMenu();
            };
            _menu.Items.Add(startupItem);

            _menu.Items.Add(new ToolStripSeparator());

            // Thoát
            _menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        }

        private void ApplyVisualState()
        {
            bool isDarkTaskbar = IsSystemDarkTheme();

            var visibleDevices = _allDevices
                .Where(d => !_settings.HideAsleepDevices || !d.IsAsleepOrCached)
                .Where(d => !_settings.HiddenDeviceIds.Contains(d.Id))
                .ToList();

            // Kiểm tra cảnh báo pin yếu
            foreach (var dev in visibleDevices)
            {
                if (dev.BatteryPercent <= LowBatteryThreshold && !_alertedLowBattery.Contains(dev.Id))
                {
                    _alertedLowBattery.Add(dev.Id);
                    (_anchorTrayIcon ?? _trayIcons.Values.FirstOrDefault())?.ShowBalloonTip(
                        4000,
                        $"Low Battery: {dev.Name}",
                        $"{dev.Name} is at {dev.BatteryPercent}%.",
                        ToolTipIcon.Warning
                    );
                }
                else if (dev.BatteryPercent > LowBatteryThreshold)
                {
                    _alertedLowBattery.Remove(dev.Id);
                }
            }

            if (_settings.ShowOnTaskbar)
            {
                // Ẩn các icon số rời trong System Tray, hiển thị thanh [Icon + %] ngay trên Taskbar
                ClearAllTrayIcons();
                _taskbarBar.UpdateDisplay(visibleDevices, isDarkTaskbar, _settings.ShowBackgroundPill);

                // Giữ 1 icon neo nhỏ trong System Tray để luôn mở được menu ngay cả khi ẩn hết thiết bị
                EnsureAnchorTrayIcon(isDarkTaskbar);
            }
            else
            {
                // Ẩn thanh trên Taskbar, hiển thị từng icon số riêng trong System Tray
                _taskbarBar.Hide();
                RemoveAnchorTrayIcon();
                RenderTrayIconsMode(visibleDevices, isDarkTaskbar);
            }
        }

        private void EnsureAnchorTrayIcon(bool isDarkTaskbar)
        {
            if (_anchorTrayIcon == null)
            {
                _anchorTrayIcon = new NotifyIcon
                {
                    Visible = true,
                    Text = "Battery Tray Monitor (Click to configure)",
                    ContextMenuStrip = _menu
                };
                _anchorTrayIcon.MouseUp += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                        _menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
                };
            }
            RenderSingleIcon(_anchorTrayIcon, "ANCHOR", "⚡", "", 100, isDarkTaskbar);
        }

        private void RemoveAnchorTrayIcon()
        {
            if (_anchorTrayIcon != null)
            {
                _anchorTrayIcon.Visible = false;
                _anchorTrayIcon.Dispose();
                _anchorTrayIcon = null;
            }
            if (_iconHandles.Remove("ANCHOR", out IntPtr h) && h != IntPtr.Zero)
                NativeMethods.DestroyIcon(h);
        }

        private void RenderTrayIconsMode(List<DeviceBatteryInfo> devices, bool isDarkTaskbar)
        {
            if (devices.Count == 0)
            {
                ClearAllTrayIcons();
                EnsureAnchorTrayIcon(isDarkTaskbar);
                return;
            }

            var activeIds = devices.Select(d => d.Id).ToHashSet();
            foreach (var oldId in _trayIcons.Keys.Where(id => !activeIds.Contains(id)).ToList())
            {
                RemoveTrayIcon(oldId);
            }

            foreach (var dev in devices)
            {
                if (!_trayIcons.TryGetValue(dev.Id, out var icon))
                {
                    icon = new NotifyIcon { Visible = true, ContextMenuStrip = _menu };
                    icon.MouseUp += (_, e) =>
                    {
                        if (e.Button == MouseButtons.Left)
                            _menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
                    };
                    _trayIcons[dev.Id] = icon;
                }

                string tip = $"{dev.Name}: {dev.BatteryPercent}% [{dev.ConnectionLabel}]";
                icon.Text = tip.Length > 63 ? tip[..63] : tip;
                RenderSingleIcon(icon, dev.Id, dev.BatteryPercent.ToString(), dev.ShortBadge, dev.BatteryPercent, isDarkTaskbar);
            }
        }

        private void RenderSingleIcon(NotifyIcon notifyIcon, string key, string percentText, string badge, int percent, bool isDarkTaskbar)
        {
            Size iconSize = SystemInformation.SmallIconSize;
            int w = Math.Max(16, iconSize.Width);
            int h = Math.Max(16, iconSize.Height);

            Color primaryColor = percent <= LowBatteryThreshold
                ? Color.FromArgb(255, 82, 82)
                : (isDarkTaskbar ? Color.White : Color.FromArgb(24, 24, 24));

            using var bitmap = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                if (!string.IsNullOrEmpty(badge) && percentText != "100")
                {
                    using var badgeFont = new Font("Segoe UI", w * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var badgeBrush = new SolidBrush(Color.FromArgb(220, primaryColor));
                    g.DrawString(badge, badgeFont, badgeBrush, 0, -1);
                }

                float mainFontSize = percentText.Length >= 3 ? w * 0.46f : w * 0.56f;
                using var mainFont = new Font("Segoe UI", mainFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(primaryColor);

                SizeF textDim = g.MeasureString(percentText, mainFont);
                float x = (w - textDim.Width) / 2f + (percentText.Length < 3 && !string.IsNullOrEmpty(badge) ? w * 0.08f : 0);
                float y = (h - textDim.Height) / 2f - (h * 0.06f);
                g.DrawString(percentText, mainFont, textBrush, x, y);

                int barHeight = Math.Max(2, h / 8);
                int barY = h - barHeight;
                int margin = Math.Max(1, w / 16);
                int maxBarW = w - (margin * 2);
                int fillW = (int)Math.Round(maxBarW * (Math.Clamp(percent, 0, 100) / 100.0));

                using var trackBrush = new SolidBrush(Color.FromArgb(75, primaryColor));
                using var fillBrush = new SolidBrush(primaryColor);
                g.FillRectangle(trackBrush, margin, barY, maxBarW, barHeight);
                g.FillRectangle(fillBrush, margin, barY, fillW, barHeight);
            }

            IntPtr newHandle = bitmap.GetHicon();
            notifyIcon.Icon = Icon.FromHandle(newHandle);

            if (_iconHandles.TryGetValue(key, out IntPtr oldHandle) && oldHandle != IntPtr.Zero)
                NativeMethods.DestroyIcon(oldHandle);
            _iconHandles[key] = newHandle;
        }

        private void RemoveTrayIcon(string id)
        {
            if (_trayIcons.TryGetValue(id, out var icon))
            {
                icon.Visible = false;
                icon.Dispose();
                _trayIcons.Remove(id);
            }
            if (_iconHandles.TryGetValue(id, out IntPtr handle) && handle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(handle);
                _iconHandles.Remove(id);
            }
        }

        private void ClearAllTrayIcons()
        {
            foreach (var id in _trayIcons.Keys.ToList()) RemoveTrayIcon(id);
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("SystemUsesLightTheme") is int val) return val == 0;
            }
            catch { }
            return true;
        }

        private static bool IsStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(StartupAppName) != null;
        }

        private static void SetStartup(bool enable)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath)) key.SetValue(StartupAppName, $"\"{exePath}\"");
            }
            else key.DeleteValue(StartupAppName, false);
        }

        private void ExitApplication()
        {
            _pollTimer.Stop();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _taskbarBar.Close();
            _taskbarBar.Dispose();
            RemoveAnchorTrayIcon();
            ClearAllTrayIcons();
            Application.Exit();
        }
    }
}

