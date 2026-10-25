using System;
using System.Collections.Generic;
using USRoadSigns.Catalog;
using USRoadSigns.Digital;
using USRoadSigns.Geometry;

namespace USRoadSigns.Route
{
    public enum PlateRole
    {
        Aux,
        Cardinal,
        Shield,
        Arrow,
        Guide,
    }

    /// <summary>
    /// One printed plate of a route sign: its outline (the plate is cut to it) and its artwork. <see cref="Key"/>
    /// identifies the artwork, so plates showing the same thing share one texture.
    /// </summary>
    public sealed class PlateFace
    {
        public PlateRole Role;
        public string Key;
        public float Width;
        public float Height;
        /// <summary>Plate outline in inches, centered, +Y up, counter-clockwise.</summary>
        public List<Vec2> Outline;
        /// <summary>Corner polygon without rounding (LED placement on straight-edged plates); null for curved shields.</summary>
        public List<Vec2> Corners;
        /// <summary>Inset of the flashing LEDs from the plate edge, inches.</summary>
        public float LedInset;
        public Func<RgbaImage> Render;
    }

    /// <summary>
    /// Artwork of route marker plates (MUTCD M1, M3, M4, M5/M6) and destination signs (D1), drawn at runtime
    /// with <see cref="FaceCanvas"/>. Sizes are inches; proportions follow the MUTCD sign drawings.
    /// </summary>
    public static class RouteFaces
    {
        // Same sign colors as data/catalog.json.
        public static readonly RgbaColor White = new RgbaColor(0xE6, 0xE7, 0xE2);
        public static readonly RgbaColor Black = new RgbaColor(0x16, 0x16, 0x16);
        public static readonly RgbaColor Red = new RgbaColor(0xB1, 0x1D, 0x2D);
        public static readonly RgbaColor Yellow = new RgbaColor(0xF2, 0xB6, 0x0E);
        public static readonly RgbaColor Green = new RgbaColor(0x00, 0x67, 0x4A);
        public static readonly RgbaColor Blue = new RgbaColor(0x10, 0x48, 0x8F);
        public static readonly RgbaColor Brown = new RgbaColor(0x5A, 0x38, 0x25);

        private const float SmallPlatePpi = 14f;
        private const float GuidePpi = 10f;

        /// <summary>Plates taller than this many inches, for a 24-inch marker; everything scales with the marker size.</summary>
        public static float PlateHeight(PlateRole role, float size)
        {
            switch (role)
            {
                case PlateRole.Shield: return size;
                case PlateRole.Arrow: return size * 0.625f;
                default: return size * 0.5f;
            }
        }

        /// <summary>Wide (30 x 24) markers for three-character route numbers, where the MUTCD calls for them.</summary>
        public static bool IsWide(ShieldKind kind, string number, bool allowWide)
        {
            if (!allowWide || number == null || number.Length < 3)
                return false;
            return kind == ShieldKind.Interstate || kind == ShieldKind.BusinessLoop || kind == ShieldKind.BusinessSpur
                || kind == ShieldKind.US || kind == ShieldKind.State;
        }

