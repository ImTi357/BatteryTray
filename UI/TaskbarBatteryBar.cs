using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryTray
{
    public class TaskbarBatteryBar : Form
    {
        private List<DeviceBatteryInfo> _devices = new();
        private bool _isDarkTaskbar = true;
        private bool _showBackgroundPill = false;
        private bool _mouseDownOnBar = false;
        private DateTime _lastMenuClosedUtc = DateTime.MinValue;
        private readonly ContextMenuStrip _contextMenu;
        private readonly System.Windows.Forms.Timer _dockSyncTimer;

        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;

        private static readonly string[] HeadsetKeywords =
        {
            "headset", "headphone", "airpods", "earbud", "buds", "earphone", "tws", "audio",
            "wh-", "wf-", "inzone", "pulse 3d", "pulse elite", "bose", "jbl", "marshall", "beats", "sennheiser", "epos", "moondrop",
            "arctis", "cloud ", "blackshark", "barracuda", "kraken", "nari", "kaira",
            "void", "virtuoso", "hs70", "hs80", "g733", "g535", "g435", "g522", "g pro x", "pro x 2", "astro",
            "rog delta", "rog pelta", "rog cetra", "tuf gaming h", "turtle beach", "stealth ",
            "mercury", "neptune", "n9", "attack shark", "l80", "g300", "g500", "vxe v1",
            "mchose v9", "mchose g9", "mchose x9", "dareu", "eh7", "a710", "edifier", "hecate", "fifine", "somic", "rapoo vh"
        };

        public TaskbarBatteryBar(ContextMenuStrip contextMenu)
        {
            _contextMenu = contextMenu;
            _contextMenu.AutoClose = true;

            _contextMenu.Opening += (_, _) =>
            {
                if (IsHandleCreated) SetForegroundWindow(Handle);
            };
            _contextMenu.Closed += (_, _) =>
            {
                _lastMenuClosedUtc = DateTime.UtcNow;
                _mouseDownOnBar = false;
            };

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            MouseDown += OnBarMouseDown;
            MouseUp += OnBarMouseUp;
            MouseLeave += (_, _) => _mouseDownOnBar = false;

            _dockSyncTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _dockSyncTimer.Tick += (_, _) => SyncPositionToTaskbar();
            _dockSyncTimer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void UpdateDisplay(List<DeviceBatteryInfo> visibleDevices, bool isDarkTaskbar, bool showBackgroundPill)
        {
            _devices = visibleDevices;
            _isDarkTaskbar = isDarkTaskbar;
            _showBackgroundPill = showBackgroundPill;

            if (_devices.Count == 0)
            {
                Hide();
                return;
            }

            RecalculateLayout();
            if (!Visible) Show();
            SyncPositionToTaskbar();
            RenderLayeredBitmap();
        }

        private void RecalculateLayout()
        {
            float scale = DeviceDpi / 96f;
            int itemPadding = (int)(10 * scale);
            int iconWidth = (int)(20 * scale);
            int gap = (int)(5 * scale);
            int totalWidth = (int)(8 * scale);

            using var tmpBmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(tmpBmp);
            using var font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            foreach (var dev in _devices)
            {
                string pctText = $"{dev.BatteryPercent}%";
                SizeF textSize = g.MeasureString(pctText, font);
                totalWidth += iconWidth + gap + (int)Math.Ceiling(textSize.Width) + itemPadding;
            }

            int barHeight = GetTaskbarHeight();
            int pillHeight = Math.Max((int)(28 * scale), barHeight - (int)(12 * scale));
            Size = new Size(Math.Max((int)(64 * scale), totalWidth), pillHeight);
        }

        private void SyncPositionToTaskbar()
        {
            if (!Visible || _devices.Count == 0) return;

            IntPtr shellTray = FindWindow("Shell_TrayWnd", null);
            if (shellTray == IntPtr.Zero) return;

            IntPtr trayNotify = FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (!GetWindowRect(shellTray, out RECT trayRect)) return;

            int anchorRight = trayRect.Right - 220;
            if (trayNotify != IntPtr.Zero && GetWindowRect(trayNotify, out RECT notifyRect))
            {
                anchorRight = notifyRect.Left;
            }

            int taskbarHeight = trayRect.Bottom - trayRect.Top;
            int x = anchorRight - Width - 4;
            int y = trayRect.Top + (taskbarHeight - Height) / 2;

            if (Left != x || Top != y) Location = new Point(x, y);
            SetWindowPos(Handle, new IntPtr(-1), x, y, Width, Height, 0x0010 | 0x0002 | 0x0001);
        }

        private static int GetTaskbarHeight()
        {
            IntPtr shellTray = FindWindow("Shell_TrayWnd", null);
            if (shellTray != IntPtr.Zero && GetWindowRect(shellTray, out RECT rect))
            {
                int h = rect.Bottom - rect.Top;
                if (h > 20 && h < 120) return h;
            }
            return 48;
        }

        private void OnBarMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

            if (_contextMenu.Visible)
            {
                _contextMenu.Close();
                _mouseDownOnBar = false;
                return;
            }

            if ((DateTime.UtcNow - _lastMenuClosedUtc).TotalMilliseconds < 250)
            {
                _mouseDownOnBar = false;
                return;
            }

            _mouseDownOnBar = ClientRectangle.Contains(e.Location);
        }

        private void OnBarMouseUp(object? sender, MouseEventArgs e)
        {
            bool wasValidClick = _mouseDownOnBar && ClientRectangle.Contains(e.Location);
            _mouseDownOnBar = false;
            if (!wasValidClick) return;

            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
            {
                SetForegroundWindow(Handle);
                Point screenPt = PointToScreen(new Point(0, 0));
                _contextMenu.Show(screenPt, ToolStripDropDownDirection.AboveRight);
            }
        }

        private void RenderLayeredBitmap()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0 || _devices.Count == 0) return;

            using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float scale = DeviceDpi / 96f;

                if (_showBackgroundPill)
                {
                    Color pillColor = _isDarkTaskbar
                        ? Color.FromArgb(190, 36, 36, 36)
                        : Color.FromArgb(215, 243, 246, 250);
                    Color borderColor = _isDarkTaskbar
                        ? Color.FromArgb(140, 85, 85, 85)
                        : Color.FromArgb(160, 195, 204, 214);

                    using var bgPath = CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(6 * scale));
                    using var bgBrush = new SolidBrush(pillColor);
                    using var borderPen = new Pen(borderColor, 1f);
                    g.FillPath(bgBrush, bgPath);
                    g.DrawPath(borderPen, bgPath);
                }
                else
                {
                    using var hitBrush = new SolidBrush(Color.FromArgb(1, 0, 0, 0));
                    g.FillRectangle(hitBrush, 0, 0, Width, Height);
                }

                Color fgColor = _isDarkTaskbar ? Color.White : Color.FromArgb(24, 24, 24);
                using var font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

                int cursorX = (int)(8 * scale);
                int iconW = (int)(19 * scale);
                int iconH = (int)(13 * scale);
                int gap = (int)(5 * scale);
                int itemPad = (int)(10 * scale);

                foreach (var dev in _devices)
                {
                    Color itemColor = dev.BatteryPercent <= 20
                        ? Color.FromArgb(232, 35, 35)
                        : fgColor;

                    var visualCategory = ResolveVisualCategory(dev);
                    int iconY = (Height - iconH) / 2;
                    DrawDeviceVectorIcon(g, visualCategory, new Rectangle(cursorX, iconY, iconW, iconH), itemColor, scale);
                    cursorX += iconW + gap;

                    string pctText = $"{dev.BatteryPercent}%";
                    SizeF textSize = g.MeasureString(pctText, font);
                    float textY = (Height - textSize.Height) / 2f;

                    using var textBrush = new SolidBrush(itemColor);
                    g.DrawString(pctText, font, textBrush, cursorX, textY);

                    cursorX += (int)Math.Ceiling(textSize.Width) + itemPad;
                }
            }

            ApplyBitmapToLayeredWindow(bitmap);
        }

        private static DeviceCategory ResolveVisualCategory(DeviceBatteryInfo dev)
        {
            if (dev.Category == DeviceCategory.Headset)
                return DeviceCategory.Headset;

            string lowerName = (dev.Name ?? string.Empty).ToLowerInvariant();
            foreach (string kw in HeadsetKeywords)
            {
                if (lowerName.Contains(kw))
                    return DeviceCategory.Headset;
            }

            return dev.Category;
        }

        private void ApplyBitmapToLayeredWindow(Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                var pointSource = new POINT { x = 0, y = 0 };
                var topPos = new POINT { x = Left, y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1
                };

                UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, 2);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static void DrawDeviceVectorIcon(Graphics g, DeviceCategory category, Rectangle bounds, Color color, float scale)
        {
            using var pen = new Pen(color, Math.Max(1.2f, 1.3f * scale));
            using var brush = new SolidBrush(color);

            if (category == DeviceCategory.Keyboard)
            {
                using var path = CreateRoundedRect(bounds, (int)(2 * scale));
                g.DrawPath(pen, path);

                int dotSize = Math.Max(1, (int)(1.5f * scale));
                int row1Y = bounds.Top + (int)(3.5f * scale);
                int row2Y = bounds.Top + (int)(6.5f * scale);
                for (int i = 0; i < 4; i++)
                {
                    int kx = bounds.Left + (int)((3 + i * 3.5f) * scale);
                    g.FillRectangle(brush, kx, row1Y, dotSize, dotSize);
                    g.FillRectangle(brush, kx, row2Y, dotSize, dotSize);
                }
                int spaceY = bounds.Bottom - (int)(3.2f * scale);
                g.DrawLine(pen, bounds.Left + (int)(4 * scale), spaceY, bounds.Right - (int)(4 * scale), spaceY);
            }
            else if (category == DeviceCategory.Mouse)
            {
                int mw = (int)(11 * scale);
                int mh = (int)(15 * scale);
                var mRect = new Rectangle(bounds.Left + (bounds.Width - mw) / 2, bounds.Top + (bounds.Height - mh) / 2, mw, mh);
                using var path = CreateRoundedRect(mRect, mw / 2);
                g.DrawPath(pen, path);
                int wheelX = mRect.Left + mw / 2;
                g.DrawLine(pen, wheelX, mRect.Top + (int)(3 * scale), wheelX, mRect.Top + (int)(6 * scale));
            }
            else if (category == DeviceCategory.Headset)
            {
                int hw = (int)(14 * scale);
                int hh = (int)(13 * scale);
                int hx = bounds.Left + (bounds.Width - hw) / 2;
                int hy = bounds.Top + (bounds.Height - hh) / 2;

                // Vòng chụp đầu (headband)
                g.DrawArc(pen, hx + (int)(1 * scale), hy, hw - (int)(2 * scale), hh - (int)(1 * scale), 180, 180);

                // Hai chụp tai trái và phải
                int cupW = Math.Max(2, (int)(3 * scale));
                int cupH = Math.Max(4, (int)(5.5f * scale));
                int cupY = hy + hh - cupH;
                using var leftCup = CreateRoundedRect(new Rectangle(hx, cupY, cupW, cupH), Math.Max(1, (int)(1 * scale)));
                using var rightCup = CreateRoundedRect(new Rectangle(hx + hw - cupW, cupY, cupW, cupH), Math.Max(1, (int)(1 * scale)));
                g.FillPath(brush, leftCup);
                g.FillPath(brush, rightCup);
            }
            else
            {
                using var path = CreateRoundedRect(bounds, (int)(2 * scale));
                g.DrawPath(pen, path);
                g.FillRectangle(brush, bounds.Left + 3, bounds.Top + 3, bounds.Width - 6, bounds.Height - 6);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int safeRadius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            int d = Math.Max(1, safeRadius * 2);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _dockSyncTimer.Dispose();
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}

