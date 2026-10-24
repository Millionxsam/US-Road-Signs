using System;
using USRoadSigns.Geometry;

namespace USRoadSigns.Digital
{
    /// <summary>RGBA8 pixels, bottom row first (the layout Unity and the game's texture importer expect).</summary>
    public sealed class RgbaImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Pixels;

        public RgbaImage(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        /// <summary>Sets a pixel; <paramref name="yTop"/> counts from the top row.</summary>
        public void Set(int x, int yTop, byte r, byte g, byte b, byte a = 255)
        {
            int i = ((Height - 1 - yTop) * Width + x) * 4;
            Pixels[i] = r;
            Pixels[i + 1] = g;
            Pixels[i + 2] = b;
            Pixels[i + 3] = a;
        }

        public void Fill(byte r, byte g, byte b, byte a)
        {
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                Pixels[i] = r;
                Pixels[i + 1] = g;
                Pixels[i + 2] = b;
                Pixels[i + 3] = a;
            }
        }
    }

    /// <summary>
    /// Procedural textures for digital signs: tiling material textures for the structures (generated once at load)
    /// and display textures for each message page (generated when a sign's message changes).
    /// Mask maps follow the game's convention: R = metallic, G = coat, B = unused, A = glossiness (linear).
    /// </summary>
    public static class DigitalTextures
    {
        // ------------------------------------------------------------------------------------------
        // Structure materials
        // ------------------------------------------------------------------------------------------

        public static RgbaImage BaseColor(MaterialSlot slot)
        {
            switch (slot)
            {
                case MaterialSlot.OrangePaint: return Painted(128, 232, 98, 28, 10f, 3);
                case MaterialSlot.BlackHousing: return Painted(128, 30, 30, 32, 5f, 5);
                case MaterialSlot.Concrete: return Painted(256, 168, 165, 156, 18f, 9);
                case MaterialSlot.Rubber: return Painted(64, 24, 24, 25, 4f, 13);
                case MaterialSlot.LedMatrix: return LedTile(32);
                case MaterialSlot.Solar: return SolarCell(64);
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        public static RgbaImage MaskMap(MaterialSlot slot)
        {
            var image = new RgbaImage(16, 16);
            switch (slot)
            {
                case MaterialSlot.OrangePaint: image.Fill(0, 40, 0, 110); break;
                case MaterialSlot.BlackHousing: image.Fill(0, 0, 0, 85); break;
                case MaterialSlot.Concrete: image.Fill(0, 0, 0, 30); break;
                case MaterialSlot.Rubber: image.Fill(0, 0, 0, 45); break;
                case MaterialSlot.LedMatrix: image.Fill(0, 90, 0, 150); break;
                case MaterialSlot.Solar: image.Fill(20, 160, 0, 215); break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
            return image;
        }

        /// <summary>Flat color with low-frequency mottling and fine grain.</summary>
        private static RgbaImage Painted(int size, int r, int g, int b, float variation, int seed)
        {
            var image = new RgbaImage(size, size);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = (ValueNoise(x / 32f, y / 32f, seed, size / 32) - 0.5f) * variation
                            + (Hash(x, y, seed + 1) - 0.5f) * variation * 0.6f;
                    image.Set(x, y, Byte(r + n), Byte(g + n), Byte(b + n));
                }
            }
            return image;
        }

        /// <summary>One unlit LED with its louver, for the cabinet window (UV 1 = one LED pitch).</summary>
        private static RgbaImage LedTile(int size)
        {
            var image = new RgbaImage(size, size);
            float[] cell = CellCoverage(size, out float[] highlight);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float c = cell[y * size + x];
                    float bg = y < size * 0.18f ? 6f : 13f;
                    float v = bg + (36f - bg) * c + highlight[y * size + x] * 14f;
                    image.Set(x, y, Byte(v), Byte(v), Byte(v + 1));
                }
            }
            return image;
        }

        private static RgbaImage SolarCell(int size)
        {
            var image = new RgbaImage(size, size);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool gap = x < 2 || y < 2;
                    bool busbar = Math.Abs(x - size / 3) < 1 || Math.Abs(x - 2 * size / 3) < 1;
                    if (gap)
                        image.Set(x, y, 190, 194, 200);
                    else if (busbar)
                        image.Set(x, y, 120, 128, 140);
                    else
                    {
                        float n = (Hash(x, y, 77) - 0.5f) * 8f;
                        image.Set(x, y, Byte(22 + n), Byte(34 + n), Byte(70 + n));
                    }
                }
            }
            return image;
        }

        // ------------------------------------------------------------------------------------------
        // Display pages
        // ------------------------------------------------------------------------------------------

        /// <summary>Texels per LED for a display of the given width (keeps textures near 1k wide).</summary>
        public static int TexelsPerLed(int columns)
        {
            if (columns <= 40)
                return 12;
            if (columns <= 64)
                return 10;
            if (columns <= 100)
                return 8;
            return 7;
        }

        /// <summary>
        /// Renders a full-matrix display frame. <paramref name="baseColor"/> is the daylight look (lit LEDs in color,
        /// dark LEDs and louvers), <paramref name="emissive"/> only contains the lit LEDs.
        /// </summary>
        public static void RenderMatrix(LedFrame frame, LedColor color, out RgbaImage baseColor, out RgbaImage emissive)
        {
            int p = TexelsPerLed(frame.Columns);
            RenderCells(frame, frame, p, 0.36f, color, out baseColor, out emissive);
        }

        /// <summary>Renders an arrow board: <paramref name="lamps"/> marks where lamps exist, <paramref name="frame"/> which are lit.</summary>
        public static void RenderLamps(LedFrame lamps, LedFrame frame, LedColor color, out RgbaImage baseColor, out RgbaImage emissive)
        {
            RenderCells(lamps, frame, 28, 0.46f, color, out baseColor, out emissive);
        }

        private static void RenderCells(LedFrame present, LedFrame lit, int p, float radius, LedColor color, out RgbaImage baseColor, out RgbaImage emissive)
        {
            LedColors.Get(color, out byte lr, out byte lg, out byte lb);
            float[] cell = CellCoverage(p, out float[] highlight, radius);

            int w = present.Columns * p;
            int h = present.Rows * p;
            baseColor = new RgbaImage(w, h);
            emissive = new RgbaImage(w, h);
            baseColor.Fill(0, 0, 0, 255);
            emissive.Fill(0, 0, 0, 255);

            for (int row = 0; row < present.Rows; row++)
            {
                for (int col = 0; col < present.Columns; col++)
                {
                    bool exists = present[col, row];
                    bool on = exists && lit[col, row];
                    for (int y = 0; y < p; y++)
                    {
                        for (int x = 0; x < p; x++)
                        {
                            int i = y * p + x;
                            float c = exists ? cell[i] : 0f;
                            int tx = col * p + x;
                            int ty = row * p + y;
                            float bg = y < p * 0.16f ? 5f : 11f;   // louver shade above each LED row
                            if (on)
                            {
                                // Bright core shading to the LED color at the rim.
                                float core = c * c;
                                float r = bg + (lr - bg) * c + (255 - lr) * core * 0.35f;
                                float g = bg + (lg - bg) * c + (255 - lg) * core * 0.35f;
                                float b = bg + (lb - bg) * c + (255 - lb) * core * 0.35f;
                                baseColor.Set(tx, ty, Byte(r), Byte(g), Byte(b));
                                emissive.Set(tx, ty, Byte(lr * c + (255 - lr) * core * 0.25f), Byte(lg * c + (255 - lg) * core * 0.25f), Byte(lb * c + (255 - lb) * core * 0.25f));
                            }
                            else
                            {
                                float v = bg + (34f - bg) * c + highlight[i] * 16f * (exists ? 1f : 0f);
                                baseColor.Set(tx, ty, Byte(v), Byte(v), Byte(v + 1f));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Anti-aliased disc coverage for one LED cell plus a small specular highlight on the lens.</summary>
        private static float[] CellCoverage(int p, out float[] highlight, float radius = 0.36f)
        {
            var coverage = new float[p * p];
            highlight = new float[p * p];
            float r = radius * p;
            float center = p * 0.5f;
            const int ss = 4;
            for (int y = 0; y < p; y++)
            {
                for (int x = 0; x < p; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float dx = x + (sx + 0.5f) / ss - center;
                            float dy = y + (sy + 0.5f) / ss - center;
                            if (dx * dx + dy * dy <= r * r)
                                inside++;
                        }
                    }
                    coverage[y * p + x] = inside / (float)(ss * ss);

                    float hx = x + 0.5f - (center - r * 0.35f);
                    float hy = y + 0.5f - (center - r * 0.4f);
                    float hd = (float)Math.Sqrt(hx * hx + hy * hy) / (r * 0.35f);
                    highlight[y * p + x] = hd < 1f ? (1f - hd) * coverage[y * p + x] : 0f;
                }
            }
            return coverage;
        }

        /// <summary>Round beacon lens, lit or dark (square texture, black outside the lens).</summary>
        public static RgbaImage BeaconLens(bool lit, int size = 32)
        {
            var image = new RgbaImage(size, size);
            float[] cell = CellCoverage(size, out float[] highlight, 0.48f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float c = cell[i];
                    if (lit)
                    {
                        float core = c * c;
                        image.Set(x, y, Byte(255 * c), Byte((150 + 90 * core) * c), Byte((20 + 150 * core) * c));
                    }
                    else
                    {
                        float v = 40f * c + highlight[i] * 40f;
                        image.Set(x, y, Byte(v * 1.1f), Byte(v * 0.75f), Byte(v * 0.2f));
                    }
                }
            }
            return image;
        }

        // ------------------------------------------------------------------------------------------

        private static byte Byte(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f));

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Tileable value noise with a period of <paramref name="period"/> cells.</summary>
        private static float ValueNoise(float x, float y, int seed, int period)
        {
            period = Math.Max(1, period);
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float Corner(int cx, int cy) => Hash(((cx % period) + period) % period, ((cy % period) + period) % period, seed);
            float a = Corner(x0, y0), b = Corner(x0 + 1, y0), c = Corner(x0, y0 + 1), d = Corner(x0 + 1, y0 + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }
    }
}
