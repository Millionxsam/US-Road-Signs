using System;
using System.Collections.Generic;
using USRoadSigns.Catalog;

namespace USRoadSigns.Geometry
{
    /// <summary>Player-configurable options that affect generated geometry.</summary>
    public sealed class AssemblyOptions
    {
        /// <summary>Post used by signs whose support type is "post": square-tube | u-channel | round-pipe.</summary>
        public string DefaultPostStyle = SupportTypes.SquareTube;

        /// <summary>Model bolt heads and washers on sign faces.</summary>
        public bool IncludeBolts = true;
    }

    public static class SupportTypes
    {
        public const string Post = "post";
        public const string SquareTube = "square-tube";
        public const string UChannel = "u-channel";
        public const string RoundPipe = "round-pipe";
        public const string TwinSquare = "twin-square";
        public const string TwinIBeam = "twin-ibeam";
        public const string PortableStand = "portable-stand";
        public const string Barricade = "barricade";
        public const string FlexPost = "flex-post";
    }

    /// <summary>Result of building one placeable sign: sub-meshes plus data for the game prefab.</summary>
    public sealed class AssemblyMesh
    {
        public readonly List<MeshPart> Parts = new List<MeshPart>();
        public Vec3 BoundsMin;
        public Vec3 BoundsMax;
        public float SurfaceArea;
        public int VertexCount;
        public int IndexCount;

        /// <summary>Collision "legs" (meters): width, height, depth of one post.</summary>
        public Vec3 LegSize;
        /// <summary>Gap between two legs along X (meters). 0 = single leg.</summary>
        public float LegGapX;
        public bool CircularLeg;
        /// <summary>Total printed sign area (square meters), used for construction cost.</summary>
        public float PanelArea;
        public int PostCount;
    }

    /// <summary>
    /// Builds a complete sign assembly (panels, fasteners, posts) from catalog data.
    /// Object space: origin on the ground at the post (or between twin posts), +Y up, sign faces toward +Z.
    /// The viewer standing in front of the sign looks toward -Z, so the viewer's right is -X.
    /// </summary>
    public static class AssemblyBuilder
    {
        private const float K = Units.InchesToMeters;

        private const float GalvanizedUvPerMeter = 3.3f;   // one galvanized texture tile every 30 cm
        private const float AluminumUvPerMeter = 1.6f;
        private const float InchesPerMeter = 1f / Units.InchesToMeters;

        private sealed class PanelLayout
        {
            public FaceDef Face;
            public PanelDef Panel;
            public Vec2 Size;          // bounding box, inches
            public float CenterX;      // inches, viewer-right positive
            public float CenterY;      // inches above ground
            public float Top => CenterY + Size.Y * 0.5f;
            public float Bottom => CenterY - Size.Y * 0.5f;
        }

        private sealed class Context
        {
            public SignCatalog Catalog;
            public SignDef Sign;
            public AssemblyOptions Options;
            public MountingDefaults Mounting;
            public List<PanelLayout> Panels = new List<PanelLayout>();
            public MeshPart Galvanized = new MeshPart(MaterialSlot.Galvanized);
            public MeshPart Perforated = new MeshPart(MaterialSlot.Perforated);
            public MeshPart Aluminum = new MeshPart(MaterialSlot.Aluminum);
            public MeshPart Dark = new MeshPart(MaterialSlot.DarkPlastic);
            public Dictionary<string, MeshPart> Faces = new Dictionary<string, MeshPart>(StringComparer.Ordinal);
            public List<string> FaceOrder = new List<string>();
            public AssemblyMesh Result = new AssemblyMesh();

            public MeshPart FacePart(string faceId)
            {
                if (!Faces.TryGetValue(faceId, out MeshPart part))
                {
                    part = new MeshPart(MaterialSlot.Face, faceId);
                    Faces.Add(faceId, part);
                    FaceOrder.Add(faceId);
                }
                return part;
            }
        }

