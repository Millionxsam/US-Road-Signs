using System;
using System.Collections.Generic;
using USRoadSigns.Catalog;
using USRoadSigns.Geometry;

namespace USRoadSigns.Route
{
    /// <summary>Fixed geometry of a route sign type: where the plates go and what holds them (inches).</summary>
    public sealed class RouteFrame
    {
        public float Size;
        /// <summary>Column centers, viewer-right positive; column 0 is the viewer's leftmost.</summary>
        public float[] ColumnX;
        public float AuxY, CardinalY, ShieldY, ArrowY;
        public float PlateBack;
        public bool Braced;
        public bool AllowWide;
        public float[] PostX;
        public float PostSize;
        public float Top;
        /// <summary>Front face of the posts, where braces are clamped.</summary>
        public float PostFront;
    }

    /// <summary>A printed plate placed on the sign (inches; bolts and LEDs relative to the plate center).</summary>
    public sealed class PlacedPlate
    {
        public PlateFace Face;
        public float X;
        public float Y;
        public float BackZ;
        public readonly List<Vec2> Bolts = new List<Vec2>();
        public readonly List<Vec2> Leds = new List<Vec2>();

        /// <summary>Cache key of the plate mesh (outline, hardware and LEDs, not the artwork).</summary>
        public string MeshKey;
    }

    /// <summary>Horizontal steel channel brace behind plates (inches).</summary>
    public struct BraceBar
    {
        public float Left, Right, Y, Z, Size;
    }

    public sealed class RouteSignLayout
    {
        public readonly List<PlacedPlate> Plates = new List<PlacedPlate>();
        public readonly List<BraceBar> Braces = new List<BraceBar>();
    }

    /// <summary>
    /// One mesh the renderer draws for a placed sign, on top of the prefab: a plate (with its artwork), a brace,
    /// or a destination sign face. Position is the object-space offset (meters), Scale applies to the mesh.
    /// </summary>
    public sealed class RouteDrawItem
    {
        public string MeshKey;
        public Func<List<MeshPart>> BuildMesh;
        public Vec3 Position;
        public Vec3 Scale = new Vec3(1f, 1f, 1f);
        /// <summary>Artwork of the Face part; null for plain steel parts.</summary>
        public PlateFace Face;
    }

    /// <summary>Static part of a route sign prefab, plus where a destination sign's face is drawn.</summary>
    public sealed class RouteStructureMesh
    {
        public AssemblyMesh Assembly;
        /// <summary>Destination signs: the face plate (center, front plane), inches.</summary>
        public float FaceX, FaceY, FaceFrontZ;
    }

    /// <summary>
    /// Geometry of route marker assemblies and destination signs. Object space as in <see cref="AssemblyBuilder"/>:
    /// origin on the ground, +Y up, signs face +Z, viewer-right inches s map to object X = -s.
    /// Route marker plates change shape with the marker chosen, so they are built per sign at runtime
    /// (<see cref="BuildPlateMesh"/>); the prefab holds the posts, the shield braces and a thin plate inside
    /// every marker that gives the sign something to click on.
    /// </summary>
    public static class RouteSignBuilder
    {
        public const float K = Units.InchesToMeters;
        public const float PlateThickness = 0.125f;
        private const float GalvanizedUvPerMeter = 3.3f;
        private const float AluminumUvPerMeter = 1.6f;
        private const float MountHeight = 84f;
        private const float PostBurial = 6f;
        private const float BraceSize = 1.625f;

        // ------------------------------------------------------------------------------------------
        // Frames
        // ------------------------------------------------------------------------------------------

