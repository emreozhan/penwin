using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PenWin
{
    internal static class Program
    {
        private const int DefaultPort = 8765;

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (IOException)
            {
            }
            EnableDpiAwareness();

            int port = DefaultPort;
            string webDir = null;
            IPAddress bind = IPAddress.Any;
            bool newKey = false;
            bool dryRun = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out port) || port < 1 || port > 65535)
                        {
                            Log.Error("--port için 1-65535 arası bir sayı verin.");
                            return 2;
                        }
                        break;
                    case "--bind":
                        if (i + 1 >= args.Length || !IPAddress.TryParse(args[++i], out bind))
                        {
                            Log.Error("--bind için geçerli bir IP adresi verin (ör. 127.0.0.1).");
                            return 2;
                        }
                        break;
                    case "--web":
                        if (i + 1 < args.Length) webDir = Path.GetFullPath(args[++i]);
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--new-key":
                        newKey = true;
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine("penwin.exe [--port 8765] [--bind 0.0.0.0] [--new-key] [--dry-run] [--web <klasör>]");
                        return 0;
                }
            }

            string token = TokenStore.LoadOrCreate(newKey);
            using (var injector = new Injector(dryRun))
            using (var controller = new Controller(injector))
            {
                var server = new WebServer(bind, port, token, controller, webDir);
                try
                {
                    server.Start();
                }
                catch (SocketException ex)
                {
                    Log.Error("Port " + port + " açılamadı (" + ex.Message + "). Başka bir PenWin açık olabilir; --port ile farklı port deneyin.");
                    return 1;
                }

                PrintBanner(bind, port, token, injector.PenAvailable, controller.CurrentMonitors, webDir);
                if (dryRun) Log.Warn("Deneme modu: iPad'den gelenler işlenir ama Windows'a girdi gönderilmez.");
                Console.CancelKeyPress += delegate { controller.ReleaseAll(); };
                server.Run();
            }
            return 0;
        }

        private static void EnableDpiAwareness()
        {
            // Fiziksel piksellerle çalışmak için; yoksa ölçekli ekranlarda imleç kayar.
            try
            {
                if (Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return;
            }
            catch (EntryPointNotFoundException)
            {
            }
            Native.SetProcessDPIAware();
        }

        private static void PrintBanner(IPAddress bind, int port, string token, bool penAvailable, List<MonitorInfo> monitors, string webDir)
        {
            Console.WriteLine();
            Log.Title("  PenWin — iPad kalemi → Windows");
            Console.WriteLine();
            Console.WriteLine("  iPad'de Safari ile şu adresi açın:");
            List<string> addresses = bind.Equals(IPAddress.Any) ? LanAddresses() : new List<string> { bind.ToString() };
            if (addresses.Count == 0) addresses.Add("<bu-bilgisayarın-ip-adresi>");
            foreach (string ip in addresses)
            {
                Log.Url("     http://" + ip + ":" + port + "/#k=" + token);
            }
            Console.WriteLine();
            Console.WriteLine("  Anahtar      : " + token);
            Console.WriteLine("  Kalem modu   : " + (penAvailable ? "kullanılabilir (Windows Ink, basınç + eğim)" : "yok, yalnızca fare modu"));
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorInfo m = monitors[i];
                Console.WriteLine("  Ekran " + (i + 1) + "      : " + m.Width + "x" + m.Height + (m.Primary ? " (ana)" : ""));
            }
            if (webDir != null) Console.WriteLine("  Web klasörü  : " + webDir + " (geliştirme modu)");
            Console.WriteLine();
            Console.WriteLine("  İlk açılışta Windows Güvenlik Duvarı sorarsa 'Özel ağlar' için izin verin.");
            Console.WriteLine("  Kapatmak için Ctrl+C.");
            Console.WriteLine();
        }

        /// <summary>Ağ geçidi olan (gerçek ağa bağlı) IPv4 adreslerini önce listeler.</summary>
        private static List<string> LanAddresses()
        {
            var preferred = new List<string>();
            var others = new List<string>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                IPInterfaceProperties props = nic.GetIPProperties();
                bool hasGateway = false;
                foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork) hasGateway = true;
                }
                foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                    (hasGateway ? preferred : others).Add(ip);
                }
            }
            return preferred.Count > 0 ? preferred : others;
        }
    }

    /// <summary>Bağlantı anahtarını %LOCALAPPDATA%\PenWin altında saklar; yer imi yeniden derlemede bozulmaz.</summary>
    internal static class TokenStore
    {
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // karışan karakterler yok
        private const int Length = 6;

        public static string LoadOrCreate(bool regenerate)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PenWin");
            string path = Path.Combine(dir, "token.txt");
            if (!regenerate && File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim().ToUpperInvariant();
                if (IsValid(existing)) return existing;
            }

            string token = Generate();
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, token);
            return token;
        }

        private static bool IsValid(string token)
        {
            if (token.Length != Length) return false;
            foreach (char c in token)
            {
                if (Alphabet.IndexOf(c) < 0) return false;
            }
            return true;
        }

        private static string Generate()
        {
            var bytes = new byte[Length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            var sb = new StringBuilder(Length);
            foreach (byte b in bytes) sb.Append(Alphabet[b % Alphabet.Length]);
            return sb.ToString();
        }
    }

    internal static class Log
    {
        private static readonly object Sync = new object();

        public static void Title(string text) { Write(text, ConsoleColor.Cyan, false); }
        public static void Url(string text) { Write(text, ConsoleColor.Yellow, false); }
        public static void Info(string text) { Write(text, ConsoleColor.Gray, true); }
        public static void Ok(string text) { Write(text, ConsoleColor.Green, true); }
        public static void Warn(string text) { Write(text, ConsoleColor.DarkYellow, true); }
        public static void Error(string text) { Write(text, ConsoleColor.Red, true); }

        private static void Write(string text, ConsoleColor color, bool stamp)
        {
            lock (Sync)
            {
                ConsoleColor old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(stamp ? "  [" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + text : text);
                Console.ForegroundColor = old;
            }
        }
    }
}
