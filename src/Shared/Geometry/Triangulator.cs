using System;
using System.Collections.Generic;

namespace USRoadSigns.Geometry
{
    /// <summary>Ear-clipping triangulation for simple (possibly concave) polygons without holes.</summary>
    public static class Triangulator
    {
        /// <summary>Returns triangle vertex indices into <paramref name="polygon"/>.</summary>
        public static List<int> Triangulate(IReadOnlyList<Vec2> polygon)
        {
            var result = new List<int>();
            int n = polygon.Count;
            if (n < 3)
                return result;

            var indices = new List<int>(n);
            if (MeshPart.SignedArea(polygon) > 0f)
            {
                for (int i = 0; i < n; i++)
                    indices.Add(i);
            }
            else
            {
                for (int i = n - 1; i >= 0; i--)
                    indices.Add(i);
            }

            int guard = n * n + 10;
            while (indices.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int iPrev = indices[(i + indices.Count - 1) % indices.Count];
                    int iCur = indices[i];
                    int iNext = indices[(i + 1) % indices.Count];
                    Vec2 a = polygon[iPrev];
                    Vec2 b = polygon[iCur];
                    Vec2 c = polygon[iNext];

                    float cross = Vec2.Cross(b - a, c - b);
                    if (cross <= 1e-9f)
                        continue; // reflex or degenerate vertex

                    bool containsOther = false;
                    for (int j = 0; j < indices.Count; j++)
                    {
                        int k = indices[j];
                        if (k == iPrev || k == iCur || k == iNext)
                            continue;
                        if (PointInTriangle(polygon[k], a, b, c))
                        {
                            containsOther = true;
                            break;
                        }
                    }
                    if (containsOther)
                        continue;

                    result.Add(iPrev);
                    result.Add(iCur);
                    result.Add(iNext);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    // Degenerate input (collinear points): drop the flattest vertex and continue.
                    int flattest = 0;
                    float best = float.MaxValue;
                    for (int i = 0; i < indices.Count; i++)
                    {
                        Vec2 a = polygon[indices[(i + indices.Count - 1) % indices.Count]];
                        Vec2 b = polygon[indices[i]];
                        Vec2 c = polygon[indices[(i + 1) % indices.Count]];
                        float cross = Math.Abs(Vec2.Cross(b - a, c - b));
                        if (cross < best)
                        {
                            best = cross;
                            flattest = i;
                        }
                    }
                    indices.RemoveAt(flattest);
                }
            }

            if (indices.Count == 3)
            {
                result.Add(indices[0]);
                result.Add(indices[1]);
                result.Add(indices[2]);
            }
            return result;
        }

        private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
        {
            float d1 = Vec2.Cross(b - a, p - a);
            float d2 = Vec2.Cross(c - b, p - b);
            float d3 = Vec2.Cross(a - c, p - c);
            bool hasNeg = d1 < -1e-9f || d2 < -1e-9f || d3 < -1e-9f;
            bool hasPos = d1 > 1e-9f || d2 > 1e-9f || d3 > 1e-9f;
            return !(hasNeg && hasPos);
        }
    }
}
