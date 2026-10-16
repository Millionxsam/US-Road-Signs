using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using USRoadSigns.Catalog;
using USRoadSigns.Geometry;

namespace SignForge.Rendering
{
    /// <summary>
    /// Renders the printed artwork of a sign face. Drawing happens in "face inches": origin at the top-left
    /// of the plate bounding box, +Y down. The texture maps the full bounding box to UV [0,1], exactly like
    /// the plate mesh built by <see cref="AssemblyBuilder"/>.
    /// </summary>
    public sealed class FaceRenderer
    {
        private readonly SignCatalog m_Catalog;
        private readonly TextRenderer m_Text;
        private readonly SymbolRenderer m_Symbols;

        public const double TargetPixelsPerInch = 14.0;

        public FaceRenderer(SignCatalog catalog)
        {
            m_Catalog = catalog;
            m_Text = new TextRenderer();
            m_Symbols = new SymbolRenderer(catalog);
        }

        public TextRenderer Text => m_Text;

        /// <summary>Power-of-two texture size for a face (automatic unless overridden in the data).</summary>
        public static void TextureSize(FaceDef face, out int width, out int height)
        {
            Vec2 size = ShapeLibrary.BoundingSize(face);
            width = face.TextureWidth > 0 ? face.TextureWidth : Pot(size.X * TargetPixelsPerInch, 64, 2048);
            height = face.TextureHeight > 0 ? face.TextureHeight : Pot(size.Y * TargetPixelsPerInch, 64, 2048);
            // Keep very elongated faces reasonably sharp along the short axis.
            int max = Math.Max(width, height);
            int minShort = max >= 1024 ? 256 : 128;
            if (width < minShort)
                width = minShort;
            if (height < minShort)
                height = minShort;
        }

        private static int Pot(double pixels, int min, int max)
        {
            int p = min;
            while (p < max && p * 1.5 < pixels)
                p *= 2;
            return p;
        }

        public PixelBuffer RenderTexture(FaceDef face, int supersample = 3)
        {
            TextureSize(face, out int width, out int height);
            Vec2 size = ShapeLibrary.BoundingSize(face);
            double sx = width / (double)size.X;
            double sy = height / (double)size.Y;
            PixelBuffer buffer = PixelBuffer.RenderSupersampled(dc =>
            {
                dc.PushTransform(new ScaleTransform(sx, sy));
                DrawFace(dc, face, fillCanvas: true);
                dc.Pop();
            }, width, height, supersample);
            buffer.AddSheetingGrain(face.Id.GetHashCode(), 0.010f, 0.012f);
            return buffer;
        }

        /// <summary>Draws the face in face-inch coordinates.</summary>
        /// <param name="fillCanvas">Fill the whole bounding box with the background (textures) instead of only the plate shape (icons).</param>
        public void DrawFace(DrawingContext dc, FaceDef face, bool fillCanvas)
        {
            Vec2 size = ShapeLibrary.BoundingSize(face);
            RgbaColor background = m_Catalog.GetColor(face.Background);
            Geometry plate = OutlineGeometry(face, 0f, face.CornerRadius, size);

            if (fillCanvas)
                dc.DrawRectangle(SymbolRenderer.Brushes(background), null, new Rect(-1, -1, size.X + 2, size.Y + 2));
            else
                dc.DrawGeometry(SymbolRenderer.Brushes(background), null, plate);

            if (!fillCanvas)
                dc.PushClip(plate);

            foreach (ElementDef element in face.Elements)
                DrawElement(dc, face, element, background);

            if (face.Border != null && face.Border.Width > 0f && ShapeLibrary.SupportsBorderOutline(face.Shape))
            {
                Geometry outer = OutlineGeometry(face, face.Border.Inset, face.Border.Radius, size);
                Geometry inner = OutlineGeometry(face, face.Border.Inset + face.Border.Width, Math.Max(0f, face.Border.Radius - face.Border.Width), size);
                var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
                ring.Children.Add(outer);
                ring.Children.Add(inner);
                dc.DrawGeometry(SymbolRenderer.Brushes(m_Catalog.GetColor(face.Border.Color)), null, ring);
            }

            if (!fillCanvas)
                dc.Pop();
        }