        /// <summary>Background and legend colors of the plates above and below a marker.</summary>
        public static void PlateColors(ShieldKind kind, PlateStyle style, out RgbaColor background, out RgbaColor legend)
        {
            background = White;
            legend = Black;
            if (style == PlateStyle.White)
                return;
            switch (kind)
            {
                case ShieldKind.Interstate:
                    background = Blue;
                    legend = White;
                    break;
                case ShieldKind.BusinessLoop:
                case ShieldKind.BusinessSpur:
                    background = Green;
                    legend = White;
                    break;
                case ShieldKind.County:
                    background = Blue;
                    legend = Yellow;
                    break;
                case ShieldKind.Forest:
                    background = Brown;
                    legend = White;
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Route marker columns
        // ------------------------------------------------------------------------------------------

        /// <summary>The plates of one column from top to bottom (absent plates are null).</summary>
        public static PlateFace Aux(RouteColumn column, PlateStyle style, float size, float width)
        {
            if (column.Aux == AuxKind.None)
                return null;
            return TextPlate(PlateRole.Aux, RouteSignState.Legend(column.Aux), column.Shield, style, size, width);
        }

        public static PlateFace Cardinal(RouteColumn column, PlateStyle style, float size, float width)
        {
            if (column.Cardinal == CardinalKind.None)
                return null;
            return TextPlate(PlateRole.Cardinal, RouteSignState.Legend(column.Cardinal), column.Shield, style, size, width);
        }

        private static PlateFace TextPlate(PlateRole role, string legend, ShieldKind shield, PlateStyle style, float size, float width)
        {
            float h = PlateHeight(role, size);
            float k = size / 24f;
            PlateColors(shield, style, out RgbaColor bg, out RgbaColor fg);
            float radius = 1.5f * k;
            return new PlateFace
            {
                Role = role,
                Key = "T|" + legend + "|" + Hex(bg) + Hex(fg) + "|" + F(width) + "x" + F(h),
                Width = width,
                Height = h,
                Outline = Shapes.RoundedRect(width, h, radius),
                Corners = Shapes.RoundedRect(width, h, 0f),
                LedInset = 0.5f * k,
                Render = () =>
                {
                    FaceCanvas c = PlateCanvas(width, h, radius, k, bg, fg);
                    float cap = h * 0.5f;
                    c.DrawText(legend, FontSeries.D, cap, 0f, -cap * 0.5f, FaceCanvas.Align.Center, width - 3.6f * k, fg, FontSeries.B);
                    return c.ToImage();
                },
            };
        }

        public static PlateFace ArrowPlate(RouteColumn column, PlateStyle style, float size)
        {
            if (column.Arrow == ArrowKind.None)
                return null;
            float k = size / 24f;
            float w = size * 0.875f;
            float h = PlateHeight(PlateRole.Arrow, size);
            PlateColors(column.Shield, style, out RgbaColor bg, out RgbaColor fg);
            float radius = 1.5f * k;
            ArrowKind arrow = column.Arrow;
            return new PlateFace
            {
                Role = PlateRole.Arrow,
                Key = "A|" + (int)arrow + "|" + Hex(bg) + Hex(fg) + "|" + F(w) + "x" + F(h),
                Width = w,
                Height = h,
                Outline = Shapes.RoundedRect(w, h, radius),
                Corners = Shapes.RoundedRect(w, h, 0f),
                LedInset = 0.5f * k,
                Render = () =>
                {
                    FaceCanvas c = PlateCanvas(w, h, radius, k, bg, fg);
                    float margin = 2.2f * k;
                    Vec2 unit = Shapes.ArrowExtent(arrow, 1f);
                    float arrowSize = Math.Min((w - 2f * margin) / unit.X, (h - 2f * margin) / unit.Y);
                    DrawArrow(c, arrow, 0f, 0f, arrowSize, fg);
                    return c.ToImage();
                },
            };
        }

        /// <summary>Plate with a thin border line in the legend color just inside the edge.</summary>
        private static FaceCanvas PlateCanvas(float w, float h, float radius, float k, RgbaColor bg, RgbaColor fg)
        {
            var c = new FaceCanvas(w, h, SmallPlatePpi);
            c.Clear(bg);
            float inset = 0.25f * k;
            float border = 0.5f * k;
            c.FillPolygons(new IReadOnlyList<Vec2>[]
            {
                Shapes.RoundedRect(w, h, radius, inset),
                Reverse(Shapes.RoundedRect(w, h, radius, inset + border)),
            }, fg);
            return c;
        }

        public static void DrawArrow(FaceCanvas c, ArrowKind arrow, float cx, float cy, float size, RgbaColor color)
        {
            Shapes.ArrowShape shape = Shapes.Arrow(arrow, cx, cy, size);
            c.DrawStrokes(shape.Shafts, shape.ShaftWidth, float.MinValue, float.MaxValue, color, roundEnds: false);
            c.FillPolygons(shape.Heads.ConvertAll(h => (IReadOnlyList<Vec2>)h), color);
        }

        // ------------------------------------------------------------------------------------------
        // Route markers (M1 series)
        // ------------------------------------------------------------------------------------------

        public static PlateFace Shield(RouteColumn column, float size, bool allowWide)
        {
            ShieldKind kind = column.Shield;
            string number = column.Number ?? "";
            string name = column.Name ?? "";
            float h = size;
            float w = IsWide(kind, number, allowWide) ? size * 1.25f : size;
            float k = size / 24f;
            var face = new PlateFace
            {
                Role = PlateRole.Shield,
                Key = "S|" + (int)kind + "|" + number + "|" + (kind == ShieldKind.County ? name : "") + "|" + F(w) + "x" + F(h),
                Width = w,
                Height = h,
                LedInset = 1.1f * k,
            };
            switch (kind)
            {
                case ShieldKind.Interstate:
                case ShieldKind.BusinessLoop:
                case ShieldKind.BusinessSpur:
                    face.Outline = Shapes.InterstateShield(w, h);
                    face.LedInset = 1.25f * k;
                    face.Render = () => RenderInterstate(kind, number, w, h);
                    break;
                case ShieldKind.County:
                    face.Outline = Shapes.CountyPentagon(w, h, 0f, 1.0f * k);
                    face.Corners = Shapes.CountyPentagon(w, h);
                    face.Render = () => RenderCounty(number, name, w, h, k);
                    break;
                case ShieldKind.Forest:
                    face.Outline = Shapes.ForestMarker(w, h, 0f, 1.0f * k);
                    face.Corners = Shapes.ForestMarker(w, h);
                    face.Render = () => RenderForest(number, w, h, k);
                    break;
                default:
                    face.Outline = Shapes.RoundedRect(w, h, 1.5f * k);
                    face.Corners = Shapes.RoundedRect(w, h, 0f);
                    face.LedInset = 0.75f * k;
                    face.Render = () => kind == ShieldKind.US ? RenderUs(number, w, h) : RenderState(number, w, h);
                    break;
            }
            return face;
        }

        private static FontSeries NumberSeries(string number) => number.Length >= 3 ? FontSeries.C : FontSeries.D;

        private static RgbaImage RenderInterstate(ShieldKind kind, string number, float w, float h)
        {
            bool business = kind != ShieldKind.Interstate;
            var c = new FaceCanvas(w, h, SmallPlatePpi);
            c.Clear(White);
            List<Vec2> inner = ShieldOutlines.Interstate(w, h, inner: true);
            // Measured on the M1-1 artwork: red crown to 0.206 h from the top, white band to 0.228 h.
            float crownBottom = h * 0.294f;
            float band = h * 0.022f;
            RgbaColor body = business ? Green : Blue;
            c.FillPolygon(inner, body);
            c.FillPolygon(ClipAbove(inner, crownBottom - band), White);
            c.FillPolygon(ClipAbove(inner, crownBottom), business ? Green : Red);

            float crownText = h * 0.107f;
            c.DrawText(business ? "BUSINESS" : "INTERSTATE", FontSeries.C, crownText, 0f, h * 0.366f - crownText * 0.5f, FaceCanvas.Align.Center, w * 0.7f, White, FontSeries.B);
            if (business)
            {
                float small = h * 0.085f;
                c.DrawText(kind == ShieldKind.BusinessSpur ? "SPUR" : "LOOP", FontSeries.D, small, 0f, h * 0.14f - small * 0.5f, FaceCanvas.Align.Center, w * 0.5f, White);
                float cap = h * 0.38f;
                c.DrawText(number, NumberSeries(number), cap, 0f, -h * 0.15f - cap * 0.5f, FaceCanvas.Align.Center, w * 0.62f, White, FontSeries.B);
            }
            else
            {
                float cap = h * 0.45f;
                c.DrawText(number, NumberSeries(number), cap, 0f, -h * 0.079f - cap * 0.5f, FaceCanvas.Align.Center, w * 0.6f, White, FontSeries.B);
            }
            return c.ToImage();
        }

        private static RgbaImage RenderUs(string number, float w, float h)
        {
            var c = new FaceCanvas(w, h, SmallPlatePpi);
            c.Clear(Black);
            c.FillPolygon(Shapes.UsShield(w, h), White);
            float cap = h * 0.46f;
            c.DrawText(number, NumberSeries(number), cap, 0f, h * 0.02f - cap * 0.5f, FaceCanvas.Align.Center, w * 0.68f, Black, FontSeries.B);
            return c.ToImage();
        }

        private static RgbaImage RenderState(string number, float w, float h)
        {
            var c = new FaceCanvas(w, h, SmallPlatePpi);
            c.Clear(Black);
            c.FillEllipse(0f, 0f, w * 0.5f - h * 0.04f, h * 0.46f, White);
            float cap = h * 0.46f;
            c.DrawText(number, NumberSeries(number), cap, 0f, -cap * 0.5f, FaceCanvas.Align.Center, w - h * 0.24f, Black, FontSeries.B);
            return c.ToImage();
        }

        private static RgbaImage RenderCounty(string number, string name, float w, float h, float k)
        {
            var c = new FaceCanvas(w, h, SmallPlatePpi);
            c.Clear(Yellow);
            c.FillPolygon(Shapes.CountyPentagon(w, h, 0.75f * k, 0.4f * k), Blue);
            float small = h * 0.11f;
            if (!string.IsNullOrEmpty(name))
                c.DrawText(name, FontSeries.D, small, 0f, h * 0.29f - small * 0.5f, FaceCanvas.Align.Center, w * 0.56f, Yellow, FontSeries.B);
            float cap = h * 0.38f;
            c.DrawText(number, NumberSeries(number), cap, 0f, -h * 0.03f - cap * 0.5f, FaceCanvas.Align.Center, w * 0.76f, Yellow, FontSeries.B);
            c.DrawText("COUNTY", FontSeries.D, small, 0f, -h * 0.35f - small * 0.5f, FaceCanvas.Align.Center, w * 0.72f, Yellow, FontSeries.B);
            return c.ToImage();
        }

        private static RgbaImage RenderForest(string number, float w, float h, float k)
        {
            var c = new FaceCanvas(w, h, SmallPlatePpi);
            c.Clear(White);
            c.FillPolygon(Shapes.ForestMarker(w, h, 0.75f * k, 0.4f * k), Brown);
            float cap = h * 0.38f;
            c.DrawText(number, NumberSeries(number), cap, 0f, h * 0.2f - cap * 0.5f, FaceCanvas.Align.Center, w * 0.78f, White, FontSeries.B);
            c.FillRect(0f, -h * 0.085f, w - 2.2f * k, 0.5f * k, White);
            float small = h * 0.095f;
            c.DrawText("National", FontSeries.D, small, 0f, -h * 0.215f - small * 0.5f, FaceCanvas.Align.Center, w * 0.62f, White);
            c.DrawText("Forest", FontSeries.D, small, 0f, -h * 0.355f - small * 0.5f, FaceCanvas.Align.Center, w * 0.5f, White);
            return c.ToImage();
        }

        // ------------------------------------------------------------------------------------------
        // Destination signs (D1 series)
        // ------------------------------------------------------------------------------------------

        public static RgbaColor GuideBackground(GuideColor color)
        {
            switch (color)
            {
                case GuideColor.Blue: return Blue;
                case GuideColor.Brown: return Brown;
                default: return Green;
            }
        }

        /// <summary>Destination sign corner radius and border, following the MUTCD guide sign table for the letter height.</summary>
        public static void GuideBorder(RouteSignType type, out float radius, out float inset, out float border)
        {
            float cap = type.LetterHeight;
            radius = cap * 0.5f;
            inset = cap * 0.0625f;
            border = cap * 0.125f;
        }

        public static PlateFace Destination(RouteSignType type, RouteSignState state)
        {
            float w = type.FaceWidth, h = type.FaceHeight;
            GuideBorder(type, out float radius, out _, out float border);
            return new PlateFace
            {
                Role = PlateRole.Guide,
                Key = "D|" + type.Id + "|" + state.Encode(type),
                Width = w,
                Height = h,
                Outline = Shapes.RoundedRect(w, h, radius),
                Corners = Shapes.RoundedRect(w, h, 0f),
                LedInset = border * 0.5f + type.LetterHeight * 0.0625f,
                Render = () => RenderDestination(type, state),
            };
        }

        private static RgbaImage RenderDestination(RouteSignType type, RouteSignState state)
        {
            float w = type.FaceWidth, h = type.FaceHeight, cap = type.LetterHeight;
            GuideBorder(type, out float radius, out float inset, out float border);
            RgbaColor bg = GuideBackground(state.Color);
            var c = new FaceCanvas(w, h, GuidePpi);
            c.Clear(bg);
            c.FillPolygons(new IReadOnlyList<Vec2>[]
            {
                Shapes.RoundedRect(w, h, radius, inset),
                Reverse(Shapes.RoundedRect(w, h, radius, inset + border)),
            }, White);

            DestinationLine[] lines = state.Lines;
            int n = Math.Max(1, lines.Length);
            float innerTop = h * 0.5f - inset - border;
            float innerLeft = -w * 0.5f + inset + border;
            float innerRight = -innerLeft;
            float rowHeight = (2f * innerTop) / n;
            float margin = cap * 0.7f;
            float gap = cap * 0.6f;
            float arrowSize = Math.Min(cap * 1.75f, rowHeight * 0.78f);

            // Columns: arrows pointing left or ahead go to the left of the names, right arrows to the right.
            float leftColumn = 0f, rightColumn = 0f, distanceColumn = 0f;
            foreach (DestinationLine line in lines)
            {
                float ext = Shapes.ArrowExtent(line.Arrow, arrowSize).X;
                if (IsRightArrow(line.Arrow))
                    rightColumn = Math.Max(rightColumn, ext);
                else if (line.Arrow != ArrowKind.None)
                    leftColumn = Math.Max(leftColumn, ext);
                if (!string.IsNullOrEmpty(line.Distance))
                    distanceColumn = Math.Max(distanceColumn, StrokeFont.Measure(line.Distance, FontSeries.E, cap));
            }

            float textLeft = innerLeft + margin + (leftColumn > 0f ? leftColumn + gap : 0f);
            float textRight = innerRight - margin - (rightColumn > 0f ? rightColumn + gap : 0f) - (distanceColumn > 0f ? distanceColumn + gap * 1.5f : 0f);
            float distanceRight = innerRight - margin - (rightColumn > 0f ? rightColumn + gap : 0f);

            for (int i = 0; i < lines.Length; i++)
            {
                DestinationLine line = lines[i];
                float rowCenter = innerTop - rowHeight * (i + 0.5f);
                if (!string.IsNullOrEmpty(line.Text))
                    c.DrawText(line.Text, FontSeries.E, cap, textLeft, rowCenter - cap * 0.5f, FaceCanvas.Align.Left, textRight - textLeft, White, FontSeries.C);
                if (!string.IsNullOrEmpty(line.Distance))
                    c.DrawText(line.Distance, FontSeries.E, cap, distanceRight, rowCenter - cap * 0.5f, FaceCanvas.Align.Right, distanceColumn * 1.05f, White, FontSeries.C);
                if (line.Arrow != ArrowKind.None)
                {
                    float ext = Shapes.ArrowExtent(line.Arrow, arrowSize).X;
                    float ax = IsRightArrow(line.Arrow)
                        ? innerRight - margin - rightColumn * 0.5f
                        : innerLeft + margin + leftColumn * 0.5f;
                    DrawArrow(c, line.Arrow, ax, rowCenter, arrowSize, White);
                }
                if (state.Dividers && i > 0)
                {
                    float y = innerTop - rowHeight * i;
                    c.FillRect(0f, y, innerRight - innerLeft - cap * 0.5f, border * 0.75f, White);
                }
            }
            return c.ToImage();
        }

        private static bool IsRightArrow(ArrowKind arrow) => arrow == ArrowKind.Right || arrow == ArrowKind.UpRight;

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>Part of a polygon above a horizontal line (Sutherland-Hodgman against one edge).</summary>
        public static List<Vec2> ClipAbove(IReadOnlyList<Vec2> polygon, float y)
        {
            var result = new List<Vec2>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vec2 a = polygon[i];
                Vec2 b = polygon[(i + 1) % polygon.Count];
                bool ina = a.Y >= y, inb = b.Y >= y;
                if (ina)
                    result.Add(a);
                if (ina != inb)
                {
                    float t = (y - a.Y) / (b.Y - a.Y);
                    result.Add(new Vec2(a.X + (b.X - a.X) * t, y));
                }
            }
            return result;
        }

        private static List<Vec2> Reverse(List<Vec2> points)
        {
            points.Reverse();
            return points;
        }

        private static string Hex(RgbaColor c) => c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
