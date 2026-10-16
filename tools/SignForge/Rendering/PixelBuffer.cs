using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SignForge.Rendering
{
    /// <summary>Straight (non-premultiplied) BGRA8 image buffer.</summary>
    public sealed class PixelBuffer
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Bgra;

        public PixelBuffer(int width, int height)
        {
            Width = width;
            Height = height;
            Bgra = new byte[width * height * 4];
        }

        /// <summary>Renders a visual at the given pixel size (1 WPF unit = 1 pixel) and returns straight BGRA.</summary>
        public static PixelBuffer Render(Visual visual, int width, int height)
        {
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var buffer = new PixelBuffer(width, height);
            rtb.CopyPixels(buffer.Bgra, width * 4, 0);
            buffer.Unpremultiply();
            return buffer;
        }

        /// <summary>Renders with supersampling and a box-filter downsample for clean edges.</summary>
        public static PixelBuffer RenderSupersampled(Action<DrawingContext> draw, int width, int height, int factor)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(factor, factor));
                draw(dc);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(width * factor, height * factor, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            byte[] big = new byte[width * factor * height * factor * 4];
            rtb.CopyPixels(big, width * factor * 4, 0);

            var result = new PixelBuffer(width, height);
            int bigWidth = width * factor;
            int samples = factor * factor;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0;
                    for (int sy = 0; sy < factor; sy++)
                    {
                        int row = (y * factor + sy) * bigWidth;
                        for (int sx = 0; sx < factor; sx++)
                        {
                            int i = (row + x * factor + sx) * 4;
                            b += big[i];
                            g += big[i + 1];
                            r += big[i + 2];
                            a += big[i + 3];
                        }
                    }
                    int o = (y * width + x) * 4;
                    result.Bgra[o] = (byte)((b + samples / 2) / samples);
                    result.Bgra[o + 1] = (byte)((g + samples / 2) / samples);
                    result.Bgra[o + 2] = (byte)((r + samples / 2) / samples);
                    result.Bgra[o + 3] = (byte)((a + samples / 2) / samples);
                }
            }
            result.Unpremultiply();
            return result;
        }

        private void Unpremultiply()
        {
            for (int i = 0; i < Bgra.Length; i += 4)
            {
                byte a = Bgra[i + 3];
                if (a == 0 || a == 255)
                    continue;
                Bgra[i] = (byte)Math.Min(255, Bgra[i] * 255 / a);
                Bgra[i + 1] = (byte)Math.Min(255, Bgra[i + 1] * 255 / a);
                Bgra[i + 2] = (byte)Math.Min(255, Bgra[i + 2] * 255 / a);
            }
        }

        /// <summary>
        /// Adds very subtle luminance grain and low-frequency mottling so printed sheeting does not look like
        /// flat vector art at close range.
        /// </summary>
        public void AddSheetingGrain(int seed, float fineAmplitude, float mottleAmplitude)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int o = (y * Width + x) * 4;
                    if (Bgra[o + 3] == 0)
                        continue;
                    float fine = (Noise.Hash(x, y, seed) - 0.5f) * 2f * fineAmplitude;
                    float mottle = (Noise.Value(x / 23f, y / 23f, seed + 17) - 0.5f) * 2f * mottleAmplitude;
                    float f = 1f + fine + mottle;
                    Bgra[o] = ClampByte(Bgra[o] * f);
                    Bgra[o + 1] = ClampByte(Bgra[o + 1] * f);
                    Bgra[o + 2] = ClampByte(Bgra[o + 2] * f);
                }
            }
        }

        public static byte ClampByte(float v) => (byte)(v < 0f ? 0f : (v > 255f ? 255f : v + 0.5f));

        public void SavePng(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            BitmapSource source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Bgra, Width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (FileStream fs = File.Create(path))
                encoder.Save(fs);
        }

        public void Fill(byte r, byte g, byte b, byte a)
        {
            for (int i = 0; i < Bgra.Length; i += 4)
            {
                Bgra[i] = b;
                Bgra[i + 1] = g;
                Bgra[i + 2] = r;
                Bgra[i + 3] = a;
            }
        }

        public void Set(int x, int y, byte r, byte g, byte b, byte a)
        {
            int o = (y * Width + x) * 4;
            Bgra[o] = b;
            Bgra[o + 1] = g;
            Bgra[o + 2] = r;
            Bgra[o + 3] = a;
        }
    }

    public static class Noise
    {
        /// <summary>Deterministic hash in [0, 1).</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Smooth value noise in [0, 1].</summary>
        public static float Value(float x, float y, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
            float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
            return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
        }

        /// <summary>Tileable value noise with the given integer period.</summary>
        public static float TileValue(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            int xa = Mod(x0, periodX), xb = Mod(x0 + 1, periodX), ya = Mod(y0, periodY), yb = Mod(y0 + 1, periodY);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed);
            float c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
        }

        private static int Mod(int v, int m) => ((v % m) + m) % m;
    }
}