        public static AssemblyMesh Build(SignCatalog catalog, SignDef sign, AssemblyOptions options)
        {
            var ctx = new Context
            {
                Catalog = catalog,
                Sign = sign,
                Options = options ?? new AssemblyOptions(),
                Mounting = catalog.Root.Mounting ?? new MountingDefaults(),
            };

            string support = ResolveSupportType(sign.Support.Type, ctx.Options);
            LayoutPanels(ctx, support);

            switch (support)
            {
                case SupportTypes.UChannel:
                    BuildSinglePost(ctx, PostKind.UChannel);
                    break;
                case SupportTypes.RoundPipe:
                    BuildSinglePost(ctx, PostKind.RoundPipe);
                    break;
                case SupportTypes.TwinSquare:
                    BuildTwinPosts(ctx, ibeam: false);
                    break;
                case SupportTypes.TwinIBeam:
                    BuildTwinPosts(ctx, ibeam: true);
                    break;
                case SupportTypes.PortableStand:
                    BuildPortableStand(ctx);
                    break;
                case SupportTypes.Barricade:
                    BuildBarricade(ctx);
                    break;
                case SupportTypes.FlexPost:
                    BuildFlexPost(ctx);
                    break;
                default:
                    BuildSinglePost(ctx, PostKind.SquareTube);
                    break;
            }

            Finish(ctx);
            return ctx.Result;
        }

        public static string ResolveSupportType(string type, AssemblyOptions options)
        {
            string t = string.IsNullOrEmpty(type) ? SupportTypes.Post : type.Trim().ToLowerInvariant();
            if (t == SupportTypes.Post)
            {
                t = string.IsNullOrEmpty(options.DefaultPostStyle) ? SupportTypes.SquareTube : options.DefaultPostStyle;
            }
            return t;
        }

        // ----------------------------------------------------------------------------------------
        // Layout
        // ----------------------------------------------------------------------------------------

        private static void LayoutPanels(Context ctx, string support)
        {
            float defaultMount = ctx.Mounting.MountHeight;
            if (support == SupportTypes.PortableStand)
                defaultMount = 12f;   // MUTCD 6F.03: portable signs at least 1 ft above the pavement
            else if (support == SupportTypes.FlexPost)
                defaultMount = 4f;
            float mountHeight = ctx.Sign.Support.MountHeight >= 0f ? ctx.Sign.Support.MountHeight : defaultMount;

            float total = 0f;
            for (int i = 0; i < ctx.Sign.Panels.Count; i++)
            {
                PanelDef panel = ctx.Sign.Panels[i];
                FaceDef face = ctx.Catalog.Faces[panel.Face];
                Vec2 size = ShapeLibrary.BoundingSize(face);
                if (i > 0)
                    total += panel.GapAbove >= 0f ? panel.GapAbove : ctx.Mounting.PanelGap;
                total += size.Y;
                ctx.Panels.Add(new PanelLayout { Face = face, Panel = panel, Size = size, CenterX = panel.OffsetX });
            }

            float cursor = mountHeight + total;
            for (int i = 0; i < ctx.Panels.Count; i++)
            {
                PanelLayout p = ctx.Panels[i];
                if (i > 0)
                    cursor -= p.Panel.GapAbove >= 0f ? p.Panel.GapAbove : ctx.Mounting.PanelGap;
                p.CenterY = cursor - p.Size.Y * 0.5f;
                cursor -= p.Size.Y;
                ctx.Result.PanelArea += PanelArea(p.Face) * K * K;
            }
        }

        private static float PanelArea(FaceDef face)
        {
            return Math.Abs(MeshPart.SignedArea(ShapeLibrary.Outline(face, 0f, face.CornerRadius, 2)));
        }

        private static float PanelsTop(Context ctx)
        {
            float top = float.MinValue;
            foreach (PanelLayout p in ctx.Panels)
                top = Math.Max(top, p.Top);
            return top;
        }

        private static float PanelsBottom(Context ctx)
        {
            float bottom = float.MaxValue;
            foreach (PanelLayout p in ctx.Panels)
                bottom = Math.Min(bottom, p.Bottom);
            return bottom;
        }

