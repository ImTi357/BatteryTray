using System;
using System.Threading;
using System.Windows.Forms;

namespace BatteryTray
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(true, @"Global\BatteryTraySingleInstanceMutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "BatteryTray đang chạy ngầm trên thanh Taskbar.\nHãy nhấp vào biểu tượng pin cạnh khay hệ thống để tùy chỉnh.",
                    "BatteryTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            ApplicationConfiguration.Initialize();

            var providers = new IBatteryProvider[]
            {
                new BluetoothBatteryProvider(),
                new AtkReceiverProvider(),
                new LogitechProvider(),
                new RazerProvider(),
                new CompxProvider()
            };

            var aggregator = new BatteryAggregator(providers);
            Application.Run(new SmartBatteryTrayContext(aggregator));
        }
    }
}
