using System;
using System.Runtime.InteropServices;

namespace BatteryTray
{
    internal static class NativeMethods
    {
        public const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
        public const uint DEVPROP_TYPE_BYTE = 0x00000003;
        public const uint DEVPROP_TYPE_UINT32 = 0x00000007;
        public const uint DEVPROP_TYPE_GUID = 0x0000000D;
        public const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
        public const uint DEVPROP_TYPE_STRING = 0x00000012;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;

            public DEVPROPKEY(string guid, uint pid)
            {
                fmtid = new Guid(guid);
                this.pid = pid;
            }
        }

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Get_Device_ID_List_SizeW(out uint pulLen, string? pszFilter, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Get_Device_ID_ListW(string? pszFilter, [Out] char[] buffer, uint bufferLen, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("CfgMgr32.dll")]
        public static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        public static extern uint CM_Get_DevNode_PropertyW(
            uint dnDevInst,
            in DEVPROPKEY propertyKey,
            out uint propertyType,
            [Out] byte[]? propertyBuffer,
            ref uint propertyBufferSize,
            uint ulFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
