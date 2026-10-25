using System;
using System.Collections.Generic;
using System.Globalization;
using USRoadSigns.Geometry;

namespace USRoadSigns.Route
{
    /// <summary>One FHWA alphabet ("Highway Gothic" series): stroke weight and width relative to the capital height.</summary>
    public sealed class FontSeries
    {
        public readonly string Name;
        /// <summary>Stroke width / capital height.</summary>
        public readonly float Weight;
        /// <summary>Horizontal scale of the letter skeletons (Series E = 1).</summary>
        public readonly float XScale;
        /// <summary>Space between letters / capital height.</summary>
        public readonly float Spacing;

        private FontSeries(string name, float weight, float xScale, float spacing)
        {
            Name = name;
            Weight = weight;
            XScale = xScale;
            Spacing = spacing;
        }

        public static readonly FontSeries B = new FontSeries("B", 0.125f, 0.56f, 0.13f);
        public static readonly FontSeries C = new FontSeries("C", 0.14f, 0.70f, 0.15f);
        public static readonly FontSeries D = new FontSeries("D", 0.155f, 0.84f, 0.17f);
        public static readonly FontSeries E = new FontSeries("E", 0.165f, 1.0f, 0.19f);
        /// <summary>Series E(Modified): Series E with a heavier stroke, used for guide sign legends.</summary>
        public static readonly FontSeries EM = new FontSeries("EM", 0.2f, 1.0f, 0.18f);
        public static readonly FontSeries F = new FontSeries("F", 0.175f, 1.16f, 0.21f);

        /// <summary>From widest to narrowest, for picking the widest series that fits.</summary>
        public static readonly FontSeries[] Narrowing = { F, E, D, C, B };
    }

    /// <summary>A stroke centerline in text space (inches, y up). Open strokes get square-cut ends.</summary>
    public sealed class Stroke
    {
        public readonly List<Vec2> Points = new List<Vec2>();
        public bool Closed;
    }

    /// <summary>Strokes of one laid-out glyph plus the band its ink is clipped to (square cuts at the base and cap lines).</summary>
    public sealed class PlacedGlyph
    {
        public readonly List<Stroke> Strokes = new List<Stroke>();
        public float ClipBottom;
        public float ClipTop;
    }

    /// <summary>
    /// Engine-independent stroke font in the style of the FHWA Standard Alphabets (Highway Gothic): every letter
    /// is a skeleton of lines and arcs drawn with one uniform stroke, which is how the alphabets are built.
    /// Skeleton units: the stroke centerline runs from y = 0 (baseline) to y = 1 (capital height), lowercase
    /// x-height at 0.70 and descenders down to -0.30; x uses the same units at Series E proportions.
    /// Strokes that run past the base or cap line are cut off square there.
    /// </summary>
    public static class StrokeFont
    {
        private const float XHeight = 0.70f;
        private const float Descender = -0.30f;
        private const float ArcStepDegrees = 7.5f;

        private enum Clip
        {
            /// <summary>Baseline to capital height.</summary>
            Cap,
            /// <summary>Baseline to x-height (lowercase letters with diagonal tops).</summary>
            XHeight,
            /// <summary>Descender to capital height.</summary>
            Descender,
            /// <summary>Descender to x-height.</summary>
            DescenderX,
        }

        private sealed class Glyph
        {
            public float Width;
            public Clip Clip;
            public List<Stroke> Strokes;
        }

        private static readonly Dictionary<char, Glyph> s_Glyphs = new Dictionary<char, Glyph>();