        private static float TopClearance(Context ctx)
        {
            return ctx.Sign.Support.TopClearance >= 0f ? ctx.Sign.Support.TopClearance : ctx.Mounting.PostTopClearance;
        }

        // ----------------------------------------------------------------------------------------
        // Plates
        // ----------------------------------------------------------------------------------------

        /// <summary>Converts sign-local inches (s = viewer right, t = up) to object space meters.</summary>
        private static Vec3 ToObject(float s, float t, float zInches) => new Vec3(-s * K, t * K, zInches * K);

        private static void AddPlate(Context ctx, PanelLayout panel, float zBack)
        {
            FaceDef face = panel.Face;
            float thickness = ctx.Mounting.PlateThickness;
            float zFront = zBack + thickness;
            List<Vec2> outline = ShapeLibrary.Outline(face, 0f, face.CornerRadius);
            Vec2 size = panel.Size;

            // Printed front face.
            MeshPart facePart = ctx.FacePart(face.Id);
            facePart.AddPolygon(
                outline,
                ToObject(panel.CenterX, panel.CenterY, zFront),
                new Vec3(-K, 0f, 0f),
                new Vec3(0f, K, 0f),
                Vec3.Forward,
                p => new Vec2(0.5f + p.X / size.X, 0.5f + p.Y / size.Y));

            // Aluminum back.
            ctx.Aluminum.AddPolygon(
                outline,
                ToObject(panel.CenterX, panel.CenterY, zBack),
                new Vec3(-K, 0f, 0f),
                new Vec3(0f, K, 0f),
                Vec3.Back,
                p => new Vec2(p.X * K * AluminumUvPerMeter, p.Y * K * AluminumUvPerMeter));

            // Edges.
            float area = MeshPart.SignedArea(outline);
            float perimeter = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 a = outline[i];
                Vec2 b = outline[(i + 1) % outline.Count];
                Vec2 e = b - a;
                float len = e.Length;
                if (len < 1e-5f)
                    continue;
                Vec2 outward = area > 0f ? new Vec2(e.Y, -e.X) / len : new Vec2(-e.Y, e.X) / len;
                Vec3 normal = new Vec3(-outward.X, outward.Y, 0f);
                float u0 = perimeter * K * AluminumUvPerMeter;
                float u1 = (perimeter + len) * K * AluminumUvPerMeter;
                float v1 = thickness * K * AluminumUvPerMeter;
                ctx.Aluminum.AddQuad(
                    ToObject(panel.CenterX + a.X, panel.CenterY + a.Y, zFront),
                    ToObject(panel.CenterX + b.X, panel.CenterY + b.Y, zFront),
                    ToObject(panel.CenterX + b.X, panel.CenterY + b.Y, zBack),
                    ToObject(panel.CenterX + a.X, panel.CenterY + a.Y, zBack),
                    normal, new Vec2(u0, v1), new Vec2(u1, v1), new Vec2(u1, 0f), new Vec2(u0, 0f));
                perimeter += len;
            }
        }

        /// <summary>Vertical positions (inches) of the fastener rows for a panel.</summary>
        private static float[] FastenerRows(PanelLayout panel)
        {
            string shape = ShapeLibrary.Normalize(panel.Face.Shape);
            float h = panel.Size.Y;
            float inset;
            switch (shape)
            {
                case "diamond":
                    inset = Math.Min(3.5f, h * 0.12f);
                    break;
                case "triangle":
                case "octagon":
                case "circle":
                case "pentagon":
                    inset = Math.Min(2.5f, h * 0.12f);
                    break;
                default:
                    inset = h <= 12.5f ? Math.Min(1.25f, h * 0.2f) : 1.5f;
                    break;
            }
            if (h < 8f)
                return new[] { panel.CenterY };
            return new[] { panel.Top - inset, panel.Bottom + inset };
        }

