using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PenWin
{
    /// <summary>Bir monitörün ekran görüntüsünü küçültülmüş JPEG olarak üretir (iPad'deki arka plan için).</summary>
    internal static class ScreenCapture
    {
        private const long JpegQuality = 60;
        private static readonly ImageCodecInfo JpegCodec = FindJpegCodec();

        /// <summary>Monitörü yakalar; kilit ekranı/UAC gibi güvenli masaüstünde null döner.</summary>
        public static byte[] CaptureJpeg(MonitorInfo monitor, int maxWidth)
        {
            int width = Math.Max(1, Math.Min(monitor.Width, maxWidth));
            int height = Math.Max(1, (int)Math.Round(monitor.Height * (double)width / monitor.Width));

            using (var full = new Bitmap(monitor.Width, monitor.Height, PixelFormat.Format24bppRgb))
            {
                try
                {
                    using (Graphics g = Graphics.FromImage(full))
                    {
                        g.CopyFromScreen(monitor.Left, monitor.Top, 0, 0, full.Size, CopyPixelOperation.SourceCopy);
                    }
                }
                catch (Win32Exception)
                {
                    return null;
                }

                using (var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (var ms = new MemoryStream())
                {
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                        g.DrawImage(full, 0, 0, width, height);
                    }
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                        scaled.Save(ms, JpegCodec, parameters);
                    }
                    return ms.ToArray();
                }
            }
        }

        private static ImageCodecInfo FindJpegCodec()
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
            }
            throw new InvalidOperationException("JPEG kodlayıcı bulunamadı.");
        }
    }
}
