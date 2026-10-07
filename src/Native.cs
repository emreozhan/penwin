using System;
using System.Runtime.InteropServices;

namespace PenWin
{
    /// <summary>Win32 P/Invoke tanımları (user32). Yapılar x64 düzenine göre yazıldı.</summary>
    internal static class Native
    {
        // --- DPI ---
        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        // --- Monitörler ---
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        public const uint MONITORINFOF_PRIMARY = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        // --- SendInput (fare / klavye) ---
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // --- Sentetik kalem (Windows 10 1809+) ---
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateSyntheticPointerDevice(int pointerType, uint maxCount, int mode);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool InjectSyntheticPointerInput(IntPtr device, [In] POINTER_TYPE_INFO[] info, uint count);

        [DllImport("user32.dll")]
        public static extern void DestroySyntheticPointerDevice(IntPtr device);

        public const int PT_PEN = 3;
        public const int POINTER_FEEDBACK_DEFAULT = 1;

        public const uint POINTER_FLAG_INRANGE = 0x00000002;
        public const uint POINTER_FLAG_INCONTACT = 0x00000004;
        public const uint POINTER_FLAG_FIRSTBUTTON = 0x00000010;
        public const uint POINTER_FLAG_PRIMARY = 0x00002000;
        public const uint POINTER_FLAG_DOWN = 0x00010000;
        public const uint POINTER_FLAG_UPDATE = 0x00020000;
        public const uint POINTER_FLAG_UP = 0x00040000;

        public const int POINTER_CHANGE_NONE = 0;
        public const int POINTER_CHANGE_FIRSTBUTTON_DOWN = 1;
        public const int POINTER_CHANGE_FIRSTBUTTON_UP = 2;

        public const uint PEN_MASK_PRESSURE = 0x1;
        public const uint PEN_MASK_TILT_X = 0x4;
        public const uint PEN_MASK_TILT_Y = 0x8;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_INFO
        {
            public int pointerType;
            public uint pointerId;
            public uint frameId;
            public uint pointerFlags;
            public IntPtr sourceDevice;
            public IntPtr hwndTarget;
            public POINT ptPixelLocation;
            public POINT ptHimetricLocation;
            public POINT ptPixelLocationRaw;
            public POINT ptHimetricLocationRaw;
            public uint dwTime;
            public uint historyCount;
            public int InputData;
            public uint dwKeyStates;
            public ulong PerformanceCount;
            public int ButtonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_PEN_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint penFlags;
            public uint penMask;
            public uint pressure;
            public uint rotation;
            public int tiltX;
            public int tiltY;
        }

        /// <summary>
        /// Yerel POINTER_TYPE_INFO bir birlik (touch/pen) içerir; x64'te boyutu 152 bayttır.
        /// Yalnızca kalem kolu kullanıldığı için açık düzenle tanımlandı.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 152)]
        public struct POINTER_TYPE_INFO
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(8)] public POINTER_PEN_INFO penInfo;
        }
    }
}
