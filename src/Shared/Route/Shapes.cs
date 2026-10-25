using System;
using System.Collections.Generic;
using USRoadSigns.Geometry;

namespace USRoadSigns.Route
{
    /// <summary>Arrow symbols for route marker arrow plates (M5/M6 series) and destination signs (D1 series).</summary>
    public enum ArrowKind
    {
        None = 0,
        Left = 1,
        Right = 2,
        Up = 3,
        UpLeft = 4,
        UpRight = 5,
        /// <summary>Advance turn: straight up, then bending left (M5-1L).</summary>
        TurnLeft = 6,
        /// <summary>Advance turn: straight up, then bending right (M5-1R).</summary>
        TurnRight = 7,
        /// <summary>Two-headed arrow pointing left and right (M6-4).</summary>
        LeftRight = 8,
        /// <summary>Straight arrow with a branch to the left (M6-7L style).</summary>
        UpAndLeft = 9,
        UpAndRight = 10,
    }

    /// <summary>Outlines used on runtime sign faces and plates (inches, centered, +Y up, counter-clockwise).</summary>
    public static class Shapes
    {
        public static List<Vec2> Ellipse(float cx, float cy, float rx, float ry, int segments)
        {
            var list = new List<Vec2>(segments);
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * MathUtil.Pi * i / segments;
                list.Add(new Vec2(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a)));
            }
            return list;
        }

        public static List<Vec2> RoundedRect(float width, float height, float radius, float inset = 0f)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            var rect = new[] { new Vec2(-hw, -hh), new Vec2(hw, -hh), new Vec2(hw, hh), new Vec2(-hw, hh) };
            return ShapeLibrary.InsetAndRound(rect, inset, Math.Max(0f, radius - inset), 6);
        }

        public static List<Vec2> Translate(List<Vec2> points, float dx, float dy)
        {
            var list = new List<Vec2>(points.Count);
            foreach (Vec2 p in points)
                list.Add(new Vec2(p.X + dx, p.Y + dy));
            return list;
        }

        /// <summary>Interstate / Business route marker outline (die-cut shield) inside a width x height box.</summary>
        public static List<Vec2> InterstateShield(float width, float height) => ShieldOutlines.Interstate(width, height);

        /// <summary>The white US route shield printed on the M1-4 plate; width x height is the whole plate.</summary>
        public static List<Vec2> UsShield(float width, float height) => ShieldOutlines.Us(width, height);

        /// <summary>County route marker (M1-6): pentagon with a flat top and cut shoulders.</summary>
        public static List<Vec2> CountyPentagon(float width, float height, float inset = 0f, float radius = 0f)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            var poly = new[]
            {
                new Vec2(-hw, -hh), new Vec2(hw, -hh), new Vec2(hw, hh * 0.42f),
                new Vec2(hw * 0.58f, hh), new Vec2(-hw * 0.58f, hh), new Vec2(-hw, hh * 0.42f),
            };
            return ShapeLibrary.InsetAndRound(poly, inset, radius, 6);
        }

        /// <summary>Forest route marker (M1-7): square shoulders, sides tapering in toward the bottom.</summary>
        public static List<Vec2> ForestMarker(float width, float height, float inset = 0f, float radius = 0f)
        {
            float hw = width * 0.5f, hh = height * 0.5f;
            var poly = new[]
            {
                new Vec2(-hw * 0.74f, -hh), new Vec2(hw * 0.74f, -hh), new Vec2(hw, -hh * 0.08f),
                new Vec2(hw, hh), new Vec2(-hw, hh), new Vec2(-hw, -hh * 0.08f),
            };
            return ShapeLibrary.InsetAndRound(poly, inset, radius, 6);
        }

        /// <summary>Polygon scaled about the origin (inner outlines of die-cut shields).</summary>
        public static List<Vec2> Scale(IReadOnlyList<Vec2> points, float sx, float sy, float dy = 0f)
        {
            var list = new List<Vec2>(points.Count);
            foreach (Vec2 p in points)
                list.Add(new Vec2(p.X * sx, p.Y * sy + dy));
            return list;
        }

        // ------------------------------------------------------------------------------------------
        // Arrows
        // ------------------------------------------------------------------------------------------

        /// <summary>Arrow made of shaft strokes (square ends, round bends) and triangular heads.</summary>
        public sealed class ArrowShape
        {
            public readonly List<Stroke> Shafts = new List<Stroke>();
            public readonly List<List<Vec2>> Heads = new List<List<Vec2>>();
            public float ShaftWidth;
        }

        /// <summary>
        /// Builds an arrow that fits a box of <paramref name="boxWidth"/> x <paramref name="boxHeight"/> centered
        /// on (<paramref name="cx"/>, <paramref name="cy"/>). <paramref name="size"/> is the length of a straight arrow.
        /// Proportions follow the MUTCD guide-sign arrow: head as wide as half the length.
        /// </summary>
        public static ArrowShape Arrow(ArrowKind kind, float cx, float cy, float size)
        {
            var shape = new ArrowShape();
            float sw = size * 0.17f;            // shaft width
            float hw = size * 0.50f;            // head width
            float hl = size * 0.40f;            // head length
            shape.ShaftWidth = sw;
            Vec2 c = new Vec2(cx, cy);
            float half = size * 0.5f;

            void Straight(Vec2 dir)
            {
                Vec2 tail = c - dir * half;
                Vec2 tip = c + dir * half;
                AddShaft(shape, tail, tip - dir * (hl * 0.9f));
                shape.Heads.Add(Head(tip, dir, hw, hl));
            }

            switch (kind)
            {
                case ArrowKind.Left:
                    Straight(new Vec2(-1f, 0f));
                    break;
                case ArrowKind.Right:
                    Straight(new Vec2(1f, 0f));
                    break;
                case ArrowKind.Up:
                    Straight(new Vec2(0f, 1f));
                    break;
                case ArrowKind.UpLeft:
                    Straight(new Vec2(-0.70710678f, 0.70710678f));
                    break;
                case ArrowKind.UpRight:
                    Straight(new Vec2(0.70710678f, 0.70710678f));
                    break;
                case ArrowKind.LeftRight:
                {
                    float len = size * 1.25f;
                    Vec2 l = c + new Vec2(-len * 0.5f, 0f), r = c + new Vec2(len * 0.5f, 0f);
                    AddShaft(shape, l + new Vec2(hl * 0.9f, 0f), r - new Vec2(hl * 0.9f, 0f));
                    shape.Heads.Add(Head(l, new Vec2(-1f, 0f), hw, hl));
                    shape.Heads.Add(Head(r, new Vec2(1f, 0f), hw, hl));
                    break;
                }
                case ArrowKind.TurnLeft:
                case ArrowKind.TurnRight:
                {
                    // Shaft rises from the bottom, bends 90 degrees and ends in a sideways head.
                    float side = kind == ArrowKind.TurnRight ? 1f : -1f;
                    float w = size * 1.05f, h = size * 0.95f;
                    float r = sw * 1.3f;
                    Vec2 bottom = c + new Vec2(-side * w * 0.36f, -h * 0.5f);
                    float bendY = c.Y + h * 0.5f - hw * 0.5f;
                    var stroke = new Stroke();
                    stroke.Points.Add(bottom);
                    Vec2 arcCenter = new Vec2(bottom.X + side * r, bendY - r);
                    for (int i = 0; i <= 8; i++)
                    {
                        float a = MathUtil.Pi * (side > 0f ? 1f - 0.5f * i / 8f : 0.5f * i / 8f);
                        stroke.Points.Add(new Vec2(arcCenter.X + r * (float)Math.Cos(a), arcCenter.Y + r * (float)Math.Sin(a)));
                    }
                    Vec2 tip = new Vec2(c.X + side * w * 0.5f, bendY);
                    stroke.Points.Add(tip - new Vec2(side * hl * 0.9f, 0f));
                    shape.Shafts.Add(stroke);
                    shape.Heads.Add(Head(tip, new Vec2(side, 0f), hw, hl));
                    break;
                }
                case ArrowKind.UpAndLeft:
                case ArrowKind.UpAndRight:
                {
                    // Straight-ahead arrow with a branch splitting off sideways near the base.
                    float side = kind == ArrowKind.UpAndRight ? 1f : -1f;
                    float w = size * 1.05f;
                    Vec2 stemX = new Vec2(c.X - side * w * 0.24f, 0f);
                    Vec2 tail = new Vec2(stemX.X, c.Y - half);
                    Vec2 tipUp = new Vec2(stemX.X, c.Y + half);
                    AddShaft(shape, tail, tipUp - new Vec2(0f, hl * 0.9f));
                    shape.Heads.Add(Head(tipUp, new Vec2(0f, 1f), hw, hl));
                    float branchY = c.Y - half * 0.15f;
                    Vec2 tipSide = new Vec2(c.X + side * w * 0.5f, branchY);
                    AddShaft(shape, new Vec2(stemX.X, branchY), tipSide - new Vec2(side * hl * 0.9f, 0f));
                    shape.Heads.Add(Head(tipSide, new Vec2(side, 0f), hw, hl));
                    break;
                }
            }
            return shape;
        }

        /// <summary>Box an arrow of <paramref name="size"/> needs (width, height), for layout.</summary>
        public static Vec2 ArrowExtent(ArrowKind kind, float size)
        {
            switch (kind)
            {
                case ArrowKind.None:
                    return new Vec2(0f, 0f);
                case ArrowKind.Left:
                case ArrowKind.Right:
                    return new Vec2(size, size * 0.5f);
                case ArrowKind.Up:
                    return new Vec2(size * 0.5f, size);
                case ArrowKind.LeftRight:
                    return new Vec2(size * 1.25f, size * 0.5f);
                case ArrowKind.UpLeft:
                case ArrowKind.UpRight:
                    return new Vec2(size * 0.85f, size * 0.85f);
                default:
                    return new Vec2(size * 1.05f, size * 0.95f);
            }
        }

        private static void AddShaft(ArrowShape shape, Vec2 from, Vec2 to)
        {
            var stroke = new Stroke();
            stroke.Points.Add(from);
            stroke.Points.Add(to);
            shape.Shafts.Add(stroke);
        }

        private static List<Vec2> Head(Vec2 tip, Vec2 dir, float width, float length)
        {
            Vec2 back = tip - dir * length;
            Vec2 side = dir.PerpLeft * (width * 0.5f);
            return MeshPart.ToCounterClockwise(new List<Vec2> { tip, back + side, back - side });
        }
    }
}
