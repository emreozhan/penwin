using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PenWin
{
    /// <summary>Windows'a fare, klavye ve sentetik kalem (Windows Ink) girdisi gönderir.</summary>
    internal sealed class Injector : IDisposable
    {
        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));
        private readonly bool dryRun;
        private IntPtr penDevice = IntPtr.Zero;
        private int lastPenError;

        /// <param name="dryRun">true ise hiçbir girdi gönderilmez (protokol/arayüz denemesi için).</param>
        public Injector(bool dryRun)
        {
            this.dryRun = dryRun;
            // Kalem yapıları yalnızca 64 bit düzene göre tanımlı.
            if (IntPtr.Size != 8 || Marshal.SizeOf(typeof(Native.POINTER_TYPE_INFO)) != 152) return;
            try
            {
                penDevice = Native.CreateSyntheticPointerDevice(Native.PT_PEN, 1, Native.POINTER_FEEDBACK_DEFAULT);
            }
            catch (EntryPointNotFoundException)
            {
                penDevice = IntPtr.Zero; // Windows 10 1809 öncesi
            }
        }

        public bool PenAvailable
        {
            get { return penDevice != IntPtr.Zero; }
        }

        /// <summary>İmleci fiziksel piksel koordinatına taşır (çoklu monitör destekli).</summary>
        public void MoveMouse(int x, int y)
        {
            MouseAt(x, y, 0);
        }

        /// <summary>
        /// Taşıma ve tuş olayını tek girdi olarak gönderir; arada fiziksel fare oynasa da
        /// tık doğru noktaya düşer.
        /// </summary>
        public void MouseAt(int x, int y, uint buttonFlag)
        {
            if (dryRun) return;
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int vw = Math.Max(1, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN));
            int vh = Math.Max(1, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
            // Windows normalize değeri floor(n * genişlik / 65536) ile piksele çevirir;
            // tavan yuvarlama hedef pikseli tam tutturur.
            int dx = (int)Math.Ceiling((x - vx) * 65536.0 / vw);
            int dy = (int)Math.Ceiling((y - vy) * 65536.0 / vh);
            Send(MouseInput(dx, dy, 0,
                Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK | buttonFlag));
        }

        /// <summary>İmlecin o anki konumunda tuş olayı (iki parmak pan başlangıcı için).</summary>
        public void MouseButton(uint flag)
        {
            if (dryRun) return;
            Send(MouseInput(0, 0, 0, flag));
        }

        public void Wheel(int delta)
        {
            if (dryRun) return;
            Send(MouseInput(0, 0, unchecked((uint)delta), Native.MOUSEEVENTF_WHEEL));
        }

        public void Key(ushort vk, bool down)
        {
            if (dryRun) return;
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki.wVk = vk;
            uint flags = down ? 0 : Native.KEYEVENTF_KEYUP;
            if (IsExtendedKey(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            input.u.ki.dwFlags = flags;
            Send(input);
        }

        /// <summary>Sentetik kalem karesi gönderir. Basınç 0..1, eğim derece (-90..90).</summary>
        public bool Pen(int x, int y, uint pointerFlags, int buttonChange, double pressure, int tiltX, int tiltY)
        {
            if (penDevice == IntPtr.Zero) return false;
            if (dryRun) return true;
            var info = new Native.POINTER_TYPE_INFO();
            info.type = Native.PT_PEN;
            info.penInfo.pointerInfo.pointerType = Native.PT_PEN;
            info.penInfo.pointerInfo.pointerFlags = pointerFlags;
            // Sentetik işaretçi konumu ekran değil, sanal masaüstünün sol-üst köşesine göredir
            // (ana monitörün üstünde/solunda ekran varsa fark eder).
            info.penInfo.pointerInfo.ptPixelLocation.X = x - Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            info.penInfo.pointerInfo.ptPixelLocation.Y = y - Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            info.penInfo.pointerInfo.ButtonChangeType = buttonChange;
            info.penInfo.penMask = Native.PEN_MASK_PRESSURE | Native.PEN_MASK_TILT_X | Native.PEN_MASK_TILT_Y;
            info.penInfo.pressure = (uint)Math.Round(Math.Max(0.0, Math.Min(1.0, pressure)) * 1024);
            info.penInfo.tiltX = Math.Max(-90, Math.Min(90, tiltX));
            info.penInfo.tiltY = Math.Max(-90, Math.Min(90, tiltY));

            bool ok = Native.InjectSyntheticPointerInput(penDevice, new[] { info }, 1);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != lastPenError) Log.Warn("Kalem girdisi reddedildi (Win32 hata " + error + ").");
                lastPenError = error;
            }
            return ok;
        }

        public void Dispose()
        {
            if (penDevice == IntPtr.Zero) return;
            Native.DestroySyntheticPointerDevice(penDevice);
            penDevice = IntPtr.Zero;
        }

        private static Native.INPUT MouseInput(int dx, int dy, uint data, uint flags)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            input.u.mi.dx = dx;
            input.u.mi.dy = dy;
            input.u.mi.mouseData = data;
            input.u.mi.dwFlags = flags;
            return input;
        }

        private static void Send(Native.INPUT input)
        {
            Native.SendInput(1, new[] { input }, InputSize);
        }

        private static readonly HashSet<ushort> ExtendedKeys = new HashSet<ushort>
        {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PgUp PgDn End Home oklar
            0x2D, 0x2E, 0xA3, 0xA5                          // Insert Delete RCtrl RAlt
        };

        private static bool IsExtendedKey(ushort vk)
        {
            return ExtendedKeys.Contains(vk);
        }
    }
}