        // Skeleton paths. M x y = move, L x y = line, A cx cy r a0 a1 = circular arc (degrees, counter-clockwise
        // positive, may run either way), E cx cy rx ry a0 a1 = elliptical arc, Z = close. An arc continues the
        // current stroke (with a joining line if needed) or starts one.
        private static readonly (char Char, float Width, Clip Clip, string Path)[] s_Data =
        {
            ('0', 0.58f, Clip.Cap, "M 0 .29 L 0 .71 A .29 .71 .29 180 0 L .58 .29 A .29 .29 .29 0 -180 Z"),
            ('1', 0.30f, Clip.Cap, "M .3 0 L .3 1 L .02 .8"),
            ('2', 0.58f, Clip.Cap, "A .29 .71 .29 163 -30 L 0 0 L .59 0"),
            ('3', 0.57f, Clip.Cap, "A .29 .775 .225 158 -90 L .15 .55 M .29 .55 A .29 .275 .275 90 -158"),
            ('4', 0.62f, Clip.Cap, "M .46 -.1 L .46 1 L 0 .3 L .63 .3"),
            ('5', 0.59f, Clip.Cap, "M .54 1 L .07 1 L .04 .57 A .3 .3 .3 126 -150"),
            ('6', 0.58f, Clip.Cap, "A .29 .71 .29 45 180 L 0 .29 A .29 .29 .29 180 540"),
            ('7', 0.58f, Clip.Cap, "M 0 1 L .58 1 L .15 -.15"),
            ('8', 0.57f, Clip.Cap, "A .285 .775 .235 -90 270 M .285 .54 A .285 .27 .27 90 450"),
            ('9', 0.58f, Clip.Cap, "A .29 .71 .29 0 360 M .58 .71 L .58 .29 A .29 .29 .29 0 -135"),

            ('A', 0.76f, Clip.Cap, "M -.04 -.1 L .38 1 L .80 -.1 M .13 .33 L .63 .33"),
            ('B', 0.60f, Clip.Cap, "M 0 0 L 0 1 L .32 1 A .32 .755 .245 90 -90 L 0 .51 L .34 .51 A .34 .255 .255 90 -90 L 0 0"),
            ('C', 0.62f, Clip.Cap, "A .31 .69 .31 22 180 L 0 .31 A .31 .31 .31 180 338"),
            ('D', 0.61f, Clip.Cap, "M 0 0 L 0 1 L .27 1 A .27 .66 .34 90 0 L .61 .34 A .27 .34 .34 0 -90 L 0 0 Z"),
            ('E', 0.50f, Clip.Cap, "M .51 1 L 0 1 L 0 0 L .53 0 M 0 .52 L .44 .52"),
            ('F', 0.49f, Clip.Cap, "M .5 1 L 0 1 L 0 -.1 M 0 .5 L .43 .5"),
            ('G', 0.62f, Clip.Cap, "A .31 .69 .31 18 180 L 0 .31 A .31 .31 .31 180 360 L .62 .47 L .35 .47"),
            ('H', 0.60f, Clip.Cap, "M 0 -.1 L 0 1.1 M .6 -.1 L .6 1.1 M 0 .52 L .6 .52"),
            ('I', 0.00f, Clip.Cap, "M 0 -.1 L 0 1.1"),
            ('J', 0.50f, Clip.Cap, "M .5 1.1 L .5 .28 A .25 .28 .25 0 -180 L 0 .36"),
            ('K', 0.60f, Clip.Cap, "M 0 -.1 L 0 1.1 M .66 1.1 L 0 .3 M .2 .54 L .66 -.12"),
            ('L', 0.48f, Clip.Cap, "M 0 1.1 L 0 0 L .49 0"),
            ('M', 0.78f, Clip.Cap, "M 0 -.1 L 0 1 L .39 .18 L .78 1 L .78 -.1"),
            ('N', 0.60f, Clip.Cap, "M 0 -.1 L 0 1 L .6 0 L .6 1.1"),
            ('O', 0.66f, Clip.Cap, "M 0 .33 L 0 .67 A .33 .67 .33 180 0 L .66 .33 A .33 .33 .33 0 -180 Z"),
            ('P', 0.58f, Clip.Cap, "M 0 -.1 L 0 1 L .31 1 A .31 .735 .265 90 -90 L 0 .47"),
            ('Q', 0.66f, Clip.Cap, "M 0 .33 L 0 .67 A .33 .67 .33 180 0 L .66 .33 A .33 .33 .33 0 -180 Z M .38 .2 L .72 -.12"),
            ('R', 0.60f, Clip.Cap, "M 0 -.1 L 0 1 L .31 1 A .31 .74 .26 90 -90 L 0 .48 M .26 .48 L .62 -.12"),
            ('S', 0.58f, Clip.Cap, "A .29 .745 .255 22 270 A .29 .255 .255 90 -158"),
            ('T', 0.60f, Clip.Cap, "M 0 1 L .6 1 M .3 1 L .3 -.1"),
            ('U', 0.60f, Clip.Cap, "M 0 1.1 L 0 .3 A .3 .3 .3 180 360 L .6 1.1"),
            ('V', 0.74f, Clip.Cap, "M -.04 1.12 L .37 0 L .78 1.12"),
            ('W', 1.00f, Clip.Cap, "M -.03 1.12 L .22 0 L .5 1 L .78 0 L 1.03 1.12"),
            ('X', 0.70f, Clip.Cap, "M -.03 1.1 L .73 -.1 M .73 1.1 L -.03 -.1"),
            ('Y', 0.74f, Clip.Cap, "M -.04 1.12 L .37 .45 L .78 1.12 M .37 .45 L .37 -.1"),
            ('Z', 0.60f, Clip.Cap, "M .02 1 L .6 1 L 0 0 L .61 0"),

            ('a', 0.56f, Clip.Cap, "A .28 .46 .24 150 0 L .52 0 M .52 .38 L .25 .38 E .25 .19 .25 .19 90 270 L .31 0 A .31 .2 .2 -90 15"),
            ('b', 0.60f, Clip.Cap, "M 0 1.1 L 0 0 M 0 .35 E .3 .35 .3 .35 180 540"),
            ('c', 0.56f, Clip.Cap, "E .29 .35 .29 .35 30 330"),
            ('d', 0.60f, Clip.Cap, "M .6 1.1 L .6 0 M .6 .35 E .3 .35 .3 .35 0 360"),
            ('e', 0.59f, Clip.Cap, "M 0 .37 L .59 .37 E .295 .35 .295 .35 3 328"),
            ('f', 0.42f, Clip.Cap, "M .14 -.1 L .14 .77 A .36 .77 .22 180 45 M 0 .7 L .38 .7"),
            ('g', 0.60f, Clip.Descender, "E .3 .38 .3 .32 0 360 M .6 .7 L .6 -.05 E .3 -.05 .3 .25 0 -155"),
            ('h', 0.58f, Clip.Cap, "M 0 1.1 L 0 0 M 0 .42 A .29 .41 .29 180 0 L .58 -.1"),
            ('i', 0.00f, Clip.Cap, "M 0 -.1 L 0 .7 M 0 .84 L 0 1.1"),
            ('j', 0.22f, Clip.Descender, "M .22 .7 L .22 -.08 A .02 -.08 .2 0 -125 M .22 .84 L .22 1.1"),
            ('k', 0.56f, Clip.Cap, "M 0 -.1 L 0 1.1 M .57 .74 L 0 .2 M .18 .37 L .6 -.1"),
            ('l', 0.00f, Clip.Cap, "M 0 -.1 L 0 1.1"),
            ('m', 0.94f, Clip.Cap, "M 0 -.1 L 0 .7 M 0 .45 A .235 .46 .235 180 0 L .47 -.1 M .47 .45 A .705 .46 .235 180 0 L .94 -.1"),
            ('n', 0.58f, Clip.Cap, "M 0 -.1 L 0 .7 M 0 .42 A .29 .41 .29 180 0 L .58 -.1"),
            ('o', 0.62f, Clip.Cap, "E .31 .35 .31 .35 0 360"),
            ('p', 0.60f, Clip.Descender, "M 0 .7 L 0 -.4 M 0 .35 E .3 .35 .3 .35 180 540"),
            ('q', 0.60f, Clip.Descender, "M .6 .7 L .6 -.4 M .6 .35 E .3 .35 .3 .35 0 360"),
            ('r', 0.33f, Clip.Cap, "M 0 -.1 L 0 .7 M 0 .4 A .28 .4 .28 180 62"),
            ('s', 0.50f, Clip.Cap, "A .25 .525 .175 22 270 A .25 .175 .175 90 -158"),
            ('t', 0.42f, Clip.Cap, "M .14 .96 L .14 .15 A .32 .15 .18 180 300 M 0 .7 L .4 .7"),
            ('u', 0.58f, Clip.Cap, "M 0 .8 L 0 .3 A .29 .3 .29 180 360 M .58 .8 L .58 -.1"),
            ('v', 0.64f, Clip.XHeight, "M -.04 .82 L .32 0 L .68 .82"),
            ('w', 0.92f, Clip.XHeight, "M -.03 .82 L .21 0 L .46 .7 L .71 0 L .95 .82"),
            ('x', 0.60f, Clip.XHeight, "M -.03 .8 L .63 -.1 M .63 .8 L -.03 -.1"),
            ('y', 0.64f, Clip.DescenderX, "M -.04 .82 L .33 0 M .68 .82 L .13 -.42"),
            ('z', 0.56f, Clip.XHeight, "M .02 .7 L .56 .7 L 0 0 L .57 0"),

            ('-', 0.30f, Clip.Cap, "M 0 .45 L .3 .45"),
            ('.', 0.00f, Clip.Cap, "M 0 -.1 L 0 .14"),
            (',', 0.06f, Clip.Descender, "M .06 .14 L .06 -.05 L -.04 -.22"),
            ('\'', 0.00f, Clip.Cap, "M 0 1.1 L 0 .72"),
            ('/', 0.46f, Clip.Cap, "M 0 -.1 L .46 1.1"),
            ('&', 0.70f, Clip.Cap, "M .72 -.1 L .12 .6 A .3 .8 .2 200 -20 L .08 .26 A .3 .26 .26 160 330 L .7 .42"),
        };

