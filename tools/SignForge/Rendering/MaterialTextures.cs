using System;
using System.IO;

namespace SignForge.Rendering
{
    /// <summary>
    /// Procedurally generates the small shared material textures (galvanized steel, perforated steel,
    /// aluminum, dark plastic, sign-face mask maps). Cities: Skylines II conventions:
    ///   _MaskMap:     R = metallic, G = coat, B = unused (black), A = glossiness   (linear)
    ///   _ControlMask: R/G/B = color variation masks, A = snow removal (white = no snow)  (linear)
    /// </summary>
    public static class MaterialTextures
    {
        public static void GenerateAll(string folder)
        {
            Directory.CreateDirectory(folder);
            (PixelBuffer galvColor, PixelBuffer galvMask) = Galvanized(512, 512, 7);
            galvColor.SavePng(Path.Combine(folder, "Galvanized_BaseColor.png"));
            galvMask.SavePng(Path.Combine(folder, "Galvanized_MaskMap.png"));

            (PixelBuffer perfColor, PixelBuffer perfMask) = Perforated(256, 128, 11);
            perfColor.SavePng(Path.Combine(folder, "Perforated_BaseColor.png"));
            perfMask.SavePng(Path.Combine(folder, "Perforated_MaskMap.png"));

            (PixelBuffer alColor, PixelBuffer alMask) = Aluminum(256, 256, 23);
            alColor.SavePng(Path.Combine(folder, "Aluminum_BaseColor.png"));
            alMask.SavePng(Path.Combine(folder, "Aluminum_MaskMap.png"));

            (PixelBuffer darkColor, PixelBuffer darkMask) = DarkPlastic(64, 64, 31);
            darkColor.SavePng(Path.Combine(folder, "DarkPlastic_BaseColor.png"));
            darkMask.SavePng(Path.Combine(folder, "DarkPlastic_MaskMap.png"));

            Uniform(64, 64, 0, 92, 0, 150).SavePng(Path.Combine(folder, "Face_MaskMap.png"));
            Uniform(64, 64, 0, 0, 0, 255).SavePng(Path.Combine(folder, "ControlMask.png"));
        }

        private static PixelBuffer Uniform(int w, int h, byte r, byte g, byte b, byte a)
        {
            var buffer = new PixelBuffer(w, h);
            buffer.Fill(r, g, b, a);
            return buffer;
        }

