using System;
using System.Collections.Generic;
using System.Linq;

namespace BatteryTray
{
    public static class UsbHardwareScanner
    {
        public static List<string> FindPresentUsbInstances(string vidHex, string? pidHex = null)
        {
            if (NativeMethods.CM_Get_Device_ID_List_SizeW(out uint listSize, null, NativeMethods.CM_GETIDLIST_FILTER_PRESENT) != 0 || listSize == 0)
                return new List<string>();

            char[] buffer = new char[listSize];
            if (NativeMethods.CM_Get_Device_ID_ListW(null, buffer, listSize, NativeMethods.CM_GETIDLIST_FILTER_PRESENT) != 0)
                return new List<string>();

            string targetPrefix = pidHex != null
                ? $"USB\\VID_{vidHex}&PID_{pidHex}"
                : $"USB\\VID_{vidHex}";

            return new string(buffer)
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(id => id.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