        static StrokeFont()
        {
            foreach ((char c, float width, Clip clip, string path) in s_Data)
                s_Glyphs[c] = new Glyph { Width = width, Clip = clip, Strokes = ParsePath(path) };
        }

        public static bool Supports(char c) => c == ' ' || s_Glyphs.ContainsKey(c);

        /// <summary>Width of a line in inches.</summary>
        public static float Measure(string text, FontSeries series, float capHeight)
        {
            float width = 0f;
            Layout(text, series, capHeight, 0f, 0f, 1f, null, ref width);
            return width;
        }

        /// <summary>
        /// Lays out one line with its left end at <paramref name="left"/> and baseline at <paramref name="baseline"/>.
        /// <paramref name="squeeze"/> narrows the letter skeletons and spacing (not the strokes) to fit a width.
        /// </summary>
        public static List<PlacedGlyph> LayoutLine(string text, FontSeries series, float capHeight, float left, float baseline, float squeeze, out float width)
        {
            var glyphs = new List<PlacedGlyph>();
            width = 0f;
            Layout(text, series, capHeight, left, baseline, squeeze, glyphs, ref width);
            return glyphs;
        }

        private static void Layout(string text, FontSeries series, float capHeight, float left, float baseline, float squeeze, List<PlacedGlyph> output, ref float width)
        {
            if (string.IsNullOrEmpty(text))
            {
                width = 0f;
                return;
            }
            float h = capHeight;
            float w = series.Weight * h;
            float unit = h - w;                         // skeleton unit in inches
            float sx = series.XScale * squeeze;
            float gap = series.Spacing * h * (0.45f + 0.55f * series.XScale) * squeeze;
            float pen = left;
            bool first = true;
            foreach (char raw in text)
            {
                if (raw == ' ')
                {
                    pen += 0.36f * h * series.XScale * squeeze + (first ? 0f : gap);
                    first = false;
                    continue;
                }
                if (!s_Glyphs.TryGetValue(raw, out Glyph glyph) && !s_Glyphs.TryGetValue(char.ToUpperInvariant(raw), out glyph))
                    continue;
                if (!first)
                    pen += gap;
                first = false;

                if (output != null)
                {
                    var placed = new PlacedGlyph();
                    float ox = pen + w * 0.5f;
                    float oy = baseline + w * 0.5f;
                    foreach (Stroke stroke in glyph.Strokes)
                    {
                        var s = new Stroke { Closed = stroke.Closed };
                        foreach (Vec2 p in stroke.Points)
                            s.Points.Add(new Vec2(ox + p.X * unit * sx, oy + p.Y * unit));
                        if (!s.Closed)
                        {
                            ExtendToGuide(s, stroke, 0, 1, w);
                            ExtendToGuide(s, stroke, stroke.Points.Count - 1, stroke.Points.Count - 2, w);
                        }
                        placed.Strokes.Add(s);
                    }
                    bool descends = glyph.Clip == Clip.Descender || glyph.Clip == Clip.DescenderX;
                    bool xTop = glyph.Clip == Clip.XHeight || glyph.Clip == Clip.DescenderX;
                    placed.ClipBottom = descends ? oy + Descender * unit - w * 0.5f : baseline;
                    placed.ClipTop = xTop ? oy + XHeight * unit + w * 0.5f : baseline + h;
                    output.Add(placed);
                }
                pen += glyph.Width * unit * sx + w;
            }
            width = pen - left;
        }