        private static void AddBolt(Context ctx, float s, float t, float zFaceInches)
        {
            if (!ctx.Options.IncludeBolts)
                return;
            Vec3 basePoint = ToObject(s, t, zFaceInches);
            // Nylon/steel washer and a low domed carriage-bolt head.
            ctx.Galvanized.AddCylinder(basePoint, Vec3.Forward, 0.06f * K, 0.5f * K, 8, capStart: false, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
            ctx.Galvanized.AddCylinder(basePoint + new Vec3(0f, 0f, 0.06f * K), Vec3.Forward, 0.16f * K, 0.32f * K, 8, capStart: false, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
        }

        // ----------------------------------------------------------------------------------------
        // Single post supports
        // ----------------------------------------------------------------------------------------

        private enum PostKind
        {
            SquareTube,
            UChannel,
            RoundPipe,
        }

        private static void BuildSinglePost(Context ctx, PostKind kind)
        {
            float top = PanelsTop(ctx) + TopClearance(ctx);
            float bottom = -ctx.Mounting.PostBurial;
            float plateBack;

            switch (kind)
            {
                case PostKind.UChannel:
                {
                    AddUChannelPost(ctx, 0f, bottom, top);
                    plateBack = 0.02f;
                    ctx.Result.LegSize = new Vec3(3.1f * K, PanelsBottom(ctx) * K, 1.3f * K);
                    break;
                }
                case PostKind.RoundPipe:
                {
                    const float radius = 2.375f * 0.5f;
                    AddRoundPipe(ctx, 0f, 0f, radius, bottom, top);
                    plateBack = radius + 0.55f;
                    ctx.Result.LegSize = new Vec3(radius * 2f * K, PanelsBottom(ctx) * K, radius * 2f * K);
                    ctx.Result.CircularLeg = true;
                    break;
                }
                default:
                {
                    AddSquareTube(ctx, 0f, 0f, 2f, bottom, top);
                    plateBack = 1.0f + 0.02f;
                    ctx.Result.LegSize = new Vec3(2f * K, PanelsBottom(ctx) * K, 2f * K);
                    break;
                }
            }
            ctx.Result.PostCount = 1;

            float faceZ = plateBack + ctx.Mounting.PlateThickness;
            foreach (PanelLayout panel in ctx.Panels)
            {
                AddPlate(ctx, panel, plateBack);
                if (kind == PostKind.RoundPipe)
                {
                    const float radius = 2.375f * 0.5f;
                    foreach (float row in FastenerRows(panel))
                    {
                        AddPipeClampBracket(ctx, 0f, row, radius, plateBack);
                        AddBolt(ctx, -1.75f, row, faceZ);
                        AddBolt(ctx, 1.75f, row, faceZ);
                    }
                }
                else
                {
                    foreach (float row in FastenerRows(panel))
                        AddBolt(ctx, 0f, row, faceZ);
                }
            }
        }

        /// <summary>2 in (or other) perforated square steel tube with slightly rounded corners.</summary>
        private static void AddSquareTube(Context ctx, float xInches, float zInches, float sizeInches, float bottom, float top)
        {
            float h = sizeInches * 0.5f;
            float c = Math.Min(0.14f, sizeInches * 0.08f);
            var profile = new List<Vec2>
            {
                new Vec2(-h + c, -h), new Vec2(h - c, -h), new Vec2(h, -h + c), new Vec2(h, h - c),
                new Vec2(h - c, h), new Vec2(-h + c, h), new Vec2(-h, h - c), new Vec2(-h, -h + c),
            };
            for (int i = 0; i < profile.Count; i++)
                profile[i] = profile[i] * K;

            Vec3 origin = new Vec3(-xInches * K, 0f, zInches * K);
            // Long sides get the hole pattern across u [0,1]; chamfers map to a hole-free strip.
            ctx.Perforated.AddVerticalExtrusion(profile, origin, bottom * K, top * K, capBottom: false, capTop: false,
                uvScale: InchesPerMeter,
                segmentU: (index, len) => len > 0.5f * K ? new Vec2(0f, 1f) : new Vec2(0.0f, 0.04f));
            // Cap
            ctx.Galvanized.AddPolygon(MeshPart.ToCounterClockwise(profile), origin + new Vec3(0f, top * K, 0f), Vec3.Right, Vec3.Forward, Vec3.Up,
                p => p * GalvanizedUvPerMeter);
        }

        /// <summary>2 lb/ft rib-back U-channel. The flat web faces the sign (front, +Z); flanges point backward.</summary>
        private static void AddUChannelPost(Context ctx, float xInches, float bottom, float top)
        {
            const float t = 0.14f;
            var centerline = new List<Vec2>
            {
                new Vec2(-1.50f, -1.16f), new Vec2(-1.30f, -1.06f), new Vec2(-0.86f, -t * 0.5f),
                new Vec2(0.86f, -t * 0.5f), new Vec2(1.30f, -1.06f), new Vec2(1.50f, -1.16f),
            };
            List<Vec2> profile = ThickPolyline(centerline, t);
            for (int i = 0; i < profile.Count; i++)
                profile[i] = profile[i] * K;

            Vec3 origin = new Vec3(-xInches * K, 0f, 0f);
            // The two web faces are the only ~1.72 in long segments: they get the hole pattern, everything else a plain strip.
            ctx.Perforated.AddVerticalExtrusion(MeshPart.ToCounterClockwise(profile), origin, bottom * K, top * K, capBottom: false, capTop: true,
                uvScale: InchesPerMeter,
                segmentU: (index, len) => Math.Abs(len - 1.72f * K) < 0.2f * K ? new Vec2(0f, 1f) : new Vec2(0f, 0.04f));
        }

        /// <summary>Galvanized pipe with a domed cap.</summary>
        private static void AddRoundPipe(Context ctx, float xInches, float zInches, float radiusInches, float bottom, float top)
        {
            Vec3 start = new Vec3(-xInches * K, bottom * K, zInches * K);
            float length = (top - bottom) * K;
            float r = radiusInches * K;
            ctx.Galvanized.AddCylinder(start, Vec3.Up, length, r, 16, capStart: false, capEnd: false, smooth: true, uvScale: GalvanizedUvPerMeter);
            // Domed cap: two stacked rings.
            Vec3 capBase = start + new Vec3(0f, length, 0f);
            AddDome(ctx.Galvanized, capBase, r * 1.04f, r * 0.55f, 16, 3, GalvanizedUvPerMeter);
        }

        private static void AddDome(MeshPart part, Vec3 center, float radius, float height, int segments, int rings, float uvScale)
        {
            // Short skirt so the cap slightly overhangs the pipe.
            part.AddCylinder(center - new Vec3(0f, 0.012f, 0f), Vec3.Up, 0.012f, radius, segments, capStart: true, capEnd: false, smooth: true, uvScale: uvScale);
            var prevRing = new List<int>();
            for (int ring = 0; ring <= rings; ring++)
            {
                float phi = (MathUtil.Pi * 0.5f) * ring / (rings + 1);
                float rr = radius * (float)Math.Cos(phi);
                float y = height * (float)Math.Sin(phi);
                var current = new List<int>();
                for (int i = 0; i <= segments; i++)
                {
                    float a = 2f * MathUtil.Pi * i / segments;
                    var dir = new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a));
                    Vec3 pos = center + dir * rr + new Vec3(0f, y, 0f);
                    Vec3 normal = (dir * (float)Math.Cos(phi) + Vec3.Up * (float)Math.Sin(phi)).Normalized;
                    current.Add(part.AddVertex(pos, normal, new Vec2(i / (float)segments, ring / (float)rings) * (radius * 6f * uvScale)));
                }
                if (ring > 0)
                {
                    for (int i = 0; i < segments; i++)
                    {
                        Vec3 facing = part.Normals[prevRing[i]];
                        part.AddTriangle(prevRing[i], prevRing[i + 1], current[i + 1], facing);
                        part.AddTriangle(prevRing[i], current[i + 1], current[i], facing);
                    }
                }
                prevRing = current;
            }
            int tip = part.AddVertex(center + new Vec3(0f, height, 0f), Vec3.Up, new Vec2(0f, 0f));
            for (int i = 0; i < segments; i++)
                part.AddTriangle(prevRing[i], prevRing[i + 1], tip, Vec3.Up);
        }

