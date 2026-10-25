using System;
using System.Collections.Generic;
using USRoadSigns.Catalog;
using USRoadSigns.Digital;
using USRoadSigns.Geometry;

namespace USRoadSigns.Route
{
    /// <summary>
    /// Small anti-aliased vector rasterizer for sign faces generated at runtime. Drawing coordinates are inches
    /// with the origin at the center of the face and +Y up (the convention of <see cref="ShapeLibrary"/>).
    /// Polygons are filled with the nonzero rule using 5 sub-scanlines and exact horizontal coverage; strokes
    /// (the letters of <see cref="StrokeFont"/>) use the analytic distance to each segment.
    /// </summary>
    public sealed class FaceCanvas
    {
        private const int SubScanlines = 5;

        public readonly int Width;
        public readonly int Height;
        public readonly float FaceWidth;
        public readonly float FaceHeight;

        private readonly float m_ScaleX;
        private readonly float m_ScaleY;
        private readonly float[] m_R, m_G, m_B;
        private readonly float[] m_Coverage;

        public FaceCanvas(float faceWidth, float faceHeight, float pixelsPerInch, int maxSize = 1024)
        {
            FaceWidth = faceWidth;
            FaceHeight = faceHeight;
            float ppi = Math.Min(pixelsPerInch, maxSize / Math.Max(faceWidth, faceHeight));
            Width = Math.Max(8, (int)Math.Round(faceWidth * ppi / 4f) * 4);
            Height = Math.Max(8, (int)Math.Round(faceHeight * ppi / 4f) * 4);
            m_ScaleX = Width / faceWidth;
            m_ScaleY = Height / faceHeight;
            m_R = new float[Width * Height];
            m_G = new float[Width * Height];
            m_B = new float[Width * Height];
            m_Coverage = new float[Width * Height];
        }

        public float PixelsPerInch => (m_ScaleX + m_ScaleY) * 0.5f;

        private float PxX(float x) => (x + FaceWidth * 0.5f) * m_ScaleX;
        private float PxY(float y) => (FaceHeight * 0.5f - y) * m_ScaleY;   // from the top row