        public static RouteFrame Frame(RouteSignType type)
        {
            float s = type.ShieldSize;
            float k = s / 24f;
            float gap = 1f * k;
            var f = new RouteFrame { Size = s };

            float y = MountHeight;
            if (type.HasArrow)
            {
                float h = RouteFaces.PlateHeight(PlateRole.Arrow, s);
                f.ArrowY = y + h * 0.5f;
                y += h + gap;
            }
            f.ShieldY = y + s * 0.5f;
            y += s + gap;
            f.CardinalY = y + s * 0.25f;
            y += s * 0.5f;
            if (type.HasAux)
            {
                y += gap;
                f.AuxY = y + s * 0.25f;
                y += s * 0.5f;
            }
            f.Top = y;

            switch (type.Structure)
            {
                case RouteStructure.CenterPost:
                {
                    float pitch = s + 4f * k;
                    f.ColumnX = new[] { -pitch * 0.5f, pitch * 0.5f };
                    f.PostX = new[] { 0f };
                    f.PostSize = 2.5f;
                    f.Braced = true;
                    break;
                }
                case RouteStructure.TwinPostBraced:
                {
                    float pitch = s + 3f * k;
                    f.ColumnX = new[] { -pitch, 0f, pitch };
                    f.PostX = new[] { -pitch * 0.5f, pitch * 0.5f };
                    f.PostSize = 2.5f;
                    f.Braced = true;
                    break;
                }
                case RouteStructure.TwinPost:
                    f.ColumnX = new[] { 0f };
                    f.PostX = new[] { -(s * 0.5f - 7f * k), s * 0.5f - 7f * k };
                    f.PostSize = 2.5f;
                    f.AllowWide = true;
                    break;
                default:
                    f.ColumnX = new[] { 0f };
                    f.PostX = new[] { 0f };
                    f.PostSize = 2f;
                    f.AllowWide = true;
                    break;
            }
            f.PostFront = f.PostSize * 0.5f;
            f.PlateBack = f.Braced ? f.PostFront + BraceSize + 0.02f : f.PostFront + 0.02f;
            return f;
        }

        // ------------------------------------------------------------------------------------------
        // Route marker layout (per sign)
        // ------------------------------------------------------------------------------------------

        public static RouteSignLayout Layout(RouteSignType type, RouteSignState state, bool flashing, FlashingDefaults flash)
        {
            var layout = new RouteSignLayout();
            if (type.Kind != RouteSignKind.Marker)
                return layout;
            RouteFrame f = Frame(type);
            float k = f.Size / 24f;

            var rows = new Dictionary<PlateRole, List<PlacedPlate>>();
            for (int i = 0; i < f.ColumnX.Length && i < state.Columns.Length; i++)
            {
                RouteColumn column = state.Columns[i];
                PlateFace shield = RouteFaces.Shield(column, f.Size, f.AllowWide);
                float width = shield.Width;
                Add(layout, rows, f, shield, f.ColumnX[i], f.ShieldY, flashing, flash);
                if (type.HasAux)
                    Add(layout, rows, f, RouteFaces.Aux(column, state.Plates, f.Size, width), f.ColumnX[i], f.AuxY, false, flash);
                Add(layout, rows, f, RouteFaces.Cardinal(column, state.Plates, f.Size, width), f.ColumnX[i], f.CardinalY, false, flash);
                if (type.HasArrow)
                    Add(layout, rows, f, RouteFaces.ArrowPlate(column, state.Plates, f.Size), f.ColumnX[i], f.ArrowY, false, flash);
            }

            // Braces for the plates above and below the markers (the marker row's braces are part of the prefab).
            if (f.Braced)
            {
                foreach (KeyValuePair<PlateRole, List<PlacedPlate>> row in rows)
                {
                    if (row.Key == PlateRole.Shield || row.Value.Count == 0)
                        continue;
                    float left = float.MaxValue, right = float.MinValue;
                    foreach (PlacedPlate p in row.Value)
                    {
                        left = Math.Min(left, p.X - p.Face.Width * 0.5f + 1f * k);
                        right = Math.Max(right, p.X + p.Face.Width * 0.5f - 1f * k);
                    }
                    ReachPost(f, ref left, ref right);
                    layout.Braces.Add(new BraceBar { Left = left, Right = right, Y = row.Value[0].Y, Z = f.PostFront + BraceSize * 0.5f, Size = BraceSize });
                }
            }

            foreach (PlacedPlate plate in layout.Plates)
                plate.MeshKey = MeshKey(plate);
            return layout;
        }

