using System;
using System.Collections.Generic;

namespace USRoadSigns.Geometry
{
    /// <summary>Material slots shared by every sign. One slot = one shared game material,
    /// except <see cref="Face"/> which uses the per-face texture.</summary>
    public enum MaterialSlot
    {
        /// <summary>Hot-dip galvanized steel (posts, bolts, clamps, brackets).</summary>
        Galvanized = 0,
        /// <summary>Galvanized steel with a 1-inch hole pattern (perforated square tube, U-channel web).</summary>
        Perforated = 1,
        /// <summary>Mill-finish aluminum (sign backs and edges, stiffeners).</summary>
        Aluminum = 2,
        /// <summary>Black rubber / plastic (flexible bases, stand feet).</summary>
        DarkPlastic = 3,
        /// <summary>Printed retroreflective sign face, textured per face id.</summary>
        Face = 4,
    }

    /// <summary>
    /// One sub-mesh of a sign assembly: a vertex/index list rendered with one material.
    /// Coordinate system matches Unity / Cities: Skylines II: left-handed, +Y up, +Z is the object's front.
    /// Triangles are front-facing when cross(b - a, c - a) points along the intended normal.
    /// </summary>
    public sealed class MeshPart
    {
        public readonly MaterialSlot Slot;
        /// <summary>Face id for <see cref="MaterialSlot.Face"/> parts, otherwise null.</summary>
        public readonly string FaceId;

        public readonly List<Vec3> Positions = new List<Vec3>();
        public readonly List<Vec3> Normals = new List<Vec3>();
        public readonly List<Vec2> UVs = new List<Vec2>();
        public readonly List<int> Indices = new List<int>();

        public MeshPart(MaterialSlot slot, string faceId = null)
        {
            Slot = slot;
            FaceId = faceId;
        }

        public int VertexCount => Positions.Count;
        public int TriangleCount => Indices.Count / 3;
        public bool IsEmpty => Indices.Count == 0;

        public int AddVertex(Vec3 position, Vec3 normal, Vec2 uv)
        {
            Positions.Add(position);
            Normals.Add(normal);
            UVs.Add(uv);
            return Positions.Count - 1;
        }

        /// <summary>Adds a triangle, fixing the winding so the front side faces <paramref name="facing"/>.</summary>
        public void AddTriangle(int a, int b, int c, Vec3 facing)
        {
            Vec3 n = Vec3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
            if (Vec3.Dot(n, facing) < 0f)
            {
                int t = b; b = c; c = t;
            }
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        /// <summary>Adds a flat quad (a, b, c, d in perimeter order) with one normal.</summary>
        public void AddQuad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 normal, Vec2 uvA, Vec2 uvB, Vec2 uvC, Vec2 uvD)
        {
            int ia = AddVertex(a, normal, uvA);
            int ib = AddVertex(b, normal, uvB);
            int ic = AddVertex(c, normal, uvC);
            int id = AddVertex(d, normal, uvD);
            AddTriangle(ia, ib, ic, normal);
            AddTriangle(ia, ic, id, normal);
        }

        /// <summary>
        /// Adds a planar polygon. The outline is given in a 2D basis (origin + uAxis * x + vAxis * y).
        /// </summary>
        public void AddPolygon(IReadOnlyList<Vec2> outline, Vec3 origin, Vec3 xAxis, Vec3 yAxis, Vec3 normal, Func<Vec2, Vec2> uv)
        {
            List<int> tris = Triangulator.Triangulate(outline);
            int baseIndex = Positions.Count;
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 p = outline[i];
                AddVertex(origin + xAxis * p.X + yAxis * p.Y, normal, uv(p));
            }
            for (int i = 0; i < tris.Count; i += 3)
                AddTriangle(baseIndex + tris[i], baseIndex + tris[i + 1], baseIndex + tris[i + 2], normal);
        }