        public void Clear(RgbaColor color)
        {
            Linear(color, out float r, out float g, out float b);
            for (int i = 0; i < m_R.Length; i++)
            {
                m_R[i] = r;
                m_G[i] = g;
                m_B[i] = b;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Shapes
        // ------------------------------------------------------------------------------------------

        public void FillPolygon(IReadOnlyList<Vec2> polygon, RgbaColor color)
        {
            FillPolygons(new[] { polygon }, color);
        }

        /// <summary>Fills several contours together (nonzero winding, so oppositely wound contours cut holes).</summary>
        public void FillPolygons(IReadOnlyList<IReadOnlyList<Vec2>> contours, RgbaColor color)
        {
            var edges = new List<(float X0, float Y0, float X1, float Y1, int Dir)>();
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (IReadOnlyList<Vec2> contour in contours)
            {
                for (int i = 0; i < contour.Count; i++)
                {
                    Vec2 a = contour[i];
                    Vec2 b = contour[(i + 1) % contour.Count];
                    float ax = PxX(a.X), ay = PxY(a.Y), bx = PxX(b.X), by = PxY(b.Y);
                    minX = Math.Min(minX, Math.Min(ax, bx));
                    maxX = Math.Max(maxX, Math.Max(ax, bx));
                    minY = Math.Min(minY, Math.Min(ay, by));
                    maxY = Math.Max(maxY, Math.Max(ay, by));
                    if (Math.Abs(ay - by) < 1e-6f)
                        continue;
                    if (ay < by)
                        edges.Add((ax, ay, bx, by, 1));
                    else
                        edges.Add((bx, by, ax, ay, -1));
                }
            }
            if (edges.Count == 0)
                return;

            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(Width - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
            if (x0 > x1 || y0 > y1)
                return;

            var crossings = new List<(float X, int Dir)>();
            var row = new float[Width];
            for (int py = y0; py <= y1; py++)
            {
                Array.Clear(row, x0, x1 - x0 + 1);
                for (int s = 0; s < SubScanlines; s++)
                {
                    float sy = py + (s + 0.5f) / SubScanlines;
                    crossings.Clear();
                    foreach (var e in edges)
                    {
                        if (sy < e.Y0 || sy >= e.Y1)
                            continue;
                        float t = (sy - e.Y0) / (e.Y1 - e.Y0);
                        crossings.Add((e.X0 + (e.X1 - e.X0) * t, e.Dir));
                    }
                    if (crossings.Count < 2)
                        continue;
                    crossings.Sort((a, b) => a.X.CompareTo(b.X));
                    int winding = 0;
                    for (int k = 0; k < crossings.Count - 1; k++)
                    {
                        winding += crossings[k].Dir;
                        if (winding != 0)
                            AddSpan(row, crossings[k].X, crossings[k + 1].X, 1f / SubScanlines, x0, x1);
                    }
                }
                int rowStart = py * Width;
                for (int px = x0; px <= x1; px++)
                {
                    float c = row[px];
                    if (c > 0f)
                        m_Coverage[rowStart + px] = Math.Min(1f, c);
                }
            }
            Blend(color, x0, y0, x1, y1);
        }

        private static void AddSpan(float[] row, float xa, float xb, float weight, int minX, int maxX)
        {
            if (xb <= xa)
                return;
            xa = Math.Max(xa, minX);
            xb = Math.Min(xb, maxX + 1);
            if (xb <= xa)
                return;
            int ia = (int)Math.Floor(xa), ib = (int)Math.Floor(xb);
            if (ia == ib)
            {
                row[ia] += (xb - xa) * weight;
                return;
            }
            row[ia] += (ia + 1 - xa) * weight;
            for (int x = ia + 1; x < ib; x++)
                row[x] += weight;
            if (ib <= maxX)
                row[ib] += (xb - ib) * weight;
        }

        public void FillRect(float centerX, float centerY, float width, float height, RgbaColor color)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            FillPolygon(new[]
            {
                new Vec2(centerX - hw, centerY - hh), new Vec2(centerX + hw, centerY - hh),
                new Vec2(centerX + hw, centerY + hh), new Vec2(centerX - hw, centerY + hh),
            }, color);
        }

        public void FillEllipse(float centerX, float centerY, float rx, float ry, RgbaColor color)
        {
            FillPolygon(Shapes.Ellipse(centerX, centerY, rx, ry, 96), color);
        }

        /// <summary>Round-ended line (bolt slots, divider lines are drawn as rectangles instead).</summary>
        public void StrokeLine(Vec2 a, Vec2 b, float width, RgbaColor color)
        {
            var stroke = new Stroke();
            stroke.Points.Add(a);
            stroke.Points.Add(b);
            DrawStrokes(new[] { stroke }, width, float.MinValue, float.MaxValue, color, roundEnds: true);
        }

        // ------------------------------------------------------------------------------------------
        // Text
        // ------------------------------------------------------------------------------------------

        public enum Align
        {
            Left,
            Center,
            Right,
        }

        /// <summary>
        /// Draws one line of text with its baseline at <paramref name="baseline"/>. When it is wider than
        /// <paramref name="maxWidth"/>, narrower series are tried first (down to <paramref name="narrowest"/>),
        /// then the letters are squeezed. Returns the drawn width.
        /// </summary>
        public float DrawText(string text, FontSeries series, float capHeight, float x, float baseline, Align align, float maxWidth, RgbaColor color, FontSeries narrowest = null)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            FontSeries used = FitSeries(text, series, capHeight, maxWidth, narrowest, out float squeeze);
            float width = StrokeFont.Measure(text, used, capHeight) * squeeze;
            float left = align == Align.Left ? x : (align == Align.Right ? x - width : x - width * 0.5f);
            List<PlacedGlyph> glyphs = StrokeFont.LayoutLine(text, used, capHeight, left, baseline, squeeze, out _);
            float strokeWidth = used.Weight * capHeight;
            foreach (PlacedGlyph glyph in glyphs)
                DrawStrokes(glyph.Strokes, strokeWidth, glyph.ClipBottom, glyph.ClipTop, color, roundEnds: false);
            return width;
        }

        /// <summary>Widest series from <paramref name="series"/> down to <paramref name="narrowest"/> that fits, plus the squeeze still needed.</summary>
        public static FontSeries FitSeries(string text, FontSeries series, float capHeight, float maxWidth, FontSeries narrowest, out float squeeze)
        {
            squeeze = 1f;
            if (maxWidth <= 0f)
                return series;
            FontSeries candidate = series;
            bool started = false;
            foreach (FontSeries s in FontSeries.Narrowing)
            {
                if (s == series)
                    started = true;
                if (!started)
                    continue;
                candidate = s;
                if (StrokeFont.Measure(text, s, capHeight) <= maxWidth)
                    return s;
                if (narrowest == null || s == narrowest)
                    break;
            }
            float width = StrokeFont.Measure(text, candidate, capHeight);
            if (width > maxWidth)
                squeeze = Math.Max(0.4f, maxWidth / width);
            return candidate;
        }

        /// <summary>
        /// Draws strokes of uniform width as one shape: segment coverages are combined with max, so overlaps
        /// do not darken. Joints are round; open ends are square unless <paramref name="roundEnds"/>.
        /// Ink is clipped to the band [clipBottom, clipTop] (inches).
        /// </summary>
        public void DrawStrokes(IReadOnlyList<Stroke> strokes, float width, float clipBottom, float clipTop, RgbaColor color, bool roundEnds)
        {
            float half = width * 0.5f * PixelsPerInch;
            float cTop = clipTop >= float.MaxValue * 0.5f ? float.MinValue : PxY(clipTop);
            float cBottom = clipBottom <= float.MinValue * 0.5f ? float.MaxValue : PxY(clipBottom);
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue;

            foreach (Stroke stroke in strokes)
            {
                int n = stroke.Points.Count;
                if (n < 2)
                    continue;
                int segments = stroke.Closed ? n : n - 1;
                for (int i = 0; i < segments; i++)
                {
                    Vec2 a = new Vec2(PxX(stroke.Points[i].X), PxY(stroke.Points[i].Y));
                    Vec2 b = new Vec2(PxX(stroke.Points[(i + 1) % n].X), PxY(stroke.Points[(i + 1) % n].Y));
                    bool jointA = stroke.Closed || i > 0;
                    bool jointB = stroke.Closed || i < segments - 1;
                    float extA = 0f, extB = 0f;
                    bool roundA = roundEnds && !jointA, roundB = roundEnds && !jointB;
                    if (jointA)
                    {
                        Vec2 prev = stroke.Points[(i - 1 + n) % n];
                        Joint(new Vec2(PxX(prev.X), PxY(prev.Y)), a, b, half, ref extA, ref roundA);
                    }
                    if (jointB)
                    {
                        Vec2 next = stroke.Points[(i + 2) % n];
                        Joint(a, b, new Vec2(PxX(next.X), PxY(next.Y)), half, ref extB, ref roundB);
                    }
                    RasterSegment(a, b, half, extA, extB, roundA, roundB, cTop, cBottom, ref bx0, ref by0, ref bx1, ref by1);
                }
            }
            if (bx0 <= bx1 && by0 <= by1)
                Blend(color, bx0, by0, bx1, by1);
        }

        /// <summary>
        /// Corner between segments p0-p1 and p1-p2: mitered (both segments extended so their outer edges meet,
        /// which keeps letter corners square and flattened arcs smooth), or round when the stroke nearly doubles back.
        /// </summary>
        private static void Joint(Vec2 p0, Vec2 p1, Vec2 p2, float half, ref float extension, ref bool round)
        {
            Vec2 d0 = p1 - p0, d1 = p2 - p1;
            float l0 = d0.Length, l1 = d1.Length;
            if (l0 < 1e-6f || l1 < 1e-6f)
            {
                round = true;
                return;
            }
            float cos = MathUtil.Clamp(Vec2.Dot(d0 / l0, d1 / l1), -1f, 1f);
            float turn = (float)Math.Acos(cos);
            if (turn > 2.1f)
            {
                round = true;
                return;
            }
            // At least a pixel of overlap so neighboring segments never leave a seam.
            extension = Math.Max(1f, half * (float)Math.Tan(turn * 0.5f));
        }

        private void RasterSegment(Vec2 a, Vec2 b, float half, float extA, float extB, bool roundA, bool roundB, float clipTop, float clipBottom,
            ref int bx0, ref int by0, ref int bx1, ref int by1)
        {
            Vec2 d = b - a;
            float len = d.Length;
            Vec2 dir = len > 1e-6f ? d / len : new Vec2(1f, 0f);
            a = a - dir * extA;
            b = b + dir * extB;
            len += extA + extB;
            float pad = half + 1f;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - pad));
            int x1 = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + pad));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Max(Math.Min(a.Y, b.Y) - pad, clipTop - 1f)));
            int y1 = Math.Min(Height - 1, (int)Math.Ceiling(Math.Min(Math.Max(a.Y, b.Y) + pad, clipBottom + 1f)));
            if (x0 > x1 || y0 > y1)
                return;
            bx0 = Math.Min(bx0, x0);
            by0 = Math.Min(by0, y0);
            bx1 = Math.Max(bx1, x1);
            by1 = Math.Max(by1, y1);

            for (int py = y0; py <= y1; py++)
            {
                // Vertical clip: fraction of this pixel row inside the band.
                float rowCover = Clamp01(Math.Min(py + 1f, clipBottom) - Math.Max(py, clipTop));
                if (rowCover <= 0f)
                    continue;
                float cy = py + 0.5f;
                int rowStart = py * Width;
                for (int px = x0; px <= x1; px++)
                {
                    float cx = px + 0.5f;
                    float rx = cx - a.X, ry = cy - a.Y;
                    float along = rx * dir.X + ry * dir.Y;
                    float cover;
                    if (along < 0f && roundA)
                    {
                        cover = Clamp01(half - (float)Math.Sqrt(rx * rx + ry * ry) + 0.5f);
                    }
                    else if (along > len && roundB)
                    {
                        float qx = cx - b.X, qy = cy - b.Y;
                        cover = Clamp01(half - (float)Math.Sqrt(qx * qx + qy * qy) + 0.5f);
                    }
                    else
                    {
                        float perp = Math.Abs(rx * dir.Y - ry * dir.X);
                        cover = Clamp01(half - perp + 0.5f);
                        if (!roundA)
                            cover = Math.Min(cover, Clamp01(along + 0.5f));
                        if (!roundB)
                            cover = Math.Min(cover, Clamp01(len - along + 0.5f));
                    }
                    cover *= rowCover;
                    int index = rowStart + px;
                    if (cover > m_Coverage[index])
                        m_Coverage[index] = cover;
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Compositing
        // ------------------------------------------------------------------------------------------

        /// <summary>Blends the coverage buffer inside the box with <paramref name="color"/> and clears it.</summary>
        private void Blend(RgbaColor color, int x0, int y0, int x1, int y1)
        {
            Linear(color, out float r, out float g, out float b);
            float alpha = color.A / 255f;
            for (int py = y0; py <= y1; py++)
            {
                int rowStart = py * Width;
                for (int px = x0; px <= x1; px++)
                {
                    int i = rowStart + px;
                    float c = m_Coverage[i] * alpha;
                    if (c <= 0f)
                        continue;
                    m_Coverage[i] = 0f;
                    m_R[i] += (r - m_R[i]) * c;
                    m_G[i] += (g - m_G[i]) * c;
                    m_B[i] += (b - m_B[i]) * c;
                }
            }
        }

        /// <summary>The finished face as 8-bit sRGB, bottom row first.</summary>
        public RgbaImage ToImage()
        {
            var image = new RgbaImage(Width, Height);
            byte[] pixels = image.Pixels;
            for (int py = 0; py < Height; py++)
            {
                int src = py * Width;
                int dst = (Height - 1 - py) * Width * 4;
                for (int px = 0; px < Width; px++)
                {
                    pixels[dst++] = ToSrgb(m_R[src + px]);
                    pixels[dst++] = ToSrgb(m_G[src + px]);
                    pixels[dst++] = ToSrgb(m_B[src + px]);
                    pixels[dst++] = 255;
                }
            }
            return image;
        }

        // Blending happens in linear light so anti-aliased edges keep their weight.
        private static readonly float[] s_ToLinear = BuildToLinear();

        private static float[] BuildToLinear()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                float c = i / 255f;
                table[i] = c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);
            }
            return table;
        }

        private static void Linear(RgbaColor c, out float r, out float g, out float b)
        {
            r = s_ToLinear[c.R];
            g = s_ToLinear[c.G];
            b = s_ToLinear[c.B];
        }

        private static byte ToSrgb(float linear)
        {
            float c = linear <= 0.0031308f ? linear * 12.92f : 1.055f * (float)Math.Pow(Math.Max(0f, linear), 1f / 2.4f) - 0.055f;
            int v = (int)(c * 255f + 0.5f);
            return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