        private static void Add(RouteSignLayout layout, Dictionary<PlateRole, List<PlacedPlate>> rows, RouteFrame f, PlateFace face, float x, float y, bool lit, FlashingDefaults flash)
        {
            if (face == null)
                return;
            float k = f.Size / 24f;
            var plate = new PlacedPlate { Face = face, X = x, Y = y, BackZ = f.PlateBack };
            float hw = face.Width * 0.5f, hh = face.Height * 0.5f;
            bool shield = face.Role == PlateRole.Shield;

            if (f.Braced)
            {
                float[] rowsY = shield ? ShieldBraceRows(f) : new[] { 0f };
                foreach (float row in rowsY)
                {
                    plate.Bolts.Add(new Vec2(-hw + 2f * k, row));
                    plate.Bolts.Add(new Vec2(hw - 2f * k, row));
                }
            }
            else
            {
                float[] rowsY = shield ? new[] { hh * 0.76f, -hh * 0.5f } : new[] { hh - 1.25f * k, -hh + 1.25f * k };
                foreach (float postX in f.PostX)
                {
                    foreach (float row in rowsY)
                        plate.Bolts.Add(new Vec2(postX - x, row));
                }
            }

            if (lit)
                plate.Leds.AddRange(LedPositions(face, Math.Min(flash.MaxSpacing, f.Size / 3f), flash.LampDiameter));

            layout.Plates.Add(plate);
            if (!rows.TryGetValue(face.Role, out List<PlacedPlate> list))
                rows[face.Role] = list = new List<PlacedPlate>();
            list.Add(plate);
        }

        private static float[] ShieldBraceRows(RouteFrame f) => new[] { f.Size * 0.28f, -f.Size * 0.22f };

        /// <summary>A brace that does not pass a post is stretched to the nearest one.</summary>
        private static void ReachPost(RouteFrame f, ref float left, ref float right)
        {
            float nearest = f.PostX[0];
            foreach (float px in f.PostX)
            {
                if (px >= left && px <= right)
                    return;
                if (Math.Abs(px - (left + right) * 0.5f) < Math.Abs(nearest - (left + right) * 0.5f))
                    nearest = px;
            }
            left = Math.Min(left, nearest - 1.5f);
            right = Math.Max(right, nearest + 1.5f);
        }

        private static string MeshKey(PlacedPlate plate)
        {
            var sb = new System.Text.StringBuilder(64);
            PlateFace face = plate.Face;
            sb.Append((int)face.Role).Append('|').Append(face.Key.Split('|')[0]).Append('|');
            // The outline depends on the role and, for markers, the marker kind and size.
            if (face.Role == PlateRole.Shield)
                sb.Append(face.Key.Split('|')[1]).Append('|');
            sb.Append(Fmt(face.Width)).Append('x').Append(Fmt(face.Height));
            foreach (Vec2 b in plate.Bolts)
                sb.Append('b').Append(Fmt(b.X)).Append(',').Append(Fmt(b.Y));
            sb.Append('l').Append(plate.Leds.Count);
            return sb.ToString();
        }

