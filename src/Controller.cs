using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace PenWin
{
    /// <summary>
    /// iPad'den gelen satır protokolünü yorumlar ve Windows girdisine çevirir.
    /// Koordinatlar seçili monitöre göre 0..1 aralığında normalize gelir.
    /// Tüm genel üyeler iş parçacığı güvenlidir.
    /// </summary>
    /// <remarks>
    /// Protokol (istemci → sunucu, her satır bir komut, bir mesajda birden çok satır olabilir):
    ///   h x y p tx ty      kalem havada (hover) hareket
    ///   d x y p tx ty B    temas başladı; B = L (sol) | R (sağ) | M (orta/pan) | O (Shift+orta/orbit)
    ///   m x y p tx ty      temasla hareket
    ///   u x y              temas bitti
    ///   o                  kalem algılama alanından çıktı
    ///   cfg mouse|pen N    çalışma modu ve monitör sırası
    ///   key ctrl+z         tuş kombinasyonu bas-bırak
    ///   mod shift 1|0      değiştirici tuşu basılı tut / bırak
    ///   wheel N            fare tekerleği (120 = bir çentik)
    ///   pan s | pan m dx dy | pan e   iki parmakla orta tuş sürükleme
    ///   ping N             gecikme ölçümü
    /// Ekran görüntüsü WebSocket dışında, HTTP ile alınır: GET /shot.jpg?k=ANAHTAR&amp;w=GENİŞLİK
    /// </remarks>
    internal sealed class Controller : IDisposable
    {
        private const int PenKeepAliveMs = 60;
        private const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

        private readonly object sync = new object();
        private readonly Injector injector;
        private readonly Timer keepAlive;
        private List<MonitorInfo> monitors;
        private int monitorIndex;
        private bool penMode;

        // Aktif vuruş durumu
        private bool contact;
        private char strokeButton = 'L';
        private bool strokeIsPen;
        private bool strokeHoldsShift;
        private bool penInRange;
        private int lastX, lastY;
        private int lastMouseX = int.MinValue, lastMouseY = int.MinValue;
        private double lastPressure;
        private int lastTiltX, lastTiltY;
        private int lastPenTick;

        // İki parmak pan jesti
        private bool panning;
        private Native.POINT panOrigin;

        private readonly HashSet<ushort> latched = new HashSet<ushort>();

        public Controller(Injector injector)
        {
            this.injector = injector;
            monitors = Monitors.GetAll();
            keepAlive = new Timer(OnKeepAlive, null, 20, 20);
        }

        public List<MonitorInfo> CurrentMonitors
        {
            get { lock (sync) return new List<MonitorInfo>(monitors); }
        }

        /// <summary>iPad'in eşlendiği monitör.</summary>
        public MonitorInfo SelectedMonitor
        {
            get { lock (sync) return Current(); }
        }

        /// <summary>Tek bir protokol satırını işler; istemciye gidecek JSON yanıtı ya da null döner.</summary>
        public string Handle(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] p = line.Trim().Split(' ');
            lock (sync)
            {
                switch (p[0])
                {
                    case "h": Hover(p); return null;
                    case "d": Down(p); return null;
                    case "m": Move(p); return null;
                    case "u": Up(p); return null;
                    case "o": OutOfRange(); return null;
                    case "key": PressCombo(Arg(p, 1)); return null;
                    case "mod": Latch(Arg(p, 1), Arg(p, 2) == "1"); return null;
                    case "wheel": Wheel(p); return null;
                    case "pan": Pan(p); return null;
                    case "cfg": Configure(p); return HelloJsonLocked();
                    case "hello": monitors = Monitors.GetAll(); return HelloJsonLocked();
                    case "ping":
                        double v;
                        return TryNum(Arg(p, 1), out v)
                            ? "{\"t\":\"pong\",\"v\":" + v.ToString("R", CultureInfo.InvariantCulture) + "}"
                            : null;
                    default: return null;
                }
            }
        }

        public string HelloJson()
        {
            lock (sync) return HelloJsonLocked();
        }

        /// <summary>Basılı her şeyi bırakır: bağlantı koptuğunda takılı tuş kalmasın.</summary>
        public void ReleaseAll()
        {
            lock (sync)
            {
                OutOfRange();
                if (panning) EndPan();
                foreach (ushort vk in latched) injector.Key(vk, false);
                latched.Clear();
            }
        }

        public void Dispose()
        {
            keepAlive.Dispose();
            ReleaseAll();
        }

        // ---------------------------------------------------------------- kalem/fare

        private void Hover(string[] p)
        {
            int x, y;
            Map(p, 1, out x, out y);
            if (contact) EndStroke(x, y); // kaçırılmış 'u' toleransı
            if (penMode && injector.PenAvailable)
            {
                PenFrame(x, y, Native.POINTER_FLAG_INRANGE | Native.POINTER_FLAG_UPDATE,
                    Native.POINTER_CHANGE_NONE, 0, Int(p, 4), Int(p, 5));
                penInRange = true;
            }
            else
            {
                MouseTo(x, y);
            }
        }

        private void Down(string[] p)
        {
            int x, y;
            Map(p, 1, out x, out y);
            if (contact) EndStroke(lastX, lastY);
            if (panning) EndPan();

            string b = Arg(p, 6);
            strokeButton = b.Length > 0 ? b[0] : 'L';
            // Kalem modunda yalnızca sol tık Windows Ink'e gider; sağ/orta tuşlar her zaman fare.
            strokeIsPen = penMode && injector.PenAvailable && strokeButton == 'L';

            if (strokeIsPen)
            {
                if (!penInRange)
                {
                    PenFrame(x, y, Native.POINTER_FLAG_INRANGE | Native.POINTER_FLAG_UPDATE,
                        Native.POINTER_CHANGE_NONE, 0, Int(p, 4), Int(p, 5));
                    penInRange = true;
                }
                PenFrame(x, y, PenContactFlags | Native.POINTER_FLAG_DOWN,
                    Native.POINTER_CHANGE_FIRSTBUTTON_DOWN, Num(p, 3), Int(p, 4), Int(p, 5));
            }
            else
            {
                if (penInRange) PenOut();
                strokeHoldsShift = strokeButton == 'O' && !latched.Contains(VK_SHIFT);
                if (strokeHoldsShift) injector.Key(VK_SHIFT, true);
                MouseButtonAt(x, y, ButtonFlag(strokeButton, true));
            }
            contact = true;
        }

        private void Move(string[] p)
        {
            if (!contact) return;
            int x, y;
            Map(p, 1, out x, out y);
            if (strokeIsPen)
            {
                PenFrame(x, y, PenContactFlags | Native.POINTER_FLAG_UPDATE,
                    Native.POINTER_CHANGE_NONE, Num(p, 3), Int(p, 4), Int(p, 5));
            }
            else
            {
                MouseTo(x, y);
            }
        }

        private void Up(string[] p)
        {
            if (!contact) return;
            int x, y;
            Map(p, 1, out x, out y);
            EndStroke(x, y);
        }

        private void OutOfRange()
        {
            if (contact) EndStroke(lastX, lastY);
            if (penInRange) PenOut();
        }

        private void EndStroke(int x, int y)
        {
            if (strokeIsPen)
            {
                PenFrame(x, y, Native.POINTER_FLAG_INRANGE | Native.POINTER_FLAG_UP,
                    Native.POINTER_CHANGE_FIRSTBUTTON_UP, 0, lastTiltX, lastTiltY);
            }
            else
            {
                MouseButtonAt(x, y, ButtonFlag(strokeButton, false));
                if (strokeHoldsShift) injector.Key(VK_SHIFT, false);
                strokeHoldsShift = false;
            }
            contact = false;
        }

        private const uint PenContactFlags =
            Native.POINTER_FLAG_INRANGE | Native.POINTER_FLAG_INCONTACT | Native.POINTER_FLAG_FIRSTBUTTON;

        private void PenOut()
        {
            PenFrame(lastX, lastY, Native.POINTER_FLAG_UPDATE, Native.POINTER_CHANGE_NONE, 0, 0, 0);
            penInRange = false;
        }

        private void PenFrame(int x, int y, uint flags, int change, double pressure, int tiltX, int tiltY)
        {
            injector.Pen(x, y, flags, change, pressure, tiltX, tiltY);
            lastMouseX = int.MinValue; // kalem imleci taşıdı; sonraki fare hareketi atlanmasın
            lastX = x;
            lastY = y;
            lastPressure = pressure;
            lastTiltX = tiltX;
            lastTiltY = tiltY;
            lastPenTick = Environment.TickCount;
        }

        private void MouseButtonAt(int x, int y, uint flag)
        {
            injector.MouseAt(x, y, flag);
            lastX = lastMouseX = x;
            lastY = lastMouseY = y;
        }

        private void MouseTo(int x, int y)
        {
            lastX = x;
            lastY = y;
            if (x == lastMouseX && y == lastMouseY) return;
            injector.MoveMouse(x, y);
            lastMouseX = x;
            lastMouseY = y;
        }

        /// <summary>Kalem temas halindeyken hareketsiz kalırsa Windows'un teması iptal etmemesi için son kareyi tekrarlar.</summary>
        private void OnKeepAlive(object state)
        {
            lock (sync)
            {
                if (!contact || !strokeIsPen) return;
                if (unchecked(Environment.TickCount - lastPenTick) < PenKeepAliveMs) return;
                PenFrame(lastX, lastY, PenContactFlags | Native.POINTER_FLAG_UPDATE,
                    Native.POINTER_CHANGE_NONE, lastPressure, lastTiltX, lastTiltY);
            }
        }

        private static uint ButtonFlag(char button, bool down)
        {
            switch (button)
            {
                case 'R': return down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP;
                case 'M':
                case 'O': return down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP;
                default: return down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP;
            }
        }

        // ---------------------------------------------------------------- jestler / tuşlar

        private void Wheel(string[] p)
        {
            int delta = Math.Max(-1200, Math.Min(1200, Int(p, 1)));
            if (delta != 0) injector.Wheel(delta);
        }

        private void Pan(string[] p)
        {
            switch (Arg(p, 1))
            {
                case "s":
                    if (contact || panning) return;
                    Native.GetCursorPos(out panOrigin);
                    injector.MouseButton(Native.MOUSEEVENTF_MIDDLEDOWN);
                    panning = true;
                    break;
                case "m":
                    if (!panning) return;
                    MonitorInfo m = Current();
                    int x = panOrigin.X + (int)Math.Round(Num(p, 2) * m.Width);
                    int y = panOrigin.Y + (int)Math.Round(Num(p, 3) * m.Height);
                    MouseTo(x, y);
                    break;
                case "e":
                    if (panning) EndPan();
                    break;
            }
        }

        private void EndPan()
        {
            injector.MouseButton(Native.MOUSEEVENTF_MIDDLEUP);
            MouseTo(panOrigin.X, panOrigin.Y);
            panning = false;
        }

        private void Latch(string name, bool down)
        {
            ushort vk;
            switch (name)
            {
                case "shift": vk = VK_SHIFT; break;
                case "ctrl": vk = VK_CONTROL; break;
                case "alt": vk = VK_MENU; break;
                default: return;
            }
            if (down && latched.Add(vk)) injector.Key(vk, true);
            if (!down && latched.Remove(vk)) injector.Key(vk, false);
        }

        private void PressCombo(string combo)
        {
            var keys = new List<ushort>();
            foreach (string part in combo.Split('+'))
            {
                ushort vk;
                if (!Keys.TryGet(part, out vk)) return; // bilinmeyen tuş: hiçbir şey basma
                keys.Add(vk);
            }
            if (keys.Count == 0) return;

            var pressed = new List<ushort>();
            foreach (ushort vk in keys)
            {
                if (latched.Contains(vk)) continue;
                injector.Key(vk, true);
                pressed.Add(vk);
            }
            for (int i = pressed.Count - 1; i >= 0; i--) injector.Key(pressed[i], false);
        }

        // ---------------------------------------------------------------- yapılandırma

        private void Configure(string[] p)
        {
            bool wantPen = Arg(p, 1) == "pen";
            if (wantPen != penMode)
            {
                OutOfRange();
                penMode = wantPen;
            }
            monitors = Monitors.GetAll();
            int index = Int(p, 2);
            monitorIndex = index >= 0 && index < monitors.Count ? index : 0;
        }

        private MonitorInfo Current()
        {
            if (monitors.Count == 0) monitors = Monitors.GetAll();
            if (monitors.Count == 0)
            {
                return new MonitorInfo { Device = "?", Width = 1920, Height = 1080, Primary = true };
            }
            return monitors[Math.Min(monitorIndex, monitors.Count - 1)];
        }

        private void Map(string[] p, int index, out int x, out int y)
        {
            MonitorInfo m = Current();
            double nx = Math.Max(0.0, Math.Min(1.0, Num(p, index)));
            double ny = Math.Max(0.0, Math.Min(1.0, Num(p, index + 1)));
            x = m.Left + Math.Min(m.Width - 1, (int)(nx * m.Width));
            y = m.Top + Math.Min(m.Height - 1, (int)(ny * m.Height));
        }

        private string HelloJsonLocked()
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"hello\",\"pen\":").Append(injector.PenAvailable ? "true" : "false");
            sb.Append(",\"mode\":\"").Append(penMode ? "pen" : "mouse").Append('"');
            sb.Append(",\"mon\":").Append(monitorIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"monitors\":[");
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo m = monitors[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(Json.Escape(m.Device)).Append('"');
                sb.Append(",\"w\":").Append(m.Width.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"h\":").Append(m.Height.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"primary\":").Append(m.Primary ? "true" : "false").Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- ayrıştırma

        private static string Arg(string[] p, int i)
        {
            return i < p.Length ? p[i] : "";
        }

        private static bool TryNum(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Num(string[] p, int i)
        {
            double v;
            return TryNum(Arg(p, i), out v) ? v : 0.0;
        }

        private static int Int(string[] p, int i)
        {
            double v = Num(p, i);
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(v)));
        }
    }

    /// <summary>Tuş adlarını sanal tuş kodlarına çevirir ("ctrl", "esc", "z", "f6" ...).</summary>
    internal static class Keys
    {
        private static readonly Dictionary<string, ushort> Named =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                { "ctrl", 0x11 }, { "shift", 0x10 }, { "alt", 0x12 }, { "win", 0x5B },
                { "esc", 0x1B }, { "enter", 0x0D }, { "tab", 0x09 }, { "space", 0x20 },
                { "backspace", 0x08 }, { "delete", 0x2E }, { "del", 0x2E }, { "insert", 0x2D },
                { "home", 0x24 }, { "end", 0x23 }, { "pageup", 0x21 }, { "pagedown", 0x22 },
                { "left", 0x25 }, { "up", 0x26 }, { "right", 0x27 }, { "down", 0x28 }
            };

        public static bool TryGet(string name, out ushort vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(name)) return false;
            if (Named.TryGetValue(name, out vk)) return true;
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    vk = c;
                    return true;
                }
            }
            int f;
            if ((name[0] == 'f' || name[0] == 'F') &&
                int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out f) &&
                f >= 1 && f <= 24)
            {
                vk = (ushort)(0x70 + f - 1);
                return true;
            }
            return false;
        }
    }

    internal static class Json
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
