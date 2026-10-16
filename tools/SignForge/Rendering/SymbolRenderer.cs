using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using USRoadSigns.Catalog;

namespace SignForge.Rendering
{
    /// <summary>Draws vector pictograms defined in data/symbols/*.json.</summary>
    public sealed class SymbolRenderer
    {
        private readonly SignCatalog m_Catalog;
        private readonly Dictionary<string, Geometry> m_GeometryCache = new Dictionary<string, Geometry>(StringComparer.Ordinal);

        public SymbolRenderer(SignCatalog catalog)
        {
            m_Catalog = catalog;
        }

        public void Draw(DrawingContext dc, ElementDef element, RgbaColor foreground, RgbaColor background)
        {
            if (!m_Catalog.Symbols.TryGetValue(element.Symbol ?? "", out SymbolDef symbol))
                throw new InvalidOperationException("Unknown symbol '" + element.Symbol + "'");

            float[] vb = symbol.ViewBox != null && symbol.ViewBox.Length == 4 ? symbol.ViewBox : new float[] { 0, 0, 100, 100 };
            double boxW = element.Width > 0 ? element.Width : element.Height * vb[2] / vb[3];
            double boxH = element.Height > 0 ? element.Height : element.Width * vb[3] / vb[2];
            double scale = Math.Min(boxW / vb[2], boxH / vb[3]);

            TextRenderer.PushElementTransform(dc, element);
            var t = new TransformGroup();
            t.Children.Add(new TranslateTransform(-(vb[0] + vb[2] * 0.5), -(vb[1] + vb[3] * 0.5)));
            t.Children.Add(new ScaleTransform(scale, scale));
            t.Children.Add(new TranslateTransform(element.X, element.Y));
            dc.PushTransform(t);

            foreach (SymbolPart part in symbol.Parts)
            {
                Geometry geometry = GetGeometry(part.Path);
                Brush fill = ResolveBrush(part.Fill, foreground, background);
                Pen pen = null;
                if (!string.IsNullOrEmpty(part.Stroke) && part.Stroke != "none" && part.StrokeWidth > 0)
                {
                    pen = new Pen(ResolveBrush(part.Stroke, foreground, background), part.StrokeWidth)
                    {
                        StartLineCap = ParseCap(part.LineCap),
                        EndLineCap = ParseCap(part.LineCap),
                        LineJoin = ParseJoin(part.LineJoin),
                        MiterLimit = 4,
                    };
                    pen.Freeze();
                }
                dc.DrawGeometry(fill, pen, geometry);
            }

            dc.Pop();
            dc.Pop();
        }

        private Geometry GetGeometry(string data)
        {
            if (!m_GeometryCache.TryGetValue(data, out Geometry geometry))
            {
                geometry = Geometry.Parse(data);
                geometry.Freeze();
                m_GeometryCache.Add(data, geometry);
            }
            return geometry;
        }

        private Brush ResolveBrush(string spec, RgbaColor foreground, RgbaColor background)
        {
            if (string.IsNullOrEmpty(spec))
                spec = "fg";
            if (spec == "none")
                return null;
            RgbaColor c = spec == "fg" ? foreground : spec == "bg" ? background : m_Catalog.GetColor(spec);
            return Brushes(c);
        }

        public static SolidColorBrush Brushes(RgbaColor c)
        {
            var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }

        private static PenLineCap ParseCap(string cap)
        {
            switch ((cap ?? "round").ToLower(CultureInfo.InvariantCulture))
            {
                case "flat":
                    return PenLineCap.Flat;
                case "square":
                    return PenLineCap.Square;
                default:
                    return PenLineCap.Round;
            }
        }

        private static PenLineJoin ParseJoin(string join)
        {
            switch ((join ?? "round").ToLower(CultureInfo.InvariantCulture))
            {
                case "miter":
                    return PenLineJoin.Miter;
                case "bevel":
                    return PenLineJoin.Bevel;
                default:
                    return PenLineJoin.Round;
            }
        }
    }
}