        private static string Fmt(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Everything drawn at runtime for one sign showing <paramref name="state"/>.</summary>
        public static List<RouteDrawItem> DrawItems(RouteSignType type, RouteSignState state, bool flashing, FlashingDefaults flash, RouteStructureMesh structure)
        {
            var items = new List<RouteDrawItem>();
            float lamp = flash.LampDiameter;
            if (type.Kind == RouteSignKind.Destination)
            {
                PlateFace face = RouteFaces.Destination(type, state);
                items.Add(new RouteDrawItem
                {
                    MeshKey = "G|" + type.Id,
                    BuildMesh = () => BuildPlateMesh(face, Array.Empty<Vec2>(), Array.Empty<Vec2>(), lamp, faceOnly: true, frontOffset: 0.03f),
                    Position = new Vec3(-structure.FaceX * K, structure.FaceY * K, (structure.FaceFrontZ - PlateThickness) * K),
                    Face = face,
                });
                return items;
            }

            RouteSignLayout layout = Layout(type, state, flashing, flash);
            foreach (PlacedPlate plate in layout.Plates)
            {
                PlacedPlate p = plate;
                items.Add(new RouteDrawItem
                {
                    MeshKey = p.MeshKey,
                    BuildMesh = () => BuildPlateMesh(p.Face, p.Bolts, p.Leds, lamp),
                    Position = new Vec3(-p.X * K, p.Y * K, p.BackZ * K),
                    Face = p.Face,
                });
            }
            foreach (BraceBar brace in layout.Braces)
            {
                items.Add(new RouteDrawItem
                {
                    MeshKey = "Box",
                    BuildMesh = () => new List<MeshPart> { UnitBox() },
                    Position = new Vec3(-(brace.Left + brace.Right) * 0.5f * K, brace.Y * K, brace.Z * K),
                    Scale = new Vec3((brace.Right - brace.Left) * K, brace.Size * K, brace.Size * K),
                });
            }
            return items;
        }

        // ------------------------------------------------------------------------------------------
        // Flashing LED positions
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// LED centers on a plate's border band: corners and evenly spaced points on straight-edged plates,
        /// evenly spaced points along curved marker outlines.
        /// </summary>
        public static List<Vec2> LedPositions(PlateFace face, float spacing, float lampDiameter)
        {
            var result = new List<Vec2>();
            float inset = Math.Max(face.LedInset, lampDiameter * 0.5f + 0.45f);
            if (face.Corners != null)
            {
                List<Vec2> poly = ShapeLibrary.InsetAndRound(face.Corners, inset, 0f, 1);
                // Pull corner LEDs in along the bisector so the lens clears the plate's rounded corner.
                var corners = new List<Vec2>(poly.Count);
                for (int i = 0; i < poly.Count; i++)
                {
                    Vec2 prev = poly[(i + poly.Count - 1) % poly.Count], cur = poly[i], next = poly[(i + 1) % poly.Count];
                    Vec2 bisector = ((prev - cur).Normalized + (next - cur).Normalized).Normalized;
                    corners.Add(cur + bisector * (lampDiameter * 0.6f));
                }
                for (int i = 0; i < corners.Count; i++)
                {
                    Vec2 a = corners[i], b = corners[(i + 1) % corners.Count];
                    int segments = Math.Max(1, (int)Math.Ceiling((b - a).Length / spacing - 0.05f));
                    for (int s = 0; s < segments; s++)
                        result.Add(a + (b - a) * ((float)s / segments));
                }
                return result;
            }

            List<Vec2> outline = face.Outline;
            float perimeter = 0f;
            for (int i = 0; i < outline.Count; i++)
                perimeter += (outline[(i + 1) % outline.Count] - outline[i]).Length;
            int count = Math.Max(8, (int)Math.Ceiling(perimeter / spacing));
            count = (count + 1) / 2 * 2;   // keep the pattern symmetric
            float step = perimeter / count;
            // Start at the bottom point of the shield (the outline's lowest vertex) so LEDs sit symmetrically.
            int start = 0;
            for (int i = 1; i < outline.Count; i++)
            {
                if (outline[i].Y < outline[start].Y)
                    start = i;
            }
            float target = 0f, walked = 0f;
            for (int n = 0; n < outline.Count && result.Count < count; n++)
            {
                Vec2 a = outline[(start + n) % outline.Count], b = outline[(start + n + 1) % outline.Count];
                float len = (b - a).Length;
                while (target <= walked + len && result.Count < count)
                {
                    Vec2 p = a + (b - a) * ((target - walked) / Math.Max(1e-5f, len));
                    Vec2 inward = (b - a).Normalized.PerpLeft;   // counter-clockwise outline: left is inside
                    result.Add(p + inward * inset);
                    target += step;
                }
                walked += len;
            }
            return result;
        }

        // ------------------------------------------------------------------------------------------
        // Plate meshes (per sign, at runtime)
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// One plate: printed front (Face part, UV 0-1 over the plate's box), aluminum back and edges, bolt heads,
        /// LED bezels (DarkPlastic) and lenses (Lamp). Origin at the plate center on its back plane.
        /// <paramref name="faceOnly"/> builds just the printed front (destination signs, whose plate is in the prefab).
        /// </summary>
        public static List<MeshPart> BuildPlateMesh(PlateFace face, IReadOnlyList<Vec2> bolts, IReadOnlyList<Vec2> leds, float lampDiameter, bool faceOnly = false, float frontOffset = 0f)
        {
            var facePart = new MeshPart(MaterialSlot.Face, face.Key);
            var aluminum = new MeshPart(MaterialSlot.Aluminum);
            var galvanized = new MeshPart(MaterialSlot.Galvanized);
            var dark = new MeshPart(MaterialSlot.DarkPlastic);
            var lamp = new MeshPart(MaterialSlot.Lamp);
            float w = face.Width, h = face.Height;
            float front = PlateThickness + frontOffset;

            facePart.AddPolygon(face.Outline, new Vec3(0f, 0f, front * K), new Vec3(-K, 0f, 0f), new Vec3(0f, K, 0f), Vec3.Forward,
                p => new Vec2(0.5f + p.X / w, 0.5f + p.Y / h));
            if (!faceOnly)
            {
                AddPlateBody(aluminum, face.Outline, 0f, 0f, 0f, PlateThickness, includeFront: false);
                foreach (Vec2 b in bolts)
                    AddBolt(galvanized, b.X, b.Y, PlateThickness);
            }
            foreach (Vec2 p in leds)
                AddLed(dark, lamp, p.X, p.Y, front, lampDiameter);
            return new List<MeshPart> { facePart, aluminum, galvanized, dark, lamp };
        }

        /// <summary>Plate slab from its outline: back, edges and (optionally) an unprinted front.</summary>
        private static void AddPlateBody(MeshPart part, List<Vec2> outline, float cx, float cy, float zBack, float thickness, bool includeFront)
        {
            float zFront = zBack + thickness;
            Vec3 Obj(float s, float t, float z) => new Vec3(-(cx + s) * K, (cy + t) * K, z * K);
            part.AddPolygon(outline, Obj(0f, 0f, zBack), new Vec3(-K, 0f, 0f), new Vec3(0f, K, 0f), Vec3.Back,
                p => new Vec2(p.X * K * AluminumUvPerMeter, p.Y * K * AluminumUvPerMeter));
            if (includeFront)
            {
                part.AddPolygon(outline, Obj(0f, 0f, zFront), new Vec3(-K, 0f, 0f), new Vec3(0f, K, 0f), Vec3.Forward,
                    p => new Vec2(p.X * K * AluminumUvPerMeter, p.Y * K * AluminumUvPerMeter));
            }
            float area = MeshPart.SignedArea(outline);
            float perimeter = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 a = outline[i], b = outline[(i + 1) % outline.Count];
                Vec2 e = b - a;
                float len = e.Length;
                if (len < 1e-5f)
                    continue;
                Vec2 outward = area > 0f ? new Vec2(e.Y, -e.X) / len : new Vec2(-e.Y, e.X) / len;
                Vec3 normal = new Vec3(-outward.X, outward.Y, 0f);
                float u0 = perimeter * K * AluminumUvPerMeter, u1 = (perimeter + len) * K * AluminumUvPerMeter, v1 = thickness * K * AluminumUvPerMeter;
                part.AddQuad(Obj(a.X, a.Y, zFront), Obj(b.X, b.Y, zFront), Obj(b.X, b.Y, zBack), Obj(a.X, a.Y, zBack),
                    normal, new Vec2(u0, v1), new Vec2(u1, v1), new Vec2(u1, 0f), new Vec2(u0, 0f));
                perimeter += len;
            }
        }