        /// <summary>Axis-aligned box (optionally yawed), each side flat shaded. UVs are in meters.</summary>
        public void AddBox(Vec3 center, Vec3 size, YawTransform transform, float uvScale = 1f)
        {
            Vec3 h = size * 0.5f;
            // +X / -X
            AddBoxSide(center, new Vec3(h.X, 0, 0), new Vec3(0, 0, h.Z), new Vec3(0, h.Y, 0), transform, uvScale);
            AddBoxSide(center, new Vec3(-h.X, 0, 0), new Vec3(0, 0, -h.Z), new Vec3(0, h.Y, 0), transform, uvScale);
            // +Y / -Y
            AddBoxSide(center, new Vec3(0, h.Y, 0), new Vec3(h.X, 0, 0), new Vec3(0, 0, h.Z), transform, uvScale);
            AddBoxSide(center, new Vec3(0, -h.Y, 0), new Vec3(h.X, 0, 0), new Vec3(0, 0, -h.Z), transform, uvScale);
            // +Z / -Z
            AddBoxSide(center, new Vec3(0, 0, h.Z), new Vec3(-h.X, 0, 0), new Vec3(0, h.Y, 0), transform, uvScale);
            AddBoxSide(center, new Vec3(0, 0, -h.Z), new Vec3(h.X, 0, 0), new Vec3(0, h.Y, 0), transform, uvScale);
        }

        private void AddBoxSide(Vec3 center, Vec3 toFace, Vec3 halfU, Vec3 halfV, YawTransform t, float uvScale)
        {
            Vec3 c = center + toFace;
            Vec3 n = t.Direction(toFace.Normalized);
            float lu = halfU.Length * 2f * uvScale;
            float lv = halfV.Length * 2f * uvScale;
            AddQuad(
                t.Point(c - halfU - halfV), t.Point(c + halfU - halfV), t.Point(c + halfU + halfV), t.Point(c - halfU + halfV),
                n, new Vec2(0, 0), new Vec2(lu, 0), new Vec2(lu, lv), new Vec2(0, lv));
        }

        /// <summary>
        /// Cylinder along an arbitrary axis. <paramref name="smooth"/> shares normals around the side.
        /// UV u = distance around the circumference (meters) * uvScale, v = distance along the axis * uvScale.
        /// </summary>
        public void AddCylinder(Vec3 start, Vec3 axis, float length, float radius, int segments, bool capStart, bool capEnd, bool smooth = true, float uvScale = 1f)
        {
            Vec3 dir = axis.Normalized;
            Vec3 helper = Math.Abs(dir.Y) < 0.9f ? Vec3.Up : Vec3.Right;
            Vec3 u = Vec3.Cross(helper, dir).Normalized;
            Vec3 v = Vec3.Cross(dir, u).Normalized;
            Vec3 end = start + dir * length;
            float circumference = 2f * MathUtil.Pi * radius;

            for (int i = 0; i < segments; i++)
            {
                float a0 = 2f * MathUtil.Pi * i / segments;
                float a1 = 2f * MathUtil.Pi * (i + 1) / segments;
                Vec3 r0 = u * (float)Math.Cos(a0) + v * (float)Math.Sin(a0);
                Vec3 r1 = u * (float)Math.Cos(a1) + v * (float)Math.Sin(a1);
                Vec3 n0 = smooth ? r0 : (r0 + r1).Normalized;
                Vec3 n1 = smooth ? r1 : n0;
                float u0 = circumference * i / segments * uvScale;
                float u1 = circumference * (i + 1) / segments * uvScale;
                int i0 = AddVertex(start + r0 * radius, n0, new Vec2(u0, 0f));
                int i1 = AddVertex(start + r1 * radius, n1, new Vec2(u1, 0f));
                int i2 = AddVertex(end + r1 * radius, n1, new Vec2(u1, length * uvScale));
                int i3 = AddVertex(end + r0 * radius, n0, new Vec2(u0, length * uvScale));
                Vec3 facing = (r0 + r1).Normalized;
                AddTriangle(i0, i1, i2, facing);
                AddTriangle(i0, i2, i3, facing);
            }

            if (capStart)
                AddDisc(start, -dir, u, v, radius, segments, uvScale);
            if (capEnd)
                AddDisc(end, dir, u, v, radius, segments, uvScale);
        }