        private void DrawElement(DrawingContext dc, FaceDef face, ElementDef element, RgbaColor background)
        {
            string type = (element.Type ?? "").ToLowerInvariant();
            RgbaColor color = m_Catalog.GetColor(string.IsNullOrEmpty(element.Color) ? "black" : element.Color);
            Brush brush = SymbolRenderer.Brushes(color);
            Pen pen = null;
            if (!string.IsNullOrEmpty(element.StrokeColor) && element.StrokeWidth > 0f)
            {
                pen = new Pen(SymbolRenderer.Brushes(m_Catalog.GetColor(element.StrokeColor)), element.StrokeWidth) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
            }

            switch (type)
            {
                case "text":
                    m_Text.Draw(dc, element, brush);
                    break;

                case "symbol":
                    m_Symbols.Draw(dc, element, color, background);
                    break;

                case "arrow":
                    DrawArrow(dc, element, brush);
                    break;

                case "rect":
                {
                    TextRenderer.PushElementTransform(dc, element);
                    var rect = new Rect(element.X - element.Width * 0.5, element.Y - element.Height * 0.5, element.Width, element.Height);
                    dc.DrawRoundedRectangle(brush, pen, rect, element.CornerRadius, element.CornerRadius);
                    dc.Pop();
                    break;
                }

                case "ellipse":
                {
                    TextRenderer.PushElementTransform(dc, element);
                    dc.DrawEllipse(brush, pen, new Point(element.X, element.Y), element.Width * 0.5, element.Height * 0.5);
                    dc.Pop();
                    break;
                }

                case "polygon":
                {
                    if (element.Points == null || element.Points.Count < 6)
                        break;
                    var points = new List<Point>();
                    for (int i = 0; i + 1 < element.Points.Count; i += 2)
                        points.Add(new Point(element.Points[i], element.Points[i + 1]));
                    TextRenderer.PushElementTransform(dc, element);
                    dc.DrawGeometry(brush, pen, Polygon(points));
                    dc.Pop();
                    break;
                }

                case "path":
                {
                    Geometry geometry = Geometry.Parse(element.Data);
                    TextRenderer.PushElementTransform(dc, element);
                    dc.DrawGeometry(element.Color == "none" ? null : brush, pen, geometry);
                    dc.Pop();
                    break;
                }

                case "shield":
                    DrawShield(dc, element);
                    break;

                default:
                    throw new InvalidOperationException("Face '" + face.Id + "': unknown element type '" + element.Type + "'");
            }
        }

        private static void DrawArrow(DrawingContext dc, ElementDef e, Brush brush)
        {
            var tail = new Vector(e.X1, e.Y1);
            var tip = new Vector(e.X2, e.Y2);
            Vector dir = tip - tail;
            double length = dir.Length;
            if (length < 1e-4)
                return;
            dir /= length;
            var perp = new Vector(-dir.Y, dir.X);
            double headLength = Math.Min(e.HeadLength, length * 0.9);
            Vector neck = tip - dir * headLength;
            double sw = e.ShaftWidth * 0.5, hw = e.HeadWidth * 0.5;
            var points = new[]
            {
                (Point)(tail + perp * sw), (Point)(neck + perp * sw), (Point)(neck + perp * hw), (Point)tip,
                (Point)(neck - perp * hw), (Point)(neck - perp * sw), (Point)(tail - perp * sw),
            };
            dc.DrawGeometry(brush, null, Polygon(points));
        }