        private static void AddBolt(MeshPart galvanized, float s, float t, float zFace)
        {
            Vec3 basePoint = new Vec3(-s * K, t * K, zFace * K);
            galvanized.AddCylinder(basePoint, Vec3.Forward, 0.06f * K, 0.5f * K, 8, capStart: false, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
            galvanized.AddCylinder(basePoint + new Vec3(0f, 0f, 0.06f * K), Vec3.Forward, 0.16f * K, 0.32f * K, 8, capStart: false, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
        }

        /// <summary>LED unit like the classic flashing signs: black bezel ring around a domed lens.</summary>
        private static void AddLed(MeshPart dark, MeshPart lamp, float s, float t, float zFace, float lampDiameter)
        {
            float lensRadius = lampDiameter * 0.5f;
            float bezelRadius = lensRadius + 0.22f;
            const float bezelDepth = 0.22f;
            const float lensDepth = 0.12f;
            Vec3 basePoint = new Vec3(-s * K, t * K, zFace * K);
            dark.AddCylinder(basePoint, Vec3.Forward, bezelDepth * K, bezelRadius * K, 12, capStart: false, capEnd: true, smooth: true);
            Vec3 lensBase = basePoint + new Vec3(0f, 0f, bezelDepth * K);
            lamp.AddCylinder(lensBase, Vec3.Forward, lensDepth * K, lensRadius * K, 12, capStart: false, capEnd: true, smooth: true);
            lamp.AddCylinder(lensBase + new Vec3(0f, 0f, lensDepth * K), Vec3.Forward, 0.08f * K, lensRadius * 0.7f * K, 12, capStart: false, capEnd: true, smooth: true);
        }

        /// <summary>Unit cube (1 m, centered) for braces, scaled per brace by the renderer.</summary>
        public static MeshPart UnitBox()
        {
            var part = new MeshPart(MaterialSlot.Galvanized);
            part.AddBox(Vec3.Zero, new Vec3(1f, 1f, 1f), YawTransform.Identity, 1f);
            return part;
        }

        // ------------------------------------------------------------------------------------------
        // Prefab structures
        // ------------------------------------------------------------------------------------------

        private sealed class Parts
        {
            public readonly MeshPart Galvanized = new MeshPart(MaterialSlot.Galvanized);
            public readonly MeshPart Perforated = new MeshPart(MaterialSlot.Perforated);
            public readonly MeshPart Aluminum = new MeshPart(MaterialSlot.Aluminum);
            public readonly MeshPart Dark = new MeshPart(MaterialSlot.DarkPlastic);
            public readonly MeshPart Lamp = new MeshPart(MaterialSlot.Lamp);
            public readonly AssemblyMesh Result = new AssemblyMesh();

            public IEnumerable<MeshPart> All => new[] { Galvanized, Perforated, Aluminum, Dark, Lamp };
        }

        public static RouteStructureMesh BuildStructure(RouteSignType type, bool flashing, FlashingDefaults flash, bool bolts)
        {
            var parts = new Parts();
            var result = new RouteStructureMesh { Assembly = parts.Result };
            if (type.Kind == RouteSignKind.Marker)
                BuildMarkerStructure(parts, type);
            else
                BuildGuideStructure(parts, type, result, flashing, flash, bolts);
            Finish(parts);
            return result;
        }

        private static void BuildMarkerStructure(Parts parts, RouteSignType type)
        {
            RouteFrame f = Frame(type);
            float lowest = type.HasArrow ? f.ArrowY - RouteFaces.PlateHeight(PlateRole.Arrow, f.Size) * 0.5f : f.ShieldY - f.Size * 0.5f;
            foreach (float px in f.PostX)
                AssemblyBuilder.AddSquareTube(parts.Perforated, parts.Galvanized, px, 0f, f.PostSize, -PostBurial, f.Top + 0.5f);

            // Something to click inside every marker plate (hidden between its front and back).
            float zMid = f.PlateBack + PlateThickness * 0.5f;
            foreach (float cx in f.ColumnX)
            {
                float w = f.Size * 0.56f, h = f.Size * 0.5f, cy = f.ShieldY + f.Size * 0.04f;
                Vec3 a = new Vec3(-(cx - w * 0.5f) * K, (cy - h * 0.5f) * K, zMid * K);
                Vec3 b = new Vec3(-(cx + w * 0.5f) * K, (cy - h * 0.5f) * K, zMid * K);
                Vec3 c = new Vec3(-(cx + w * 0.5f) * K, (cy + h * 0.5f) * K, zMid * K);
                Vec3 d = new Vec3(-(cx - w * 0.5f) * K, (cy + h * 0.5f) * K, zMid * K);
                parts.Aluminum.AddQuad(a, b, c, d, Vec3.Forward, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1));
                parts.Aluminum.AddQuad(b, a, d, c, Vec3.Back, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1));
            }

            // The marker row is always there, so its braces belong to the prefab.
            if (f.Braced)
            {
                float k = f.Size / 24f;
                float left = f.ColumnX[0] - f.Size * 0.5f + 1f * k;
                float right = f.ColumnX[f.ColumnX.Length - 1] + f.Size * 0.5f - 1f * k;
                foreach (float row in ShieldBraceRows(f))
                {
                    float y = f.ShieldY + row;
                    parts.Galvanized.AddBox(new Vec3(-(left + right) * 0.5f * K, y * K, (f.PostFront + BraceSize * 0.5f) * K),
                        new Vec3((right - left) * K, BraceSize * K, BraceSize * K), YawTransform.Identity, GalvanizedUvPerMeter);
                    foreach (float px in f.PostX)
                        AddPostClamp(parts, px, y, f.PostSize);
                }
            }

            AssemblyMesh m = parts.Result;
            m.PostCount = f.PostX.Length;
            m.LegSize = new Vec3(f.PostSize * K, lowest * K, f.PostSize * K);
            m.LegGapX = f.PostX.Length > 1 ? (f.PostX[f.PostX.Length - 1] - f.PostX[0] - f.PostSize) * K : 0f;
            m.CircularLeg = false;
            m.PanelArea = f.Size * f.Size * f.ColumnX.Length;
        }

