using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly ContextMenuStrip _contextMenu;
        private readonly System.Windows.Forms.Timer _dockSyncTimer;

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;

        // Dùng màu Magenta làm TransparencyKey để chữ và icon sắc nét tuyệt đối trên Taskbar
        private static readonly Color ChromaKeyColor = Color.FromArgb(255, 0, 255);

        public TaskbarBatteryBar(ContextMenuStrip contextMenu)
        {
            _contextMenu = contextMenu;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = ChromaKeyColor;
            TransparencyKey = ChromaKeyColor;

            MouseUp += OnBarMouseUp;

            _dockSyncTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _dockSyncTimer.Tick += (_, _) => SyncPositionToTaskbar();
            _dockSyncTimer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
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
            Invalidate();
        }

        private void RecalculateLayout()
        {
            float scale = DeviceDpi / 96f;
            int itemPadding = (int)(10 * scale);
            int iconWidth = (int)(20 * scale);
            int gap = (int)(5 * scale);
            int totalWidth = (int)(8 * scale);

            using var g = CreateGraphics();
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

            if (Left != x || Top != y)
            {
                Location = new Point(x, y);
            }
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

        private void OnBarMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
            {
                Point screenPt = PointToScreen(new Point(0, 0));
                _contextMenu.Show(screenPt, ToolStripDropDownDirection.AboveRight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_devices.Count == 0) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            float scale = DeviceDpi / 96f;

            // Vẽ nền mờ (hoặc nền tiệp màu Taskbar) nếu bật tùy chọn "Background behind taskbar text"
            if (_showBackgroundPill)
            {
                Color pillColor = _isDarkTaskbar
                    ? Color.FromArgb(44, 44, 44)
                    : Color.FromArgb(238, 242, 246);
                Color borderColor = _isDarkTaskbar
                    ? Color.FromArgb(72, 72, 72)
                    : Color.FromArgb(205, 212, 220);

                using var bgPath = CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(6 * scale));
                using var bgBrush = new SolidBrush(pillColor);
                using var borderPen = new Pen(borderColor, 1f);
                g.FillPath(bgBrush, bgPath);
                g.DrawPath(borderPen, bgPath);
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

                int iconY = (Height - iconH) / 2;
                DrawDeviceVectorIcon(g, dev.Category, new Rectangle(cursorX, iconY, iconW, iconH), itemColor, scale);
                cursorX += iconW + gap;

                string pctText = $"{dev.BatteryPercent}%";
                SizeF textSize = g.MeasureString(pctText, font);
                float textY = (Height - textSize.Height) / 2f;

                using var textBrush = new SolidBrush(itemColor);
                g.DrawString(pctText, font, textBrush, cursorX, textY);

                cursorX += (int)Math.Ceiling(textSize.Width) + itemPad;
            }
        }

        private static void DrawDeviceVectorIcon(Graphics g, DeviceCategory category, Rectangle bounds, Color color, float scale)
        {
            using var pen = new Pen(color, Math.Max(1.2f, 1.3f * scale));
            using var brush = new SolidBrush(color);

            if (category == DeviceCategory.Keyboard)
            {
                // Vẽ biểu tượng bàn phím nhỏ giống hệt ảnh mẫu
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
                // Vẽ biểu tượng chuột đứng gọn gàng
                int mw = (int)(11 * scale);
                int mh = (int)(15 * scale);
                var mRect = new Rectangle(bounds.Left + (bounds.Width - mw) / 2, bounds.Top + (bounds.Height - mh) / 2, mw, mh);
                using var path = CreateRoundedRect(mRect, mw / 2);
                g.DrawPath(pen, path);
                int wheelX = mRect.Left + mw / 2;
                g.DrawLine(pen, wheelX, mRect.Top + (int)(3 * scale), wheelX, mRect.Top + (int)(6 * scale));
            }
            else
            {
                // Biểu tượng pin chung cho tai nghe / tay cầm
                using var path = CreateRoundedRect(bounds, (int)(2 * scale));
                g.DrawPath(pen, path);
                g.FillRectangle(brush, bounds.Left + 3, bounds.Top + 3, bounds.Width - 6, bounds.Height - 6);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
