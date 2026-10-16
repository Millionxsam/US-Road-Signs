using System;
using System.Collections.Generic;
using USRoadSigns.Catalog;

namespace USRoadSigns.Geometry
{
    /// <summary>
    /// Sign blank outlines. Used both for the 3D plate mesh and for the printed border artwork, so the
    /// texture and the geometry always agree. Units: inches, origin at the center of the bounding box,
    /// +Y up, polygons counter-clockwise.
    /// </summary>
    public static class ShapeLibrary
    {
        public const int DefaultArcSegments = 6;

        /// <summary>Size of the plate's bounding box in inches.</summary>
        public static Vec2 BoundingSize(FaceDef face)
        {
            switch (Normalize(face.Shape))
            {
                case "diamond":
                    return new Vec2(face.Width * MathUtil.Sqrt2, face.Height * MathUtil.Sqrt2);
                case "triangle":
                    return new Vec2(face.Width, face.Width * MathUtil.Sqrt3 * 0.5f);
                case "circle":
                    return new Vec2(face.Width, face.Width);
                case "crossbuck":
                {
                    float s = (face.Width + face.Height) * MathUtil.Sqrt2 * 0.5f;
                    return new Vec2(s, s);
                }
                default:
                    return new Vec2(face.Width, face.Height);
            }
        }

        public static bool SupportsBorderOutline(string shape)
        {
            string s = Normalize(shape);
            return s != "crossbuck" && s != "shield-interstate";
        }