        /// <summary>
        /// Square ends sit on the skeleton, half a stroke short of the guide lines. A stroke ending on the base,
        /// x-height, cap or descender line is lengthened to reach the line's outer edge.
        /// </summary>
        private static void ExtendToGuide(Stroke placed, Stroke skeleton, int end, int neighbor, float strokeWidth)
        {
            if (skeleton.Points.Count < 2)
                return;
            float y = skeleton.Points[end].Y;
            bool onGuide = Math.Abs(y) < 0.01f || Math.Abs(y - 1f) < 0.01f || Math.Abs(y - XHeight) < 0.01f || Math.Abs(y - Descender) < 0.01f;
            if (!onGuide)
                return;
            Vec2 dir = placed.Points[end] - placed.Points[neighbor];
            float len = dir.Length;
            if (len < 1e-6f)
                return;
            dir = dir / len;
            if (Math.Abs(dir.Y) < 0.35f)
                return;
            float extension = Math.Min(strokeWidth, strokeWidth * 0.5f / Math.Abs(dir.Y));
            placed.Points[end] = placed.Points[end] + dir * extension;
        }

        // ------------------------------------------------------------------------------------------
        // Path parsing
        // ------------------------------------------------------------------------------------------

        private static List<Stroke> ParsePath(string path)
        {
            var strokes = new List<Stroke>();
            string[] tokens = path.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Stroke current = null;
            int i = 0;
            float F() => float.Parse(tokens[i++], NumberStyles.Float, CultureInfo.InvariantCulture);
            while (i < tokens.Length)
            {
                string cmd = tokens[i++];
                switch (cmd)
                {
                    case "M":
                        current = new Stroke();
                        strokes.Add(current);
                        current.Points.Add(new Vec2(F(), F()));
                        break;
                    case "L":
                        current.Points.Add(new Vec2(F(), F()));
                        break;
                    case "A":
                    {
                        float cx = F(), cy = F(), r = F(), a0 = F(), a1 = F();
                        current = AddArc(strokes, current, cx, cy, r, r, a0, a1);
                        break;
                    }
                    case "E":
                    {
                        float cx = F(), cy = F(), rx = F(), ry = F(), a0 = F(), a1 = F();
                        current = AddArc(strokes, current, cx, cy, rx, ry, a0, a1);
                        break;
                    }
                    case "Z":
                        current.Closed = true;
                        if (current.Points.Count > 1 && (current.Points[current.Points.Count - 1] - current.Points[0]).Length < 1e-4f)
                            current.Points.RemoveAt(current.Points.Count - 1);
                        current = null;
                        break;
                    default:
                        throw new FormatException("Bad glyph path command " + cmd);
                }
            }

            // A full circle drawn as one arc closes on itself.
            foreach (Stroke s in strokes)
            {
                if (!s.Closed && s.Points.Count > 2 && (s.Points[s.Points.Count - 1] - s.Points[0]).Length < 1e-4f)
                {
                    s.Points.RemoveAt(s.Points.Count - 1);
                    s.Closed = true;
                }
            }
            return strokes;
        }

        private static Stroke AddArc(List<Stroke> strokes, Stroke current, float cx, float cy, float rx, float ry, float a0, float a1)
        {
            if (current == null)
            {
                current = new Stroke();
                strokes.Add(current);
            }
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(a1 - a0) / ArcStepDegrees));
            for (int k = 0; k <= steps; k++)
            {
                float a = (a0 + (a1 - a0) * k / steps) * MathUtil.Pi / 180f;
                var p = new Vec2(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a));
                if (k == 0 && current.Points.Count > 0 && (current.Points[current.Points.Count - 1] - p).Length < 1e-4f)
                    continue;
                current.Points.Add(p);
            }
            return current;
        }
    }
}