        /// <summary>Tileable cellular noise returning per-pixel cell value and distance to the cell edge.</summary>
        private static void Cellular(float x, float y, int cellsX, int cellsY, int seed, out float cellValue, out float edge)
        {
            float gx = x * cellsX, gy = y * cellsY;
            int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
            float best = float.MaxValue, second = float.MaxValue;
            cellValue = 0f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = ix + ox, cy = iy + oy;
                    int wx = ((cx % cellsX) + cellsX) % cellsX, wy = ((cy % cellsY) + cellsY) % cellsY;
                    float px = cx + Noise.Hash(wx, wy, seed);
                    float py = cy + Noise.Hash(wx, wy, seed + 101);
                    float dx = gx - px, dy = gy - py;
                    float d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        second = best;
                        best = d;
                        cellValue = Noise.Hash(wx, wy, seed + 202);
                    }
                    else if (d < second)
                    {
                        second = d;
                    }
                }
            }
            edge = (float)(Math.Sqrt(second) - Math.Sqrt(best));
        }

        private static (PixelBuffer baseColor, PixelBuffer mask) Galvanized(int w, int h, int seed)
        {
            var color = new PixelBuffer(w, h);
            var mask = new PixelBuffer(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    Cellular(u, v, 22, 22, seed, out float cell, out float edge);
                    float fine = Noise.Hash(x, y, seed + 5) - 0.5f;
                    float low = Noise.TileValue(u * 5f, v * 5f, 5, 5, seed + 9) - 0.5f;
                    float streak = Noise.TileValue(u * 18f, v * 2f, 18, 2, seed + 13) - 0.5f;
                    float boundary = edge < 0.06f ? -0.035f : 0f;

                    float lum = 0.64f + (cell - 0.5f) * 0.075f + fine * 0.03f + low * 0.05f + streak * 0.03f + boundary;
                    color.Set(x, y,
                        ToSrgbByte(lum * 0.985f),
                        ToSrgbByte(lum * 1.0f),
                        ToSrgbByte(lum * 1.02f),
                        255);

                    float metallic = 0.72f + (cell - 0.5f) * 0.16f;
                    float gloss = 0.46f + (cell - 0.5f) * 0.22f + low * 0.10f + boundary * 2f;
                    mask.Set(x, y, ToLinearByte(metallic), 0, 0, ToLinearByte(gloss));
                }
            }
            return (color, mask);
        }

        /// <summary>One 2 in x 1 in tile of perforated post: hole (7/16 in) in the middle, galvanized around it.</summary>
        private static (PixelBuffer baseColor, PixelBuffer mask) Perforated(int w, int h, int seed)
        {
            (PixelBuffer galv, PixelBuffer galvMask) = Galvanized(w, h, seed);
            var color = new PixelBuffer(w, h);
            var mask = new PixelBuffer(w, h);
            Array.Copy(galv.Bgra, color.Bgra, galv.Bgra.Length);
            Array.Copy(galvMask.Bgra, mask.Bgra, galvMask.Bgra.Length);

            float cx = w * 0.5f, cy = h * 0.5f;
            float pixelsPerInchX = w / 2f, pixelsPerInchY = h / 1f;
            float radiusInches = 0.4375f * 0.5f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - cx) / pixelsPerInchX;
                    float dy = (y + 0.5f - cy) / pixelsPerInchY;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < radiusInches)
                    {
                        // Dark interior with a hint of the far wall.
                        float lum = 0.035f + 0.05f * (1f - d / radiusInches);
                        color.Set(x, y, ToSrgbByte(lum), ToSrgbByte(lum), ToSrgbByte(lum * 1.05f), 255);
                        mask.Set(x, y, 0, 0, 0, ToLinearByte(0.08f));
                    }
                    else if (d < radiusInches + 0.035f)
                    {
                        // Slightly darker punched rim.
                        int o = (y * w + x) * 4;
                        for (int c = 0; c < 3; c++)
                            color.Bgra[o + c] = (byte)(color.Bgra[o + c] * 0.72f);
                        mask.Bgra[o + 3] = (byte)(mask.Bgra[o + 3] * 0.6f);
                    }
                }
            }
            return (color, mask);
        }

        private static (PixelBuffer baseColor, PixelBuffer mask) Aluminum(int w, int h, int seed)
        {
            var color = new PixelBuffer(w, h);
            var mask = new PixelBuffer(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    // Horizontal mill-finish brushing: stretched noise.
                    float brush = Noise.TileValue(u * 64f, v * 256f, 64, 256, seed) - 0.5f;
                    float low = Noise.TileValue(u * 4f, v * 4f, 4, 4, seed + 3) - 0.5f;
                    float fine = Noise.Hash(x, y, seed + 7) - 0.5f;
                    float lum = 0.62f + brush * 0.05f + low * 0.06f + fine * 0.02f;
                    color.Set(x, y, ToSrgbByte(lum), ToSrgbByte(lum * 1.005f), ToSrgbByte(lum * 1.015f), 255);
                    mask.Set(x, y, ToLinearByte(0.62f + low * 0.1f), 0, 0, ToLinearByte(0.40f + brush * 0.12f + low * 0.08f));
                }
            }
            return (color, mask);
        }

        private static (PixelBuffer baseColor, PixelBuffer mask) DarkPlastic(int w, int h, int seed)
        {
            var color = new PixelBuffer(w, h);
            var mask = new PixelBuffer(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fine = Noise.Hash(x, y, seed) - 0.5f;
                    float lum = 0.055f + fine * 0.01f;
                    color.Set(x, y, ToSrgbByte(lum), ToSrgbByte(lum), ToSrgbByte(lum), 255);
                    mask.Set(x, y, 0, 0, 0, ToLinearByte(0.28f + fine * 0.05f));
                }
            }
            return (color, mask);
        }

        /// <summary>Linear reflectance [0,1] to an sRGB-encoded byte.</summary>
        private static byte ToSrgbByte(float linear)
        {
            linear = Math.Max(0f, Math.Min(1f, linear));
            double s = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
            return (byte)Math.Round(s * 255.0);
        }

        private static byte ToLinearByte(float value) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 255f);
    }
}
