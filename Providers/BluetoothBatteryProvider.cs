using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BatteryTray
{
    public class BluetoothBatteryProvider : IBatteryProvider
    {
        public string ProviderName => "Windows Bluetooth PnP";

        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_Device_BatteryLevel = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_Bluetooth_Connected = new("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_Bluetooth_ClassOfDevice = new("83DA6326-97A6-4088-9453-A1923F573B29", 10);
        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_Device_FriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_NAME = new("B725F130-47EF-101A-A5F1-02608C9EEBAC", 10);
        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_Device_Service = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 6);
        private static readonly NativeMethods.DEVPROPKEY DEVPKEY_Device_ContainerId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);

        public IEnumerable<DeviceBatteryInfo> GetDevices()
        {
            var results = new Dictionary<string, DeviceBatteryInfo>();
            var containerServices = new Dictionary<string, HashSet<string>>();
            var containerNames = new Dictionary<string, string>();
            var containerCod = new Dictionary<string, uint>();

            if (NativeMethods.CM_Get_Device_ID_List_SizeW(out uint listSize, null, NativeMethods.CM_GETIDLIST_FILTER_PRESENT) != 0 || listSize == 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            char[] buffer = new char[listSize];
            if (NativeMethods.CM_Get_Device_ID_ListW(null, buffer, listSize, NativeMethods.CM_GETIDLIST_FILTER_PRESENT) != 0)
                return Enumerable.Empty<DeviceBatteryInfo>();

            string[] deviceIds = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);

            foreach (string deviceId in deviceIds)
            {
                if (NativeMethods.CM_Locate_DevNodeW(out uint devInst, deviceId, 0) != 0) continue;
                Guid? containerId = ReadGuid(devInst, DEVPKEY_Device_ContainerId);
                if (!containerId.HasValue || containerId.Value == Guid.Empty) continue;

                string key = containerId.Value.ToString();
                string? service = ReadString(devInst, DEVPKEY_Device_Service);
                if (!string.IsNullOrWhiteSpace(service))
                {
                    if (!containerServices.ContainsKey(key)) containerServices[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    containerServices[key].Add(service);
                }

                uint? cod = ReadUInt32(devInst, DEVPKEY_Bluetooth_ClassOfDevice);
                if (cod.HasValue && cod.Value != 0) containerCod[key] = cod.Value;

                string? name = ReadString(devInst, DEVPKEY_Device_FriendlyName) ?? ReadString(devInst, DEVPKEY_NAME);
                if (!string.IsNullOrWhiteSpace(name) && !IsGenericName(name)) containerNames[key] = name;
            }

            foreach (string deviceId in deviceIds)
            {
                if (!deviceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) &&
                    !deviceId.StartsWith("HID", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (NativeMethods.CM_Locate_DevNodeW(out uint devInst, deviceId, 0) != 0) continue;

                int? battery = ReadByte(devInst, DEVPKEY_Device_BatteryLevel);
                if (battery == null || battery < 0 || battery > 100) continue;

                bool connected = IsConnected(devInst);
                Guid? containerId = ReadGuid(devInst, DEVPKEY_Device_ContainerId);
                string groupKey = (containerId.HasValue && containerId.Value != Guid.Empty) ? containerId.Value.ToString() : deviceId;
                string name = containerNames.TryGetValue(groupKey, out var cached) ? cached : ResolveName(devInst);

                containerServices.TryGetValue(groupKey, out var services);
                containerCod.TryGetValue(groupKey, out uint cod);

                results.TryAdd("BT_" + groupKey, new DeviceBatteryInfo
                {
                    Id = "BT_" + groupKey,
                    Name = name,
                    ProviderName = ProviderName,
                    BatteryPercent = battery.Value,
                    IsAsleepOrCached = !connected,
                    Connection = ConnectionType.Bluetooth,
                    Category = DetectCategory(name, cod, services)
                });
            }

            return results.Values;
        }

        private static DeviceCategory DetectCategory(string name, uint cod, HashSet<string>? services)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("keyboard") || n.Contains("kb") || n.Contains("keychron") || n.Contains("aula") || n.Contains("akko") || n.Contains("k380") || n.Contains("mx keys"))
                return DeviceCategory.Keyboard;
            if (n.Contains("mouse") || n.Contains("mx master") || n.Contains("viper") || n.Contains("superlight") || n.Contains("dragonfly") || n.Contains("blazing sky"))
                return DeviceCategory.Mouse;
            if (n.Contains("headphone") || n.Contains("headset") || n.Contains("airpods") || n.Contains("buds"))
                return DeviceCategory.Audio;
            if (n.Contains("controller") || n.Contains("xbox") || n.Contains("dualsense") || n.Contains("gamepad"))
                return DeviceCategory.Gamepad;

            if ((cod & 0x1F00) == 0x0500)
            {
                uint minor = cod & 0x00C0;
                if (minor == 0x0040) return DeviceCategory.Keyboard;
                if (minor == 0x0080) return DeviceCategory.Mouse;
            }

            if (services != null)
            {
                if (services.Contains("kbdhid") || services.Contains("kbdclass")) return DeviceCategory.Keyboard;
                if (services.Contains("mouhid") || services.Contains("mouclass")) return DeviceCategory.Mouse;
                if (services.Any(s => s.Contains("btha2dp", StringComparison.OrdinalIgnoreCase))) return DeviceCategory.Audio;
            }
            return DeviceCategory.Other;
        }

        private static bool IsConnected(uint devInst)
        {
            uint current = devInst;
            for (int i = 0; i < 4; i++)
            {
                bool? conn = ReadBool(current, DEVPKEY_Bluetooth_Connected);
                if (conn.HasValue) return conn.Value;
                if (NativeMethods.CM_Get_Parent(out uint parent, current, 0) != 0) break;
                current = parent;
            }
            return true;
        }

        private static string ResolveName(uint devInst)
        {
            uint current = devInst;
            string fallback = "Bluetooth Device";
            for (int i = 0; i < 4; i++)
            {
                string? name = ReadString(current, DEVPKEY_Device_FriendlyName) ?? ReadString(current, DEVPKEY_NAME);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fallback = name;
                    if (!IsGenericName(name)) return name;
                }
                if (NativeMethods.CM_Get_Parent(out uint parent, current, 0) != 0) break;
                current = parent;
            }
            return fallback;
        }

        private static bool IsGenericName(string name) =>
            name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Battery Service", StringComparison.OrdinalIgnoreCase);

        private static int? ReadByte(uint inst, NativeMethods.DEVPROPKEY key)
        {
            uint size = 0;
            NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out uint type, null, ref size, 0);
            if (size == 0) return null;
            byte[] data = new byte[size];
            return (NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out type, data, ref size, 0) == 0 && type == NativeMethods.DEVPROP_TYPE_BYTE) ? data[0] : null;
        }

        private static uint? ReadUInt32(uint inst, NativeMethods.DEVPROPKEY key)
        {
            uint size = 0;
            NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out uint type, null, ref size, 0);
            if (size < 4) return null;
            byte[] data = new byte[size];
            return (NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out type, data, ref size, 0) == 0 && type == NativeMethods.DEVPROP_TYPE_UINT32) ? BitConverter.ToUInt32(data, 0) : null;
        }

        private static bool? ReadBool(uint inst, NativeMethods.DEVPROPKEY key)
        {
            uint size = 0;
            NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out uint type, null, ref size, 0);
            if (size == 0) return null;
            byte[] data = new byte[size];
            return (NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out type, data, ref size, 0) == 0 && type == NativeMethods.DEVPROP_TYPE_BOOLEAN) ? data[0] != 0 : null;
        }

        private static Guid? ReadGuid(uint inst, NativeMethods.DEVPROPKEY key)
        {
            uint size = 0;
            NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out uint type, null, ref size, 0);
            if (size < 16) return null;
            byte[] data = new byte[size];
            return (NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out type, data, ref size, 0) == 0 && type == NativeMethods.DEVPROP_TYPE_GUID) ? new Guid(data.AsSpan(0, 16)) : null;
        }

        private static string? ReadString(uint inst, NativeMethods.DEVPROPKEY key)
        {
            uint size = 0;
            NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out uint type, null, ref size, 0);
            if (size < 2) return null;
            byte[] data = new byte[size];
            return (NativeMethods.CM_Get_DevNode_PropertyW(inst, in key, out type, data, ref size, 0) == 0 && type == NativeMethods.DEVPROP_TYPE_STRING) ? Encoding.Unicode.GetString(data).TrimEnd('\0') : null;
        }
    }
}