        public void AddDisc(Vec3 center, Vec3 normal, Vec3 u, Vec3 v, float radius, int segments, float uvScale = 1f)
        {
            int c = AddVertex(center, normal, new Vec2(radius * uvScale, radius * uvScale));
            int first = Positions.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * MathUtil.Pi * i / segments;
                float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                AddVertex(center + (u * cs + v * sn) * radius, normal, new Vec2((1f + cs) * radius * uvScale, (1f + sn) * radius * uvScale));
            }
            for (int i = 0; i < segments; i++)
                AddTriangle(c, first + i, first + (i + 1) % segments, normal);
        }

        /// <summary>
        /// Extrudes a closed 2D profile (x = object X, y = object Z) vertically from y0 to y1.
        /// Hard edges between profile segments; optional caps. UV u = perimeter distance, v = height, both * uvScale
        /// (or per-segment u from <paramref name="segmentU"/> when given).
        /// </summary>
        public void AddVerticalExtrusion(IReadOnlyList<Vec2> profile, Vec3 origin, float y0, float y1, bool capBottom, bool capTop,
            float uvScale = 1f, Func<int, float, Vec2> segmentU = null, bool[] smoothEdges = null)
        {
            int n = profile.Count;
            float area = SignedArea(profile);
            float perimeter = 0f;
            for (int i = 0; i < n; i++)
            {
                Vec2 p0 = profile[i];
                Vec2 p1 = profile[(i + 1) % n];
                Vec2 e = p1 - p0;
                float len = e.Length;
                if (len < 1e-6f)
                    continue;
                // Outward normal in the X/Z plane for a counter-clockwise (positive area) profile.
                Vec2 outward2 = area > 0f ? new Vec2(e.Y, -e.X) / len : new Vec2(-e.Y, e.X) / len;
                Vec3 normal = new Vec3(outward2.X, 0f, outward2.Y);
                Vec3 a = origin + new Vec3(p0.X, y0, p0.Y);
                Vec3 b = origin + new Vec3(p1.X, y0, p1.Y);
                Vec3 c = origin + new Vec3(p1.X, y1, p1.Y);
                Vec3 d = origin + new Vec3(p0.X, y1, p0.Y);
                Vec2 uvA, uvB, uvC, uvD;
                if (segmentU != null)
                {
                    Vec2 range = segmentU(i, len);
                    uvA = new Vec2(range.X, y0 * uvScale);
                    uvB = new Vec2(range.Y, y0 * uvScale);
                    uvC = new Vec2(range.Y, y1 * uvScale);
                    uvD = new Vec2(range.X, y1 * uvScale);
                }
                else
                {
                    uvA = new Vec2(perimeter * uvScale, y0 * uvScale);
                    uvB = new Vec2((perimeter + len) * uvScale, y0 * uvScale);
                    uvC = new Vec2((perimeter + len) * uvScale, y1 * uvScale);
                    uvD = new Vec2(perimeter * uvScale, y1 * uvScale);
                }
                AddQuad(a, b, c, d, normal, uvA, uvB, uvC, uvD);
                perimeter += len;
            }

            if (capTop)
                AddPolygon(ToCounterClockwise(profile), origin + new Vec3(0f, y1, 0f), Vec3.Right, Vec3.Forward, Vec3.Up, p => p * uvScale);
            if (capBottom)
                AddPolygon(ToCounterClockwise(profile), origin + new Vec3(0f, y0, 0f), Vec3.Right, Vec3.Forward, Vec3.Down, p => p * uvScale);
        }

        public static float SignedArea(IReadOnlyList<Vec2> polygon)
        {
            float a = 0f;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                a += polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
            return a * 0.5f;
        }

        public static List<Vec2> ToCounterClockwise(IReadOnlyList<Vec2> polygon)
        {
            var list = new List<Vec2>(polygon);
            if (SignedArea(list) < 0f)
                list.Reverse();
            return list;
        }

        public float ComputeSurfaceArea()
        {
            double area = 0;
            for (int i = 0; i < Indices.Count; i += 3)
            {
                Vec3 a = Positions[Indices[i]];
                Vec3 b = Positions[Indices[i + 1]];
                Vec3 c = Positions[Indices[i + 2]];
                area += Vec3.Cross(b - a, c - a).Length * 0.5f;
            }
            return (float)area;
        }
    }
}
