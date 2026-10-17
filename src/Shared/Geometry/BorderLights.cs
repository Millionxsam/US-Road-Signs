using System;
using System.Collections.Generic;
using USRoadSigns.Catalog;

namespace USRoadSigns.Geometry
{
    /// <summary>
    /// Layout of the flashing LEDs of a "(Flashing)" sign variant. Like commercial LED-enhanced signs
    /// (MUTCD 2A.07), the LEDs sit on the border band: one at every corner, plus evenly spaced ones along
    /// each side so neighbors are never further apart than <see cref="FlashingDefaults.MaxSpacing"/>.
    /// Units: inches, origin at the center of the panel's bounding box, +X viewer right, +Y up.
    /// </summary>
    public static class BorderLights
    {
        /// <summary>Index of the panel that carries the LEDs: the sign's explicit choice, else the largest panel.</summary>
        public static int LitPanelIndex(SignCatalog catalog, SignDef sign)
        {
            if (sign.FlashingPanel >= 0 && sign.FlashingPanel < sign.Panels.Count)
                return sign.FlashingPanel;
            int best = 0;
            float bestArea = -1f;
            for (int i = 0; i < sign.Panels.Count; i++)
            {
                Vec2 size = ShapeLibrary.BoundingSize(catalog.Faces[sign.Panels[i].Face]);
                float area = size.X * size.Y;
                if (area > bestArea + 1e-3f)
                {
                    bestArea = area;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Distance from the plate edge to the LED centers: the middle of the printed border, or a
        /// comparable margin on faces without one, never so close that the lens overhangs the edge.</summary>
        public static float Inset(FaceDef face, FlashingDefaults settings)
        {
            Vec2 size = ShapeLibrary.BoundingSize(face);
            float inset = face.Border != null && face.Border.Width > 0f
                ? face.Border.Inset + face.Border.Width * 0.5f
                : Math.Max(1.25f, 0.045f * Math.Min(size.X, size.Y));
            return Math.Max(inset, settings.LampDiameter * 0.5f + 0.45f);
        }

        public static List<Vec2> Positions(FaceDef face, FlashingDefaults settings)
        {
            var result = new List<Vec2>();
            float inset = Inset(face, settings);
            float spacing = Math.Max(4f, settings.MaxSpacing);

            if (ShapeLibrary.Normalize(face.Shape) == "circle")
            {
                float r = ShapeLibrary.BoundingSize(face).X * 0.5f - inset;
                int count = Math.Max(8, (int)Math.Ceiling(2f * MathUtil.Pi * r / spacing));
                count = (count + 3) / 4 * 4;   // keep the pattern symmetric (top, bottom, left, right)
                for (int i = 0; i < count; i++)
                {
                    float angle = MathUtil.Pi * 0.5f + 2f * MathUtil.Pi * i / count;
                    result.Add(new Vec2((float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r));
                }
                return result;
            }

            // Sharp-cornered border line; corner LEDs go at its vertices, pulled in along the corner bisector
            // where the plate's rounded corner would otherwise cut into the LED housing.
            List<Vec2> outline = ShapeLibrary.Outline(face, inset, 0f);
            float clearance = settings.LampDiameter * 0.5f + 0.45f;
            float plateRadius = Math.Max(0f, face.CornerRadius);
            var corners = new Vec2[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 prev = outline[(i + outline.Count - 1) % outline.Count];
                Vec2 cur = outline[i];
                Vec2 next = outline[(i + 1) % outline.Count];
                Vec2 toPrev = (prev - cur).Normalized;
                Vec2 toNext = (next - cur).Normalized;
                float halfAngle = (float)Math.Acos(MathUtil.Clamp(Vec2.Dot(toPrev, toNext), -1f, 1f)) * 0.5f;
                float sin = (float)Math.Sin(halfAngle);
                corners[i] = cur;
                if (sin < 1e-3f)
                    continue;
                // Distance from the plate's sharp corner: current, and needed to clear the rounded corner.
                float current = inset / sin;
                float needed = clearance + plateRadius * (1f / sin - 1f);
                if (needed > current)
                    corners[i] = cur + (toPrev + toNext).Normalized * (needed - current);
            }
            outline = new List<Vec2>(corners);

            // Non-square rectangles get at least 10 LEDs: two between the corners on each long side, one on each short side.
            Vec2 size = ShapeLibrary.BoundingSize(face);
            bool oblong = ShapeLibrary.Normalize(face.Shape) == "rect" && Math.Abs(size.X - size.Y) > 0.5f;
            bool wide = size.X > size.Y;

            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 a = outline[i];
                Vec2 b = outline[(i + 1) % outline.Count];
                float length = (b - a).Length;
                if (length < 1e-3f)
                    continue;
                int segments = Math.Max(1, (int)Math.Ceiling(length / spacing - 0.05f));
                if (oblong)
                {
                    bool horizontal = Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y);
                    segments = Math.Max(segments, horizontal == wide ? 3 : 2);
                }
                for (int k = 0; k < segments; k++)
                    result.Add(a + (b - a) * ((float)k / segments));
            }
            return result;
        }
    }
}
