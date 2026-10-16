using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using USRoadSigns.Catalog;

namespace SignForge.Rendering
{
    /// <summary>
    /// Draws sign legends. The FHWA "Highway Gothic" alphabets (Series B..F) are approximated with the
    /// Bahnschrift variable font that ships with Windows (a DIN 1451 design with very similar construction):
    ///   B = Condensed (compressed), C = Condensed, D = SemiCondensed, E = Normal, EM = SemiBold, F = Normal (expanded).
    /// Sizes are given as capital letter height in inches, like the MUTCD.
    /// </summary>
    public sealed class TextRenderer
    {
        private sealed class SeriesStyle
        {
            public GlyphTypeface Glyphs;
            public double XScale;
            public double DefaultTracking;
        }

        private readonly Dictionary<string, SeriesStyle> m_Styles = new Dictionary<string, SeriesStyle>(StringComparer.OrdinalIgnoreCase);

        public TextRenderer()
        {
            m_Styles["B"] = Create(FontStretches.Condensed, FontWeights.Normal, 0.86, 0.02);
            m_Styles["C"] = Create(FontStretches.Condensed, FontWeights.Normal, 1.0, 0.02);
            m_Styles["D"] = Create(FontStretches.SemiCondensed, FontWeights.Normal, 1.0, 0.03);
            m_Styles["E"] = Create(FontStretches.Normal, FontWeights.Normal, 1.0, 0.03);
            m_Styles["EM"] = Create(FontStretches.Normal, FontWeights.SemiBold, 1.0, 0.03);
            m_Styles["F"] = Create(FontStretches.Normal, FontWeights.Normal, 1.12, 0.04);
            m_Styles["DB"] = Create(FontStretches.SemiCondensed, FontWeights.SemiBold, 1.0, 0.03);
        }

        private static SeriesStyle Create(FontStretch stretch, FontWeight weight, double xScale, double tracking)
        {
            var typeface = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, weight, stretch);
            if (!typeface.TryGetGlyphTypeface(out GlyphTypeface glyphs))
            {
                typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, weight, FontStretches.Normal);
                if (!typeface.TryGetGlyphTypeface(out glyphs))
                    throw new InvalidOperationException("No usable font found (Bahnschrift or Arial required).");
                xScale *= stretch == FontStretches.Condensed ? 0.8 : (stretch == FontStretches.SemiCondensed ? 0.9 : 1.0);
            }
            return new SeriesStyle { Glyphs = glyphs, XScale = xScale, DefaultTracking = tracking };
        }

        private SeriesStyle Style(string series)
        {
            if (series != null && m_Styles.TryGetValue(series, out SeriesStyle style))
                return style;
            return m_Styles["D"];
        }

        /// <summary>Width in inches of a single line.</summary>
        public double Measure(string text, string series, double capHeight, double tracking)
        {
            BuildLine(text, series, capHeight, tracking, out double width);
            return width;
        }

        /// <summary>Builds the outline of one line with its baseline at y = 0 and left edge at x = 0 (inches).</summary>
        public Geometry BuildLine(string text, string series, double capHeight, double extraTracking, out double width)
        {
            SeriesStyle style = Style(series);
            GlyphTypeface gt = style.Glyphs;
            double emSize = capHeight / gt.CapsHeight;
            double tracking = (style.DefaultTracking + extraTracking) * capHeight;

            var indices = new List<ushort>(text.Length);
            var advances = new List<double>(text.Length);
            width = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!gt.CharacterToGlyphMap.TryGetValue(c, out ushort glyph))
                    gt.CharacterToGlyphMap.TryGetValue('?', out glyph);
                double advance = gt.AdvanceWidths[glyph] * emSize * style.XScale;
                if (i < text.Length - 1)
                    advance += tracking;
                indices.Add(glyph);
                advances.Add(advance / style.XScale);
                width += advance;
            }

            if (indices.Count == 0)
                return Geometry.Empty;

            var run = new GlyphRun(gt, 0, false, emSize, 1.0f, indices, new Point(0, 0), advances, null, null, null, null, null, null);
            Geometry geometry = run.BuildGeometry();
            if (Math.Abs(style.XScale - 1.0) > 1e-3)
            {
                geometry = geometry.Clone();
                geometry.Transform = new ScaleTransform(style.XScale, 1.0);
            }
            return geometry;
        }

        /// <summary>Draws a text element (single or multi-line) centered on (X, Y) of the cap height.</summary>
        public void Draw(DrawingContext dc, ElementDef element, Brush brush)
        {
            IList<string> lines = element.Lines != null && element.Lines.Count > 0 ? (IList<string>)element.Lines : new[] { element.Text ?? "" };
            double cap = element.Size > 0 ? element.Size : 4.0;
            double spacing = element.LineSpacing > 0 ? element.LineSpacing : cap * 1.55;
            double firstBaselineCenter = element.Y - spacing * (lines.Count - 1) * 0.5;

            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;
                Geometry geometry = BuildLine(line, element.Font, cap, element.Tracking, out double width);
                double scaleX = 1.0;
                if (element.MaxWidth > 0 && width > element.MaxWidth)
                {
                    scaleX = element.MaxWidth / width;
                    width = element.MaxWidth;
                }

                double left;
                switch ((element.Align ?? "center").ToLowerInvariant())
                {
                    case "left":
                        left = element.X;
                        break;
                    case "right":
                        left = element.X - width;
                        break;
                    default:
                        left = element.X - width * 0.5;
                        break;
                }
                double baseline = firstBaselineCenter + spacing * i + cap * 0.5;

                var transform = new TransformGroup();
                if (geometry.Transform != null && !geometry.Transform.Value.IsIdentity)
                    transform.Children.Add(geometry.Transform);
                transform.Children.Add(new ScaleTransform(scaleX, 1.0));
                transform.Children.Add(new TranslateTransform(left, baseline));
                Geometry placed = geometry.Clone();
                placed.Transform = transform;
                group.Children.Add(placed);
            }

            PushElementTransform(dc, element);
            dc.DrawGeometry(brush, null, group);
            dc.Pop();
        }

        internal static void PushElementTransform(DrawingContext dc, ElementDef element)
        {
            var t = new TransformGroup();
            if (element.MirrorX)
                t.Children.Add(new ScaleTransform(-1, 1, element.X, element.Y));
            if (Math.Abs(element.Rotation) > 1e-4f)
                t.Children.Add(new RotateTransform(element.Rotation, element.X, element.Y));
            dc.PushTransform(t);
        }
    }
}
