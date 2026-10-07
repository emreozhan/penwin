using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PenWin
{
    /// <summary>
    /// Bağımlılıksız küçük HTTP + WebSocket sunucusu. Statik sayfayı (gömülü kaynaklar) sunar,
    /// /ws üzerinden tek bir aktif iPad bağlantısını kabul eder.
    /// </summary>
    internal sealed class WebServer
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int SocketTimeoutMs = 10000; // istemci 2 sn'de bir ping atar

        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            { "/", "index.html" }, { "/index.html", "index.html" },
            { "/app.js", "app.js" }, { "/style.css", "style.css" }
        };

        private readonly IPAddress bind;
        private readonly int port;
        private readonly string token;
        private readonly Controller controller;
        private readonly string webDir; // geliştirme: dosyaları diskten sun
        private readonly object activeLock = new object();
        private TcpListener listener;
        private WebSocketConnection active;

        public WebServer(IPAddress bind, int port, string token, Controller controller, string webDir)
        {
            this.bind = bind;
            this.port = port;
            this.token = token;
            this.controller = controller;
            this.webDir = webDir;
        }

        public void Start()
        {
            listener = new TcpListener(bind, port);
            listener.Start();
        }

        public void Run()
        {
            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                var thread = new Thread(() => Serve(client)) { IsBackground = true, Name = "client" };
                thread.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                client.NoDelay = true; // Nagle kapalı: küçük kalem paketleri beklemeden gitsin
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = SocketTimeoutMs;
                stream.WriteTimeout = SocketTimeoutMs;

                HttpRequest request = HttpRequest.Read(stream);
                if (request == null) return;

                if (request.Path == "/ws" && request.IsWebSocketUpgrade)
                {
                    ServeSocket(client, stream, request);
                }
                else if (request.Path == "/shot.jpg")
                {
                    ServeScreenshot(stream, request);
                }
                else
                {
                    ServeFile(stream, request);
                }
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Log.Warn("Beklenmeyen hata: " + ex.Message);
            }
            finally
            {
                client.Close();
            }
        }

        // ---------------------------------------------------------------- WebSocket

        private void ServeSocket(TcpClient client, NetworkStream stream, HttpRequest request)
        {
            string key = request.Header("Sec-WebSocket-Key");
            if (string.IsNullOrEmpty(key))
            {
                WriteResponse(stream, 400, "Bad Request", "text/plain", Encoding.ASCII.GetBytes("bad request"));
                return;
            }

            string accept;
            using (var sha1 = SHA1.Create())
            {
                accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
            }
            byte[] head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
            stream.Write(head, 0, head.Length);

            var ws = new WebSocketConnection(client, stream);
            string remote = client.Client.RemoteEndPoint.ToString();

            if (!TokenMatches(request.Query("k")))
            {
                Thread.Sleep(800); // tahmin denemelerini yavaşlat
                ws.SendText("{\"t\":\"error\",\"code\":\"token\"}");
                ws.Close();
                Log.Warn("Yanlış anahtarla bağlantı denemesi: " + remote);
                return;
            }

            WebSocketConnection previous;
            lock (activeLock)
            {
                previous = active;
                active = ws;
            }
            if (previous != null)
            {
                controller.ReleaseAll();
                previous.SendText("{\"t\":\"error\",\"code\":\"replaced\"}");
                previous.Close();
            }

            Log.Ok("iPad bağlandı: " + remote);
            ws.SendText(controller.HelloJson());
            try
            {
                while (true)
                {
                    string message = ws.ReadText();
                    if (message == null || !IsActive(ws)) break;
                    foreach (string line in message.Split('\n'))
                    {
                        string reply = controller.Handle(line);
                        if (reply != null) ws.SendText(reply);
                    }
                }
            }
            finally
            {
                bool wasActive;
                lock (activeLock)
                {
                    wasActive = active == ws;
                    if (wasActive) active = null;
                }
                if (wasActive)
                {
                    controller.ReleaseAll();
                    Log.Info("iPad bağlantısı kapandı: " + remote);
                }
            }
        }

        private bool IsActive(WebSocketConnection ws)
        {
            lock (activeLock) return active == ws;
        }

        private bool TokenMatches(string candidate)
        {
            if (candidate == null || candidate.Length != token.Length) return false;
            int diff = 0;
            for (int i = 0; i < token.Length; i++)
            {
                diff |= char.ToUpperInvariant(candidate[i]) ^ token[i];
            }
            return diff == 0;
        }

        // ---------------------------------------------------------------- ekran görüntüsü

        private const int DefaultShotWidth = 1280;
        private const int MaxShotWidth = 2560;

        /// <summary>Seçili monitörün JPEG görüntüsü: /shot.jpg?k=ANAHTAR&amp;w=GENİŞLİK</summary>
        private void ServeScreenshot(NetworkStream stream, HttpRequest request)
        {
            if (!TokenMatches(request.Query("k")))
            {
                Thread.Sleep(800);
                WriteResponse(stream, 403, "Forbidden", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("anahtar hatalı"));
                return;
            }
            int width;
            if (!int.TryParse(request.Query("w"), out width)) width = DefaultShotWidth;
            width = Math.Max(160, Math.Min(MaxShotWidth, width));

            byte[] jpeg = ScreenCapture.CaptureJpeg(controller.SelectedMonitor, width);
            if (jpeg == null)
            {
                // Kilit ekranı / UAC penceresi gibi güvenli masaüstü yakalanamaz.
                WriteResponse(stream, 503, "Service Unavailable", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("ekran yakalanamadı"));
                return;
            }
            WriteResponse(stream, 200, "OK", "image/jpeg", jpeg);
        }

        // ---------------------------------------------------------------- statik dosyalar

        private void ServeFile(NetworkStream stream, HttpRequest request)
        {
            string name = null;
            byte[] body = null;
            if (request.Method == "GET" && Files.TryGetValue(request.Path, out name))
            {
                body = LoadFile(name);
            }
            if (body == null)
            {
                WriteResponse(stream, 404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("bulunamadı"));
                return;
            }
            WriteResponse(stream, 200, "OK", ContentType(name), body);
        }

        private byte[] LoadFile(string name)
        {
            if (webDir != null)
            {
                string path = Path.Combine(webDir, name);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static string ContentType(string name)
        {
            if (name.EndsWith(".html", StringComparison.Ordinal)) return "text/html; charset=utf-8";
            if (name.EndsWith(".js", StringComparison.Ordinal)) return "text/javascript; charset=utf-8";
            if (name.EndsWith(".css", StringComparison.Ordinal)) return "text/css; charset=utf-8";
            return "application/octet-stream";
        }

        private static void WriteResponse(Stream stream, int status, string reason, string contentType, byte[] body)
        {
            byte[] head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + status + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Cache-Control: no-cache\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
        }
    }

    /// <summary>Yalnızca ihtiyaç duyulan kadar ayrıştırılmış HTTP isteği.</summary>
    internal sealed class HttpRequest
    {
        private const int MaxHeaderBytes = 16 * 1024;

        public string Method;
        public string Path;
        private string query = "";
        private readonly Dictionary<string, string> headers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsWebSocketUpgrade
        {
            get
            {
                string upgrade = Header("Upgrade");
                return upgrade != null && upgrade.Trim().Equals("websocket", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string Header(string name)
        {
            string value;
            return headers.TryGetValue(name, out value) ? value : null;
        }

        public string Query(string name)
        {
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == name) return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return null;
        }

        /// <summary>Başlıkları boş satıra kadar okur. Bağlantı kapanırsa null döner.</summary>
        public static HttpRequest Read(Stream stream)
        {
            var buffer = new List<byte>(512);
            int state = 0; // \r\n\r\n eşleşme durumu
            while (state < 4)
            {
                int b = stream.ReadByte();
                if (b < 0 || buffer.Count >= MaxHeaderBytes) return null;
                buffer.Add((byte)b);
                if ((b == '\r' && (state == 0 || state == 2)) || (b == '\n' && (state == 1 || state == 3))) state++;
                else state = b == '\r' ? 1 : 0;
            }

            string[] lines = Encoding.ASCII.GetString(buffer.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;

            var request = new HttpRequest { Method = first[0] };
            string target = first[1];
            int q = target.IndexOf('?');
            request.Path = q >= 0 ? target.Substring(0, q) : target;
            if (q >= 0) request.query = target.Substring(q + 1);

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) request.headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            return request;
        }
    }

    /// <summary>RFC 6455 sunucu tarafı: maskeli istemci çerçevelerini okur, maskesiz metin gönderir.</summary>
    internal sealed class WebSocketConnection
    {
        private const int MaxMessageBytes = 64 * 1024;
        private readonly TcpClient client;
        private readonly Stream stream;
        private readonly object writeLock = new object();

        public WebSocketConnection(TcpClient client, Stream stream)
        {
            this.client = client;
            this.stream = stream;
        }

        /// <summary>Bir metin mesajı okur; bağlantı kapanırsa ya da protokol ihlalinde null döner.</summary>
        public string ReadText()
        {
            var message = new MemoryStream();
            while (true)
            {
                byte[] head = ReadExact(2);
                if (head == null) return null;
                bool fin = (head[0] & 0x80) != 0;
                int opcode = head[0] & 0x0F;
                bool masked = (head[1] & 0x80) != 0;
                long length = head[1] & 0x7F;

                if (length == 126)
                {
                    byte[] ext = ReadExact(2);
                    if (ext == null) return null;
                    length = (ext[0] << 8) | ext[1];
                }
                else if (length == 127)
                {
                    byte[] ext = ReadExact(8);
                    if (ext == null) return null;
                    length = 0;
                    for (int i = 0; i < 8; i++) length = (length << 8) | ext[i];
                }
                if (!masked || length < 0 || length > MaxMessageBytes) return null;

                byte[] mask = ReadExact(4);
                byte[] payload = mask == null ? null : ReadExact((int)length);
                if (payload == null) return null;
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];

                switch (opcode)
                {
                    case 0x0: // devam
                    case 0x1: // metin
                    case 0x2: // ikili
                        message.Write(payload, 0, payload.Length);
                        if (message.Length > MaxMessageBytes) return null;
                        if (fin) return Encoding.UTF8.GetString(message.ToArray());
                        break;
                    case 0x8: // kapat
                        SendFrame(0x8, new byte[0]);
                        return null;
                    case 0x9: // ping
                        SendFrame(0xA, payload);
                        break;
                    case 0xA: // pong
                        break;
                    default:
                        return null;
                }
            }
        }

        public void SendText(string text)
        {
            SendFrame(0x1, Encoding.UTF8.GetBytes(text));
        }

        public void Close()
        {
            SendFrame(0x8, new byte[0]);
            try
            {
                client.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void SendFrame(int opcode, byte[] payload)
        {
            int extra = payload.Length < 126 ? 0 : payload.Length <= 0xFFFF ? 2 : 8;
            var frame = new byte[2 + extra + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            if (extra == 0)
            {
                frame[1] = (byte)payload.Length;
            }
            else if (extra == 2)
            {
                frame[1] = 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)payload.Length;
            }
            else
            {
                frame[1] = 127;
                for (int i = 0; i < 8; i++) frame[2 + i] = (byte)((long)payload.Length >> (56 - 8 * i));
            }
            Buffer.BlockCopy(payload, 0, frame, 2 + extra, payload.Length);

            lock (writeLock)
            {
                try
                {
                    stream.Write(frame, 0, frame.Length);
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private byte[] ReadExact(int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return null;
                offset += read;
            }
            return buffer;
        }
    }
}