        /// <summary>Sign bracket for round posts: aluminum strip on the sign back plus a steel band around the pipe.</summary>
        private static void AddPipeClampBracket(Context ctx, float xInches, float yInches, float pipeRadius, float plateBack)
        {
            float stripDepth = plateBack - pipeRadius;
            // Strip behind the sign.
            ctx.Aluminum.AddBox(
                new Vec3(-xInches * K, yInches * K, (pipeRadius + stripDepth * 0.5f) * K),
                new Vec3(5.0f * K, 1.25f * K, stripDepth * K),
                YawTransform.Identity, AluminumUvPerMeter);
            // Band around the pipe.
            ctx.Galvanized.AddCylinder(
                new Vec3(-xInches * K, (yInches - 0.5f) * K, 0f),
                Vec3.Up, 1.0f * K, (pipeRadius + 0.08f) * K, 16, capStart: true, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
        }

        // ----------------------------------------------------------------------------------------
        // Twin posts (large signs)
        // ----------------------------------------------------------------------------------------

        private static void BuildTwinPosts(Context ctx, bool ibeam)
        {
            float width = 0f;
            foreach (PanelLayout p in ctx.Panels)
                width = Math.Max(width, p.Size.X);
            float spacing = ctx.Sign.Support.Spacing > 0f ? ctx.Sign.Support.Spacing : Math.Max(18f, width * 0.55f);
            float top = PanelsTop(ctx) + (ctx.Sign.Support.TopClearance >= 0f ? ctx.Sign.Support.TopClearance : 0.5f);
            float bottom = -ctx.Mounting.PostBurial;
            float halfSpacing = spacing * 0.5f;

            float plateBack;
            float postWidth;
            float postDepth;
            if (ibeam)
            {
                const float d = 5.90f, bf = 3.94f;
                AddIBeam(ctx, -halfSpacing, bottom, top);
                AddIBeam(ctx, halfSpacing, bottom, top);
                const float zBarDepth = 2.0f;
                plateBack = d * 0.5f + zBarDepth;
                postWidth = bf;
                postDepth = d;
            }
            else
            {
                const float size = 2.5f;
                AddSquareTube(ctx, -halfSpacing, 0f, size, bottom, top);
                AddSquareTube(ctx, halfSpacing, 0f, size, bottom, top);
                plateBack = size * 0.5f + 0.02f;
                postWidth = size;
                postDepth = size;
            }

            ctx.Result.PostCount = 2;
            ctx.Result.LegSize = new Vec3(postWidth * K, PanelsBottom(ctx) * K, postDepth * K);
            ctx.Result.LegGapX = (spacing - postWidth) * K;

            float faceZ = plateBack + ctx.Mounting.PlateThickness;
            foreach (PanelLayout panel in ctx.Panels)
            {
                AddPlate(ctx, panel, plateBack);
                float[] rows = FastenerRows(panel);
                if (ibeam)
                {
                    rows = new[] { panel.Bottom + panel.Size.Y * 0.8f, panel.Bottom + panel.Size.Y * 0.2f };
                    foreach (float row in rows)
                    {
                        // Aluminum Z-bar stiffener spanning the sign.
                        float barWidth = panel.Size.X - 4f;
                        ctx.Aluminum.AddBox(
                            new Vec3(-panel.CenterX * K, row * K, (plateBack - 1.0f) * K),
                            new Vec3(barWidth * K, 2.0f * K, 2.0f * K), YawTransform.Identity, AluminumUvPerMeter);
                        // Post clips.
                        foreach (float px in new[] { -halfSpacing, halfSpacing })
                        {
                            ctx.Galvanized.AddBox(
                                new Vec3(-px * K, row * K, (5.90f * 0.5f + 0.2f) * K),
                                new Vec3(5.0f * K, 3.0f * K, 0.4f * K), YawTransform.Identity, GalvanizedUvPerMeter);
                        }
                    }
                }
                foreach (float row in rows)
                {
                    AddBolt(ctx, -halfSpacing, row, faceZ);
                    AddBolt(ctx, halfSpacing, row, faceZ);
                }
            }
        }

        private static void AddIBeam(Context ctx, float xInches, float bottom, float top)
        {
            const float d = 5.90f, bf = 3.94f, tf = 0.215f, tw = 0.170f;
            float hd = d * 0.5f, hb = bf * 0.5f, ht = tw * 0.5f;
            var profile = new List<Vec2>
            {
                new Vec2(-hb, -hd), new Vec2(hb, -hd), new Vec2(hb, -hd + tf), new Vec2(ht, -hd + tf),
                new Vec2(ht, hd - tf), new Vec2(hb, hd - tf), new Vec2(hb, hd), new Vec2(-hb, hd),
                new Vec2(-hb, hd - tf), new Vec2(-ht, hd - tf), new Vec2(-ht, -hd + tf), new Vec2(-hb, -hd + tf),
            };
            for (int i = 0; i < profile.Count; i++)
                profile[i] = profile[i] * K;
            ctx.Galvanized.AddVerticalExtrusion(MeshPart.ToCounterClockwise(profile), new Vec3(-xInches * K, 0f, 0f), bottom * K, top * K,
                capBottom: false, capTop: true, uvScale: GalvanizedUvPerMeter);
        }

        // ----------------------------------------------------------------------------------------
        // Temporary supports
        // ----------------------------------------------------------------------------------------

        private static void BuildPortableStand(Context ctx)
        {
            // Spring-loaded portable sign stand holding a roll-up/rigid sign by its corners.
            float top = PanelsTop(ctx);
            float bottomPanel = PanelsBottom(ctx);
            float mastTop = top - 1.5f;
            const float mast = 1.0f;

            // Vertical mast (behind the sign).
            float plateBack = mast * 0.5f + 0.35f;
            AddSquareTube(ctx, 0f, 0f, mast, 9f, mastTop);
            // Horizontal cross brace across the widest panel.
            float width = 0f;
            float braceY = 0f;
            foreach (PanelLayout p in ctx.Panels)
            {
                if (p.Size.X > width)
                {
                    width = p.Size.X;
                    braceY = p.CenterY;
                }
            }
            ctx.Galvanized.AddBox(new Vec3(0f, braceY * K, 0.1f * K), new Vec3((width - 3f) * K, 0.9f * K, 0.9f * K), YawTransform.Identity, GalvanizedUvPerMeter);

            // Spring and base housing.
            ctx.Dark.AddCylinder(new Vec3(0f, 6f * K, 0f), Vec3.Up, 3.2f * K, 0.9f * K, 10, capStart: true, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
            ctx.Galvanized.AddBox(new Vec3(0f, 4.2f * K, 0f), new Vec3(2.2f * K, 3.0f * K, 2.2f * K), YawTransform.Identity, GalvanizedUvPerMeter);

            // Four legs splayed at 45 degrees, lying almost flat with rubber feet.
            for (int i = 0; i < 4; i++)
            {
                float yaw = MathUtil.Pi * 0.25f + i * MathUtil.Pi * 0.5f;
                var t = new YawTransform(Vec3.Zero, yaw);
                const float legLength = 30f;
                ctx.Galvanized.AddBox(new Vec3(0f, 2.2f * K, legLength * 0.5f * K), new Vec3(1.0f * K, 1.0f * K, legLength * K), t, GalvanizedUvPerMeter);
                ctx.Dark.AddBox(new Vec3(0f, 0.8f * K, (legLength - 1f) * K), new Vec3(1.6f * K, 1.6f * K, 3f * K), t, GalvanizedUvPerMeter);
            }

            ctx.Result.PostCount = 1;
            ctx.Result.LegSize = new Vec3(4f * K, bottomPanel * K, 4f * K);
            foreach (PanelLayout panel in ctx.Panels)
                AddPlate(ctx, panel, plateBack);
        }

        private static void BuildBarricade(Context ctx)
        {
            // Type III barricade: two uprights with three striped rails; sign panels mounted on the uprights.
            float width = 0f;
            foreach (PanelLayout p in ctx.Panels)
                width = Math.Max(width, p.Size.X);
            float spacing = ctx.Sign.Support.Spacing > 0f ? ctx.Sign.Support.Spacing : Math.Max(60f, width + 12f);
            float half = spacing * 0.5f;
            float top = PanelsTop(ctx) + 1f;

            AddSquareTube(ctx, -half, 0f, 2.5f, 0f, top);
            AddSquareTube(ctx, half, 0f, 2.5f, 0f, top);
            // Feet.
            foreach (float x in new[] { -half, half })
                ctx.Galvanized.AddBox(new Vec3(-x * K, 1f * K, 0f), new Vec3(3f * K, 2f * K, 30f * K), YawTransform.Identity, GalvanizedUvPerMeter);

            ctx.Result.PostCount = 2;
            ctx.Result.LegSize = new Vec3(3f * K, PanelsBottom(ctx) * K, 30f * K);
            ctx.Result.LegGapX = (spacing - 3f) * K;
            float plateBack = 1.25f + 0.02f;
            foreach (PanelLayout panel in ctx.Panels)
                AddPlate(ctx, panel, plateBack);
        }

        private static void BuildFlexPost(Context ctx)
        {
            // In-street flexible sign: rubber base on the pavement and a flexible upright behind the sign.
            float top = PanelsTop(ctx) - 1f;
            ctx.Dark.AddCylinder(new Vec3(0f, 0f, 0f), Vec3.Up, 1.2f * K, 5.5f * K, 14, capStart: false, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
            ctx.Dark.AddCylinder(new Vec3(0f, 1.2f * K, 0f), Vec3.Up, (top - 1.2f) * K, 1.1f * K, 10, capStart: false, capEnd: true, smooth: true, uvScale: GalvanizedUvPerMeter);
            ctx.Result.PostCount = 1;
            ctx.Result.LegSize = new Vec3(2.2f * K, PanelsBottom(ctx) * K, 2.2f * K);
            ctx.Result.CircularLeg = true;
            float plateBack = 1.15f;
            foreach (PanelLayout panel in ctx.Panels)
                AddPlate(ctx, panel, plateBack);
        }

        // ----------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------

        /// <summary>Turns an open polyline into a closed polygon of the given thickness (mitered joints).</summary>
        private static List<Vec2> ThickPolyline(IReadOnlyList<Vec2> line, float thickness)
        {
            int n = line.Count;
            var left = new List<Vec2>(n);
            var right = new List<Vec2>(n);
            float h = thickness * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Vec2 dirPrev = i > 0 ? (line[i] - line[i - 1]).Normalized : (line[1] - line[0]).Normalized;
                Vec2 dirNext = i < n - 1 ? (line[i + 1] - line[i]).Normalized : dirPrev;
                Vec2 nPrev = dirPrev.PerpLeft;
                Vec2 nNext = dirNext.PerpLeft;
                Vec2 miter = (nPrev + nNext).Normalized;
                float denom = Vec2.Dot(miter, nNext);
                float scale = Math.Abs(denom) > 0.2f ? h / denom : h;
                left.Add(line[i] + miter * scale);
                right.Add(line[i] - miter * scale);
            }
            var polygon = new List<Vec2>(n * 2);
            polygon.AddRange(left);
            for (int i = n - 1; i >= 0; i--)
                polygon.Add(right[i]);
            return polygon;
        }

        private static void Finish(Context ctx)
        {
            AssemblyMesh result = ctx.Result;
            void AddIfUsed(MeshPart part)
            {
                if (!part.IsEmpty)
                    result.Parts.Add(part);
            }

            AddIfUsed(ctx.Galvanized);
            AddIfUsed(ctx.Perforated);
            AddIfUsed(ctx.Aluminum);
            AddIfUsed(ctx.Dark);
            foreach (string faceId in ctx.FaceOrder)
                AddIfUsed(ctx.Faces[faceId]);

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
