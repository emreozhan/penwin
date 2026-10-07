using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PenWin
{
    /// <summary>Bir monitörün fiziksel piksel cinsinden konumu ve boyutu.</summary>
    internal sealed class MonitorInfo
    {
        public string Device;
        public int Left, Top, Width, Height;
        public bool Primary;
    }

    internal static class Monitors
    {
        /// <summary>Monitörleri listeler: önce ana monitör, sonra soldan sağa.</summary>
        public static List<MonitorInfo> GetAll()
        {
            var list = new List<MonitorInfo>();
            Native.MonitorEnumProc callback = delegate(IntPtr handle, IntPtr hdc, ref Native.RECT rect, IntPtr data)
            {
                var info = new Native.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (Native.GetMonitorInfo(handle, ref info))
                {
                    var r = info.rcMonitor;
                    list.Add(new MonitorInfo
                    {
                        Device = info.szDevice,
                        Left = r.Left,
                        Top = r.Top,
                        Width = r.Right - r.Left,
                        Height = r.Bottom - r.Top,
                        Primary = (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0
                    });
                }
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            list.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                return a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Top.CompareTo(b.Top);
            });
            return list;
        }
    }
}