        /// <summary>
        /// Outline of the blank shrunk by <paramref name="inset"/> with corners rounded to <paramref name="radius"/>.
        /// inset = 0 and radius = face.CornerRadius gives the physical plate.
        /// </summary>
        public static List<Vec2> Outline(FaceDef face, float inset, float radius, int arcSegments = DefaultArcSegments)
        {
            Vec2 size = BoundingSize(face);
            float hw = size.X * 0.5f;
            float hh = size.Y * 0.5f;
            switch (Normalize(face.Shape))
            {
                case "diamond":
                    return InsetAndRound(new[] { new Vec2(0, -hh), new Vec2(hw, 0), new Vec2(0, hh), new Vec2(-hw, 0) }, inset, radius, arcSegments);

                case "octagon":
                {
                    float side = face.Width / (1f + MathUtil.Sqrt2);
                    float a = side * 0.5f;
                    var poly = new[]
                    {
                        new Vec2(-a, -hh), new Vec2(a, -hh), new Vec2(hw, -a), new Vec2(hw, a),
                        new Vec2(a, hh), new Vec2(-a, hh), new Vec2(-hw, a), new Vec2(-hw, -a),
                    };
                    return InsetAndRound(poly, inset, radius, arcSegments);
                }

                case "triangle": // point down (YIELD)
                    return InsetAndRound(new[] { new Vec2(0, -hh), new Vec2(hw, hh), new Vec2(-hw, hh) }, inset, radius, arcSegments);

                case "triangle-up":
                    return InsetAndRound(new[] { new Vec2(-hw, -hh), new Vec2(hw, -hh), new Vec2(0, hh) }, inset, radius, arcSegments);

                case "pentagon": // school sign: vertical sides up to mid height, pointed roof
                {
                    float shoulder = 0f; // y of the top of the vertical sides
                    var poly = new[] { new Vec2(-hw, -hh), new Vec2(hw, -hh), new Vec2(hw, shoulder), new Vec2(0, hh), new Vec2(-hw, shoulder) };
                    return InsetAndRound(poly, inset, radius, arcSegments);
                }

                case "pennant": // pointing right (NO PASSING ZONE)
                    return InsetAndRound(new[] { new Vec2(-hw, -hh), new Vec2(hw, 0), new Vec2(-hw, hh) }, inset, radius, arcSegments);

                case "circle":
                {
                    float r = Math.Max(0.01f, hw - inset);
                    int segments = 48;
                    var list = new List<Vec2>(segments);
                    for (int i = 0; i < segments; i++)
                    {
                        float ang = 2f * MathUtil.Pi * i / segments;
                        list.Add(new Vec2((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r));
                    }
                    return list;
                }

                case "crossbuck":
                    return Crossbuck(face.Width, face.Height, inset);

                case "shield-interstate":
                    return InterstateShield(size.X - inset * 2f, size.Y - inset * 2f);

                default: // rect
                    return InsetAndRound(new[] { new Vec2(-hw, -hh), new Vec2(hw, -hh), new Vec2(hw, hh), new Vec2(-hw, hh) }, inset, radius, arcSegments);
            }
        }

        public static string Normalize(string shape) => string.IsNullOrEmpty(shape) ? "rect" : shape.Trim().ToLowerInvariant();

        /// <summary>Offsets a convex counter-clockwise polygon inward and fillets every corner.</summary>
        public static List<Vec2> InsetAndRound(IReadOnlyList<Vec2> polygon, float inset, float radius, int arcSegments)
        {
            int n = polygon.Count;
            var offset = new Vec2[n];
            for (int i = 0; i < n; i++)
            {
                Vec2 prev = polygon[(i + n - 1) % n];
                Vec2 cur = polygon[i];
                Vec2 next = polygon[(i + 1) % n];
                Vec2 eIn = (cur - prev).Normalized;
                Vec2 eOut = (next - cur).Normalized;
                // Inward normals of a CCW polygon point to the left of each edge.
                Vec2 nIn = eIn.PerpLeft;
                Vec2 nOut = eOut.PerpLeft;
                offset[i] = IntersectLines(prev + nIn * inset, eIn, cur + nOut * inset, eOut, cur + (nIn + nOut).Normalized * inset);
            }

            var result = new List<Vec2>();
            if (radius <= 1e-4f)
            {
                result.AddRange(offset);
                return result;
            }

            for (int i = 0; i < n; i++)
            {
                Vec2 prev = offset[(i + n - 1) % n];
                Vec2 cur = offset[i];
                Vec2 next = offset[(i + 1) % n];
                Vec2 toPrev = prev - cur;
                Vec2 toNext = next - cur;
                float lenPrev = toPrev.Length;
                float lenNext = toNext.Length;
                Vec2 dPrev = toPrev / lenPrev;
                Vec2 dNext = toNext / lenNext;
                float cosAlpha = MathUtil.Clamp(Vec2.Dot(dPrev, dNext), -1f, 1f);
                float alpha = (float)Math.Acos(cosAlpha); // interior angle
                float halfTan = (float)Math.Tan(alpha * 0.5f);
                if (halfTan < 1e-4f)
                {
                    result.Add(cur);
                    continue;
                }
                float t = radius / halfTan;
                float tMax = Math.Min(lenPrev, lenNext) * 0.5f;
                float r = radius;
                if (t > tMax)
                {
                    t = tMax;
                    r = t * halfTan;
                }
                Vec2 start = cur + dPrev * t;
                Vec2 end = cur + dNext * t;
                Vec2 bisector = (dPrev + dNext).Normalized;
                Vec2 center = cur + bisector * (r / (float)Math.Sin(alpha * 0.5f));
                float sweep = MathUtil.Pi - alpha;
                int segments = Math.Max(1, (int)Math.Round(arcSegments * sweep / (MathUtil.Pi * 0.5f)));
                float a0 = (float)Math.Atan2(start.Y - center.Y, start.X - center.X);
                for (int k = 0; k <= segments; k++)
                {
                    float ang = a0 + sweep * k / segments;
                    result.Add(new Vec2(center.X + (float)Math.Cos(ang) * r, center.Y + (float)Math.Sin(ang) * r));
                }
            }
            return result;
        }

        private static Vec2 IntersectLines(Vec2 p0, Vec2 d0, Vec2 p1, Vec2 d1, Vec2 fallback)
        {
            float denom = Vec2.Cross(d0, d1);
            if (Math.Abs(denom) < 1e-6f)
                return fallback;
            float t = Vec2.Cross(p1 - p0, d1) / denom;
            return p0 + d0 * t;
        }

        private static List<Vec2> Crossbuck(float bladeLength, float bladeWidth, float inset)
        {
            float l = bladeLength * 0.5f - inset;
            float w = bladeWidth * 0.5f - inset;
            var plus = new[]
            {
                new Vec2(l, -w), new Vec2(l, w), new Vec2(w, w), new Vec2(w, l),
                new Vec2(-w, l), new Vec2(-w, w), new Vec2(-l, w), new Vec2(-l, -w),
                new Vec2(-w, -w), new Vec2(-w, -l), new Vec2(w, -l), new Vec2(w, -w),
            };
            float c = (float)Math.Cos(Math.PI / 4), s = (float)Math.Sin(Math.PI / 4);
            var result = new List<Vec2>(plus.Length);
            foreach (Vec2 p in plus)
                result.Add(new Vec2(p.X * c - p.Y * s, p.X * s + p.Y * c));
            return result;
        }

        /// <summary>Interstate route marker (M1-1) outline, flattened from Bezier curves.</summary>
        public static List<Vec2> InterstateShield(float width, float height)
        {
            // Normalized design (y up), half extents 0.5.
            var pts = new List<Vec2>();
            const int steps = 10;
            Vec2 topLeft = new Vec2(-0.5f, 0.47f);
            Vec2 topRight = new Vec2(0.5f, 0.47f);
            // Right side: from the top-right ear down to the bottom point.
            AddCubic(pts, topRight, new Vec2(0.5f, 0.05f), new Vec2(0.36f, -0.32f), new Vec2(0f, -0.5f), steps * 2, includeStart: true);
            // Left side: from bottom point up to top-left ear.
            AddCubic(pts, new Vec2(0f, -0.5f), new Vec2(-0.36f, -0.32f), new Vec2(-0.5f, 0.05f), topLeft, steps * 2, includeStart: false);
            // Top edge: the crown dips toward the center.
            AddQuadratic(pts, topLeft, new Vec2(-0.25f, 0.53f), new Vec2(0f, 0.41f), steps, includeStart: false);
            AddQuadratic(pts, new Vec2(0f, 0.41f), new Vec2(0.25f, 0.53f), topRight, steps, includeStart: false);
            pts.RemoveAt(pts.Count - 1); // last point duplicates the first

            for (int i = 0; i < pts.Count; i++)
                pts[i] = new Vec2(pts[i].X * width, pts[i].Y * height);
            return MeshPart.ToCounterClockwise(pts);
        }

        private static void AddCubic(List<Vec2> pts, Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, int steps, bool includeStart)
        {
            for (int i = includeStart ? 0 : 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                pts.Add(p0 * (u * u * u) + p1 * (3f * u * u * t) + p2 * (3f * u * t * t) + p3 * (t * t * t));
            }
        }

        private static void AddQuadratic(List<Vec2> pts, Vec2 p0, Vec2 p1, Vec2 p2, int steps, bool includeStart)
        {
            for (int i = includeStart ? 0 : 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                pts.Add(p0 * (u * u) + p1 * (2f * u * t) + p2 * (t * t));
            }
        }
    }
}