        private void DrawShield(DrawingContext dc, ElementDef e)
        {
            string kind = (e.Kind ?? "interstate").ToLowerInvariant();
            double w = e.Width, h = e.Height;
            double left = e.X - w * 0.5, top = e.Y - h * 0.5;
            Brush white = SymbolRenderer.Brushes(m_Catalog.GetColor("white"));
            Brush black = SymbolRenderer.Brushes(m_Catalog.GetColor("black"));

            if (kind == "interstate")
            {
                List<Vec2> outer = ShapeLibrary.InterstateShield((float)w, (float)h);
                List<Vec2> inner = ShapeLibrary.InterstateShield((float)(w * 0.90), (float)(h * 0.90));
                Geometry outerG = PolygonFromCentered(outer, e.X, e.Y);
                Geometry innerG = PolygonFromCentered(inner, e.X, e.Y + h * 0.012);
                dc.DrawGeometry(white, null, outerG);
                dc.PushClip(innerG);
                dc.DrawRectangle(SymbolRenderer.Brushes(m_Catalog.GetColor("blue")), null, new Rect(left, top, w, h));
                dc.DrawRectangle(SymbolRenderer.Brushes(m_Catalog.GetColor("red")), null, new Rect(left, top, w, h * 0.30));
                dc.DrawRectangle(white, null, new Rect(left, top + h * 0.30, w, h * 0.035));
                dc.Pop();
                m_Text.Draw(dc, new ElementDef { Text = "INTERSTATE", Font = "C", Size = (float)(h * 0.085), X = e.X, Y = (float)(top + h * 0.19), Color = "white", MaxWidth = (float)(w * 0.62) }, white);
                if (!string.IsNullOrEmpty(e.Text))
                {
                    string font = e.Text.Length >= 3 ? "C" : "D";
                    m_Text.Draw(dc, new ElementDef { Text = e.Text, Font = font, Size = (float)(h * 0.40), X = e.X, Y = (float)(top + h * 0.60), MaxWidth = (float)(w * 0.74) }, white);
                }
            }
            else if (kind == "us")
            {
                // White US route shield on the black square blank.
                Geometry shield = Geometry.Parse(
                    "M 0.08 0.04 C 0.26 0.1 0.38 0.06 0.5 0.0 C 0.62 0.06 0.74 0.1 0.92 0.04 " +
                    "C 0.94 0.2 0.88 0.3 0.94 0.44 C 1.0 0.62 0.98 0.84 0.5 0.98 C 0.02 0.84 0.0 0.62 0.06 0.44 " +
                    "C 0.12 0.3 0.06 0.2 0.08 0.04 Z").Clone();
                shield.Transform = new MatrixTransform(w, 0, 0, h, left, top);
                dc.DrawGeometry(white, null, shield);
                if (!string.IsNullOrEmpty(e.Text))
                {
                    string font = e.Text.Length >= 3 ? "C" : "D";
                    m_Text.Draw(dc, new ElementDef { Text = e.Text, Font = font, Size = (float)(h * 0.46), X = e.X, Y = (float)(top + h * 0.53), MaxWidth = (float)(w * 0.72) }, black);
                }
            }
            else
            {
                // Generic state route marker (M1-5): white circle.
                dc.DrawEllipse(white, null, new Point(e.X, e.Y), w * 0.5, h * 0.5);
                if (!string.IsNullOrEmpty(e.Text))
                {
                    string font = e.Text.Length >= 3 ? "C" : "D";
                    m_Text.Draw(dc, new ElementDef { Text = e.Text, Font = font, Size = (float)(h * 0.48), X = e.X, Y = e.Y, MaxWidth = (float)(w * 0.74) }, black);
                }
            }
        }

        /// <summary>Plate outline as WPF geometry in face-inch coordinates.</summary>
        public static Geometry OutlineGeometry(FaceDef face, float inset, float radius, Vec2 size)
        {
            List<Vec2> outline = ShapeLibrary.Outline(face, inset, radius, 12);
            return PolygonFromCentered(outline, size.X * 0.5, size.Y * 0.5);
        }

        private static Geometry PolygonFromCentered(List<Vec2> outline, double cx, double cy)
        {
            var points = new List<Point>(outline.Count);
            foreach (Vec2 p in outline)
                points.Add(new Point(cx + p.X, cy - p.Y));
            return Polygon(points);
        }

        public static Geometry Polygon(IList<Point> points)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Count; i++)
                    ctx.LineTo(points[i], true, true);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
