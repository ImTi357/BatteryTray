using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BatteryTray
{
    public class AppSettings
    {
        public HashSet<string> HiddenDeviceIds { get; set; } = new();
        public bool HideAsleepDevices { get; set; } = true;
        public bool ShowOnTaskbar { get; set; } = true;
        public bool ShowBackgroundPill { get; set; } = false;

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BatteryTray"
        );
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
