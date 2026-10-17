using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using USRoadSigns.Catalog;
using USRoadSigns.Geometry;

namespace SignForge.Rendering
{
    /// <summary>Renders toolbar thumbnails: the sign panels as mounted, with a short post stub.</summary>
    public sealed class IconRenderer
    {
        private readonly SignCatalog m_Catalog;
        private readonly FaceRenderer m_Faces;

        public IconRenderer(SignCatalog catalog, FaceRenderer faces)
        {
            m_Catalog = catalog;
            m_Faces = faces;
        }

        private struct PanelBox
        {
            public FaceDef Face;
            public Vec2 Size;
            public double Left;
            public double Top;
            /// <summary>LED color name when this panel carries flashing LEDs, else null.</summary>
            public string LedColor;
        }

        public PixelBuffer RenderSign(SignDef sign, int size)
        {
            var boxes = new List<PanelBox>();
            int litPanel = sign.IsFlashing ? BorderLights.LitPanelIndex(m_Catalog, sign) : -1;
            double cursor = 0;
            double minX = double.MaxValue, maxX = double.MinValue;
            for (int i = 0; i < sign.Panels.Count; i++)
            {
                PanelDef panel = sign.Panels[i];
                FaceDef face = m_Catalog.Faces[panel.Face];
                Vec2 s = ShapeLibrary.BoundingSize(face);
                if (i > 0)
                    cursor += panel.GapAbove >= 0 ? panel.GapAbove : m_Catalog.Root.Mounting.PanelGap;
                var box = new PanelBox { Face = face, Size = s, Left = panel.OffsetX - s.X * 0.5, Top = cursor, LedColor = i == litPanel ? sign.LedColor : null };
                boxes.Add(box);
                cursor += s.Y;
                minX = Math.Min(minX, box.Left);
                maxX = Math.Max(maxX, box.Left + s.X);
            }
            return RenderBoxes(boxes, cursor, minX, maxX, size, postStub: true);
        }

        public PixelBuffer RenderFace(FaceDef face, int size, bool postStub, string ledColor = null)
        {
            Vec2 s = ShapeLibrary.BoundingSize(face);
            var boxes = new List<PanelBox> { new PanelBox { Face = face, Size = s, Left = -s.X * 0.5, Top = 0, LedColor = ledColor } };
            return RenderBoxes(boxes, s.Y, -s.X * 0.5, s.X * 0.5, size, postStub);
        }

        private PixelBuffer RenderBoxes(List<PanelBox> boxes, double panelsHeight, double minX, double maxX, int size, bool postStub)
        {
            double stub = postStub ? Math.Max(6.0, panelsHeight * 0.30) : 0.0;
            double contentW = maxX - minX;
            double contentH = panelsHeight + stub;
            double margin = size * 0.05;
            double scale = (size - margin * 2) / Math.Max(contentW, contentH);
            double offsetX = (size - contentW * scale) * 0.5 - minX * scale;
            double offsetY = (size - contentH * scale) * 0.5;

            return PixelBuffer.RenderSupersampled(dc =>
            {
                dc.PushTransform(new TranslateTransform(offsetX, offsetY));
                dc.PushTransform(new ScaleTransform(scale, scale));

                if (postStub)
                {
                    double postWidth = Math.Max(2.2, 2.0 / scale);
                    var postBrush = new LinearGradientBrush(Color.FromRgb(0x6E, 0x72, 0x75), Color.FromRgb(0xB4, 0xB8, 0xBB), new Point(0, 0.5), new Point(1, 0.5));
                    postBrush.Freeze();
                    dc.DrawRectangle(postBrush, null, new Rect(-postWidth * 0.5, panelsHeight * 0.5, postWidth, panelsHeight * 0.5 + stub));
                }

                var outlinePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 10, 10, 10)), 1.3 / scale);
                outlinePen.Freeze();
                foreach (PanelBox box in boxes)
                {
                    dc.PushTransform(new TranslateTransform(box.Left, box.Top));
                    m_Faces.DrawFace(dc, box.Face, fillCanvas: false);
                    dc.DrawGeometry(null, outlinePen, FaceRenderer.OutlineGeometry(box.Face, 0f, box.Face.CornerRadius, box.Size));
                    if (box.LedColor != null)
                        DrawLights(dc, box, scale);
                    dc.Pop();
                }

                dc.Pop();
                dc.Pop();
            }, size, size, 3);
        }

        /// <summary>LEDs shown lit, enlarged so they stay readable at icon size.</summary>
        private void DrawLights(DrawingContext dc, PanelBox box, double scale)
        {
            FlashingDefaults settings = m_Catalog.Root.Flashing ?? new FlashingDefaults();
            RgbaColor c = settings.GetColor(box.LedColor);
            Color led = Color.FromRgb(c.R, c.G, c.B);
            double radius = Math.Max(settings.LampDiameter * 0.75, 2.6 / scale);

            var glow = new RadialGradientBrush(Color.FromArgb(170, led.R, led.G, led.B), Color.FromArgb(0, led.R, led.G, led.B));
            glow.Freeze();
            var lens = new RadialGradientBrush(Color.FromRgb(0xFF, 0xF6, 0xC8), led) { GradientOrigin = new Point(0.4, 0.35) };
            lens.Freeze();
            var bezel = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), radius * 0.35);
            bezel.Freeze();

            foreach (Vec2 p in BorderLights.Positions(box.Face, settings))
            {
                var center = new Point(box.Size.X * 0.5 + p.X, box.Size.Y * 0.5 - p.Y);
                dc.DrawEllipse(glow, null, center, radius * 2.3, radius * 2.3);
                dc.DrawEllipse(lens, bezel, center, radius, radius);
            }
        }
    }
}