        private static void AddPostClamp(Parts parts, float px, float y, float postSize)
        {
            parts.Galvanized.AddBox(new Vec3(-px * K, y * K, 0f), new Vec3((postSize + 0.3f) * K, 2.2f * K, (postSize + 0.3f) * K), YawTransform.Identity, GalvanizedUvPerMeter);
        }

        private static void BuildGuideStructure(Parts parts, RouteSignType type, RouteStructureMesh result, bool flashing, FlashingDefaults flash, bool bolts)
        {
            float w = type.FaceWidth, h = type.FaceHeight;
            RouteFaces.GuideBorder(type, out float radius, out _, out _);
            List<Vec2> outline = Shapes.RoundedRect(w, h, radius);
            AssemblyMesh m = parts.Result;
            float cx = 0f, cy, plateBack;
            var boltPoints = new List<Vec2>();

            switch (type.Structure)
            {
                case RouteStructure.GuideMastArm:
                {
                    const float poleR = 6f, armR = 3.5f;
                    cy = 212f + h * 0.5f;                       // 17.7 ft clearance under the sign
                    cx = -(66f + w * 0.5f);                      // over the lanes, to the viewer's left of the pole
                    float armY = cy + h * 0.5f + 8f;
                    float armEnd = cx - w * 0.5f - 14f;
                    float poleTop = armY + 18f;
                    plateBack = armR + 2.2f;

                    // Pole on a base plate with anchor nuts, cap on top.
                    parts.Galvanized.AddCylinder(new Vec3(0f, -PostBurial * K, 0f), Vec3.Up, (poleTop + PostBurial) * K, poleR * K, 20, false, true, true, GalvanizedUvPerMeter);
                    parts.Galvanized.AddBox(new Vec3(0f, 0.75f * K, 0f), new Vec3(20f * K, 1.5f * K, 20f * K), YawTransform.Identity, GalvanizedUvPerMeter);
                    foreach (float ax in new[] { -7.5f, 7.5f })
                    {
                        foreach (float az in new[] { -7.5f, 7.5f })
                            parts.Galvanized.AddCylinder(new Vec3(ax * K, 1.5f * K, az * K), Vec3.Up, 1.6f * K, 0.9f * K, 6, false, true, false, GalvanizedUvPerMeter);
                    }
                    parts.Galvanized.AddCylinder(new Vec3(0f, poleTop * K, 0f), Vec3.Up, 1.2f * K, (poleR + 0.6f) * K, 20, false, true, true, GalvanizedUvPerMeter);

                    // Arm: flange plate on the pole, then the tube reaching over the road.
                    parts.Galvanized.AddBox(new Vec3((poleR + 0.6f) * K, armY * K, 0f), new Vec3(1.2f * K, 16f * K, 16f * K), YawTransform.Identity, GalvanizedUvPerMeter);
                    parts.Galvanized.AddCylinder(new Vec3((poleR + 1f) * K, armY * K, 0f), new Vec3(1f, 0f, 0f), (-armEnd - poleR - 1f) * K, armR * K, 16, false, true, true, GalvanizedUvPerMeter);

                    // Two hanger brackets clamp the sign to the arm.
                    foreach (float dx in new[] { -w * 0.3f, w * 0.3f })
                    {
                        float hx = cx + dx;
                        float bottom = cy - h * 0.35f;
                        parts.Galvanized.AddBox(new Vec3(-hx * K, (armY + bottom) * 0.5f * K, (plateBack - 1f) * K), new Vec3(3f * K, (armY - bottom) * K, 2f * K), YawTransform.Identity, GalvanizedUvPerMeter);
                        parts.Galvanized.AddCylinder(new Vec3(-hx * K, armY * K, -armR * K), Vec3.Forward, (plateBack - 0f + armR) * K, 0.6f * K, 8, true, true, true, GalvanizedUvPerMeter);
                        boltPoints.Add(new Vec2(dx, h * 0.3f));
                        boltPoints.Add(new Vec2(dx, -h * 0.3f));
                    }
                    // Z-bar stiffeners across the back.
                    foreach (float row in new[] { h * 0.3f, -h * 0.3f })
                        parts.Aluminum.AddBox(new Vec3(-cx * K, (cy + row) * K, (plateBack - 1f) * K), new Vec3((w - 6f) * K, 2f * K, 2f * K), YawTransform.Identity, AluminumUvPerMeter);

                    m.PostCount = 1;
                    m.LegSize = new Vec3(poleR * 2f * K, (cy - h * 0.5f) * K, poleR * 2f * K);
                    m.CircularLeg = true;
                    break;
                }
                case RouteStructure.GuideIBeam:
                {
                    cy = MountHeight + h * 0.5f;
                    float half = w * 0.3f;
                    const float depth = 5.90f, flange = 3.94f;
                    plateBack = depth * 0.5f + 2f;
                    foreach (float px in new[] { -half, half })
                        AssemblyBuilder.AddIBeam(parts.Galvanized, px, -PostBurial, cy + h * 0.5f - 1f);
                    foreach (float row in new[] { h * 0.3f, -h * 0.3f })
                    {
                        parts.Aluminum.AddBox(new Vec3(0f, (cy + row) * K, (plateBack - 1f) * K), new Vec3((w - 4f) * K, 2f * K, 2f * K), YawTransform.Identity, AluminumUvPerMeter);
                        foreach (float px in new[] { -half, half })
                        {
                            parts.Galvanized.AddBox(new Vec3(-px * K, (cy + row) * K, (depth * 0.5f + 0.2f) * K), new Vec3(5f * K, 3f * K, 0.4f * K), YawTransform.Identity, GalvanizedUvPerMeter);
                            boltPoints.Add(new Vec2(px, row));
                        }
                    }
                    m.PostCount = 2;
                    m.LegSize = new Vec3(flange * K, (cy - h * 0.5f) * K, depth * K);
                    m.LegGapX = (half * 2f - flange) * K;
                    break;
                }
                default:
                {
                    cy = MountHeight + h * 0.5f;
                    float half = Math.Max(9f, w * 0.275f);
                    const float size = 2.5f;
                    plateBack = size * 0.5f + 0.02f;
                    foreach (float px in new[] { -half, half })
                    {
                        AssemblyBuilder.AddSquareTube(parts.Perforated, parts.Galvanized, px, 0f, size, -PostBurial, cy + h * 0.5f - 0.5f);
                        foreach (float row in new[] { h * 0.5f - 1.5f, -h * 0.5f + 1.5f })
                            boltPoints.Add(new Vec2(px, row - 0f));
                    }
                    m.PostCount = 2;
                    m.LegSize = new Vec3(size * K, (cy - h * 0.5f) * K, size * K);
                    m.LegGapX = (half * 2f - size) * K;
                    break;
                }
            }

            // Plate (front left unprinted: the face is drawn at runtime just in front of it).
            AddPlateBody(parts.Aluminum, outline, cx, cy, plateBack, PlateThickness, includeFront: true);
            float faceZ = plateBack + PlateThickness;
            if (bolts)
            {
                foreach (Vec2 b in boltPoints)
                    AddBolt(parts.Galvanized, b.X + (type.Structure == RouteStructure.GuideMastArm ? cx : 0f), cy + b.Y, faceZ);
            }

            if (flashing)
            {
                PlateFace plate = RouteFaces.Destination(type, type.Default());
                foreach (Vec2 p in LedPositions(plate, flash.MaxSpacing, flash.LampDiameter))
                    AddLed(parts.Dark, parts.Lamp, cx + p.X, cy + p.Y, faceZ, flash.LampDiameter);
            }

            result.FaceX = cx;
            result.FaceY = cy;
            result.FaceFrontZ = faceZ;
            m.PanelArea = w * h;
        }

        private static void Finish(Parts parts)
        {
            AssemblyMesh result = parts.Result;
            foreach (MeshPart part in parts.All)
            {
                if (!part.IsEmpty)
                    result.Parts.Add(part);
            }
            var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MeshPart part in result.Parts)
            {
                foreach (Vec3 p in part.Positions)
                {
                    min = Vec3.Min(min, p);
                    max = Vec3.Max(max, p);
                }
                result.SurfaceArea += part.ComputeSurfaceArea();
                result.VertexCount += part.VertexCount;
                result.IndexCount += part.Indices.Count;
            }
            result.BoundsMin = min;
            result.BoundsMax = max;
        }
    }
}
