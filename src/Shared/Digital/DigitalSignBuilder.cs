using System;
using System.Collections.Generic;
using USRoadSigns.Geometry;

namespace USRoadSigns.Digital
{
    /// <summary>Where the message is drawn: a rectangle facing +Z, in object space (meters).</summary>
    public struct DisplayRect
    {
        public Vec3 Center;
        public float Width;
        public float Height;
    }

    /// <summary>Warning beacon lens facing +Z (object space, meters).</summary>
    public struct BeaconLens
    {
        public Vec3 Center;
        public float Diameter;
    }

    public sealed class DigitalSignMesh
    {
        public AssemblyMesh Assembly;
        public DisplayRect Display;
        public readonly List<BeaconLens> Beacons = new List<BeaconLens>();
    }

    /// <summary>
    /// Builds the structures of digital message signs. Same object space as <see cref="AssemblyBuilder"/>:
    /// origin on the ground, +Y up, the display faces +Z (the viewer looks toward -Z, so the viewer's right is -X).
    /// </summary>
    public static class DigitalSignBuilder
    {
        private const float PaintUv = 2f;
        private const float GalvanizedUv = 3.3f;
        private const float ConcreteUv = 1f;

        /// <summary>Lamp spacing of arrow boards, meters.</summary>
        public const float ArrowLampPitch = 0.16f;

        private sealed class Context
        {
            public readonly MeshPart Galvanized = new MeshPart(MaterialSlot.Galvanized);
            public readonly MeshPart Aluminum = new MeshPart(MaterialSlot.Aluminum);
            public readonly MeshPart Orange = new MeshPart(MaterialSlot.OrangePaint);
            public readonly MeshPart Housing = new MeshPart(MaterialSlot.BlackHousing);
            public readonly MeshPart Led = new MeshPart(MaterialSlot.LedMatrix);
            public readonly MeshPart Concrete = new MeshPart(MaterialSlot.Concrete);
            public readonly MeshPart Solar = new MeshPart(MaterialSlot.Solar);
            public readonly MeshPart Rubber = new MeshPart(MaterialSlot.Rubber);
            public readonly DigitalSignMesh Result = new DigitalSignMesh { Assembly = new AssemblyMesh() };

            public IEnumerable<MeshPart> Parts => new[] { Galvanized, Aluminum, Orange, Housing, Led, Concrete, Solar, Rubber };
        }

        public static DigitalSignMesh Build(DigitalSignType type)
        {
            var ctx = new Context();
            switch (type.Structure)
            {
                case StructureKind.Trailer:
                    BuildTrailer(ctx, type, compact: false);
                    break;
                case StructureKind.CompactTrailer:
                    BuildTrailer(ctx, type, compact: true);
                    break;
                case StructureKind.ArrowTrailer:
                    BuildTrailer(ctx, type, compact: false);
                    break;
                case StructureKind.Pole:
                    BuildPole(ctx, type);
                    break;
                case StructureKind.Cantilever:
                    BuildCantilever(ctx, type);
                    break;
                case StructureKind.Gantry:
                    BuildGantry(ctx, type);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
            Finish(ctx);
            return ctx.Result;
        }

        /// <summary>Width and height of the lit area (meters).</summary>
        public static void DisplaySize(DigitalSignType type, out float width, out float height)
        {
            if (type.Display == DisplayKind.Arrow)
            {
                width = ArrowBoard.Columns * ArrowLampPitch;
                height = ArrowBoard.Rows * ArrowLampPitch;
            }
            else
            {
                width = type.Columns * type.Pitch;
                height = type.Rows * type.Pitch;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Display cabinet (shared by every structure)
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Black cabinet with a bezel around the display window. Returns the cabinet outer size.
        /// </summary>
        private static Vec2 AddCabinet(Context ctx, DigitalSignType type, float centerX, float bottomY, float frontZ, float border, float depth, float extraHeight = 0f)
        {
            DisplaySize(type, out float w, out float h);
            float cw = w + border * 2f;
            float ch = h + border * 2f + extraHeight;
            float centerY = bottomY + ch * 0.5f;
            float displayY = bottomY + border + h * 0.5f + extraHeight * 0.5f;

            // Body, set back so the bezel stands proud of the window.
            const float bezel = 0.035f;
            float bodyFront = frontZ - bezel;
            ctx.Housing.AddBox(new Vec3(centerX, centerY, bodyFront - (depth - bezel) * 0.5f), new Vec3(cw, ch, depth - bezel), YawTransform.Identity, PaintUv);

            // Bezel: four bars framing the window.
            float zb = bodyFront + bezel * 0.5f;
            float cabTop = bottomY + ch;
            float windowTop = displayY + h * 0.5f;
            float windowBottom = displayY - h * 0.5f;
            ctx.Housing.AddBox(new Vec3(centerX, (cabTop + windowTop) * 0.5f, zb), new Vec3(cw, cabTop - windowTop, bezel), YawTransform.Identity, PaintUv);
            ctx.Housing.AddBox(new Vec3(centerX, (bottomY + windowBottom) * 0.5f, zb), new Vec3(cw, windowBottom - bottomY, bezel), YawTransform.Identity, PaintUv);
            ctx.Housing.AddBox(new Vec3(centerX + w * 0.5f + border * 0.5f, displayY, zb), new Vec3(border, h, bezel), YawTransform.Identity, PaintUv);
            ctx.Housing.AddBox(new Vec3(centerX - w * 0.5f - border * 0.5f, displayY, zb), new Vec3(border, h, bezel), YawTransform.Identity, PaintUv);

            // Unlit LED window (tiles one LED per UV unit); the live message is drawn just in front of it.
            float units = type.Display == DisplayKind.Arrow ? 1f / ArrowLampPitch : 1f / type.Pitch;
            float wz = bodyFront + 0.002f;
            Vec3 a = new Vec3(centerX - w * 0.5f, displayY - h * 0.5f, wz);
            Vec3 b = new Vec3(centerX + w * 0.5f, displayY - h * 0.5f, wz);
            Vec3 c = new Vec3(centerX + w * 0.5f, displayY + h * 0.5f, wz);
            Vec3 d = new Vec3(centerX - w * 0.5f, displayY + h * 0.5f, wz);
            if (type.Display == DisplayKind.Arrow)
                ctx.Housing.AddQuad(a, b, c, d, Vec3.Forward, new Vec2(0, 0), new Vec2(w * PaintUv, 0), new Vec2(w * PaintUv, h * PaintUv), new Vec2(0, h * PaintUv));
            else
                ctx.Led.AddQuad(a, b, c, d, Vec3.Forward, new Vec2(w * units, 0), new Vec2(0, 0), new Vec2(0, h * units), new Vec2(w * units, h * units));

            // Sun hood over the top edge.
            ctx.Housing.AddBox(new Vec3(centerX, bottomY + ch - 0.01f, frontZ + 0.06f), new Vec3(cw, 0.02f, 0.14f), YawTransform.Identity, PaintUv);

            ctx.Result.Display = new DisplayRect { Center = new Vec3(centerX, displayY, wz + 0.004f), Width = w, Height = h };

            if (type.Display == DisplayKind.Arrow)
                AddLampHoods(ctx, centerX, displayY, wz);

            return new Vec2(cw, ch);
        }

        /// <summary>Short black sun visors over each arrow-board lamp.</summary>
        private static void AddLampHoods(Context ctx, float centerX, float centerY, float z)
        {
            LedFrame lamps = ArrowBoard.LampLayout();
            float p = ArrowLampPitch;
            for (int row = 0; row < ArrowBoard.Rows; row++)
            {
                for (int col = 0; col < ArrowBoard.Columns; col++)
                {
                    if (!lamps[col, row])
                        continue;
                    // Column 0 is the viewer's left, which is +X.
                    float x = centerX + (ArrowBoard.Columns * 0.5f - col - 0.5f) * p;
                    float y = centerY + (ArrowBoard.Rows * 0.5f - row - 0.5f) * p;
                    ctx.Housing.AddBox(new Vec3(x, y + p * 0.47f, z + 0.035f), new Vec3(p * 0.86f, 0.008f, 0.07f), YawTransform.Identity, PaintUv);
                }
            }
        }

        /// <summary>Round warning beacon in a black housing; the lens faces +Z.</summary>
        private static void AddBeacon(Context ctx, Vec3 lensCenter, float diameter)
        {
            float r = diameter * 0.5f;
            ctx.Housing.AddCylinder(lensCenter + new Vec3(0f, 0f, -0.14f), Vec3.Forward, 0.13f, r + 0.03f, 16, true, false, true, PaintUv);
            ctx.Housing.AddDisc(lensCenter + new Vec3(0f, 0f, -0.01f), Vec3.Forward, Vec3.Right, Vec3.Up, r + 0.03f, 16, PaintUv);
            // Visor
            ctx.Housing.AddBox(lensCenter + new Vec3(0f, r + 0.03f, 0.05f), new Vec3(diameter + 0.06f, 0.012f, 0.12f), YawTransform.Identity, PaintUv);
            ctx.Result.Beacons.Add(new BeaconLens { Center = lensCenter, Diameter = diameter });
        }

        // ----------------------------------------------------------------------------------------
        // Trailers
        // ----------------------------------------------------------------------------------------

        private static void BuildTrailer(Context ctx, DigitalSignType type, bool compact)
        {
            float s = compact ? 0.68f : 1f;            // overall trailer scale
            float frameY = 0.56f * s + (compact ? 0.06f : 0f);
            float railX = 0.6f * s;
            float rearZ = 0.7f * s;
            float frontZ = -1.9f * s;
            float hitchZ = -3.15f * s;
            float axleZ = -0.45f * s;
            float wheelR = compact ? 0.24f : 0.34f;
            float wheelW = compact ? 0.15f : 0.2f;
            float wheelX = railX + 0.06f + wheelW * 0.5f + 0.05f;

            // Frame rails, cross members and tongue (rectangular orange tubing).
            Vec3 rail = new Vec3(0.07f, 0.12f, rearZ - frontZ);
            ctx.Orange.AddBox(new Vec3(railX, frameY, (rearZ + frontZ) * 0.5f), rail, YawTransform.Identity, PaintUv);
            ctx.Orange.AddBox(new Vec3(-railX, frameY, (rearZ + frontZ) * 0.5f), rail, YawTransform.Identity, PaintUv);
            foreach (float z in new[] { rearZ - 0.035f, axleZ + 0.4f * s, frontZ + 0.035f })
                ctx.Orange.AddBox(new Vec3(0f, frameY, z), new Vec3(railX * 2f, 0.1f, 0.07f), YawTransform.Identity, PaintUv);
            AddBeam(ctx.Orange, new Vec3(railX, frameY, frontZ), new Vec3(0.05f, frameY, hitchZ + 0.1f), 0.07f, 0.11f);
            AddBeam(ctx.Orange, new Vec3(-railX, frameY, frontZ), new Vec3(-0.05f, frameY, hitchZ + 0.1f), 0.07f, 0.11f);

            // Coupler and tongue jack.
            ctx.Galvanized.AddBox(new Vec3(0f, frameY, hitchZ), new Vec3(0.12f, 0.1f, 0.3f), YawTransform.Identity, GalvanizedUv);
            ctx.Galvanized.AddCylinder(new Vec3(0.13f, 0.06f, hitchZ + 0.4f * s), Vec3.Up, frameY + 0.32f, 0.03f, 10, false, true, true, GalvanizedUv);
            ctx.Galvanized.AddCylinder(new Vec3(0.13f, 0.0f, hitchZ + 0.4f * s), Vec3.Up, 0.03f, 0.08f, 12, true, true, true, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(0.13f, frameY + 0.32f, hitchZ + 0.4f * s - 0.08f), new Vec3(0.02f, 0.02f, 0.16f), YawTransform.Identity, GalvanizedUv);

            // Axle, wheels, hubs and fenders.
            ctx.Galvanized.AddCylinder(new Vec3(-wheelX, wheelR, axleZ), Vec3.Right, wheelX * 2f, 0.035f, 8, false, false, true, GalvanizedUv);
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * wheelX;
                ctx.Rubber.AddCylinder(new Vec3(x - wheelW * 0.5f, wheelR, axleZ), Vec3.Right, wheelW, wheelR, 20, true, true, true, 2f);
                ctx.Galvanized.AddCylinder(new Vec3(x + side * wheelW * 0.5f - (side < 0f ? 0.02f : 0f), wheelR, axleZ), Vec3.Right, 0.02f, wheelR * 0.55f, 16, true, true, true, GalvanizedUv);
                // Spring hanger
                ctx.Galvanized.AddBox(new Vec3(side * (railX + 0.02f), (wheelR + frameY) * 0.5f, axleZ), new Vec3(0.06f, frameY - wheelR, 0.5f * s), YawTransform.Identity, GalvanizedUv);
                // Fender: top plate plus front and rear aprons
                float fz = wheelR * 2.4f;
                ctx.Housing.AddBox(new Vec3(x, wheelR * 2f + 0.06f, axleZ), new Vec3(wheelW + 0.08f, 0.03f, fz), YawTransform.Identity, PaintUv);
                ctx.Housing.AddBox(new Vec3(x, wheelR * 1.55f, axleZ + fz * 0.5f), new Vec3(wheelW + 0.08f, wheelR * 0.95f, 0.03f), YawTransform.Identity, PaintUv);
                ctx.Housing.AddBox(new Vec3(x, wheelR * 1.55f, axleZ - fz * 0.5f), new Vec3(wheelW + 0.08f, wheelR * 0.95f, 0.03f), YawTransform.Identity, PaintUv);
                ctx.Housing.AddBox(new Vec3(x - side * (wheelW * 0.5f + 0.03f), wheelR * 1.7f, axleZ), new Vec3(0.02f, wheelR * 0.7f, fz), YawTransform.Identity, PaintUv);

                // Rear outrigger and leveling jack.
                float jx = side * (wheelX + wheelW * 0.5f + 0.12f);
                ctx.Galvanized.AddBox(new Vec3((side * railX + jx) * 0.5f, frameY, rearZ - 0.12f), new Vec3(Math.Abs(jx - side * railX), 0.06f, 0.06f), YawTransform.Identity, GalvanizedUv);
                AddJack(ctx, new Vec3(jx, 0f, rearZ - 0.12f), frameY + 0.12f);
                if (!compact)
                {
                    ctx.Galvanized.AddBox(new Vec3((side * railX + jx) * 0.5f, frameY, frontZ + 0.15f), new Vec3(Math.Abs(jx - side * railX), 0.06f, 0.06f), YawTransform.Identity, GalvanizedUv);
                    AddJack(ctx, new Vec3(jx, 0f, frontZ + 0.15f), frameY + 0.12f);
                }
            }

            // Battery / controller box with a lid seam.
            Vec3 boxSize = compact ? new Vec3(0.7f, 0.42f, 0.45f) : new Vec3(1.1f, 0.55f, 0.6f);
            Vec3 boxCenter = new Vec3(0f, frameY + 0.06f + boxSize.Y * 0.5f, frontZ + boxSize.Z * 0.5f + 0.15f);
            ctx.Orange.AddBox(boxCenter, boxSize, YawTransform.Identity, PaintUv);
            ctx.Orange.AddBox(boxCenter + new Vec3(0f, boxSize.Y * 0.5f - 0.04f, 0f), new Vec3(boxSize.X + 0.02f, 0.02f, boxSize.Z + 0.02f), YawTransform.Identity, PaintUv);
            ctx.Galvanized.AddBox(boxCenter + new Vec3(0f, boxSize.Y * 0.2f, -boxSize.Z * 0.5f - 0.01f), new Vec3(0.12f, 0.04f, 0.02f), YawTransform.Identity, GalvanizedUv);

            // Display cabinet raised on the mast.
            float border = type.Display == DisplayKind.Arrow ? 0.05f : (compact ? 0.07f : 0.1f);
            float extra = type.Display == DisplayKind.Arrow ? 0.45f : 0f;
            float cabDepth = compact ? 0.13f : 0.2f;
            float cabFront = rearZ - 0.1f;
            float cabBottom = compact ? 1.3f : (type.Display == DisplayKind.Arrow ? 2.15f : 2.05f);
            Vec2 cab = AddCabinet(ctx, type, 0f, cabBottom, cabFront, border, cabDepth, extra);
            float cabBack = cabFront - cabDepth;

            // Mast: twin telescoping square tubes (single tube on the compact trailer), base plate and brace.
            float mastZ = cabBack - 0.06f;
            float mastTop = cabBottom + cab.Y * 0.65f;
            float[] mastXs = compact ? new[] { 0f } : new[] { -0.13f, 0.13f };
            foreach (float mx in mastXs)
            {
                ctx.Orange.AddBox(new Vec3(mx, (frameY + mastTop) * 0.5f, mastZ), new Vec3(0.1f * s + 0.02f, mastTop - frameY, 0.1f * s + 0.02f), YawTransform.Identity, PaintUv);
                ctx.Orange.AddBox(new Vec3(mx, cabBottom * 0.55f + frameY * 0.45f, mastZ), new Vec3(0.13f * s + 0.02f, 0.25f, 0.13f * s + 0.02f), YawTransform.Identity, PaintUv);
            }
            ctx.Orange.AddBox(new Vec3(0f, frameY + 0.08f, mastZ), new Vec3(0.5f * s, 0.06f, 0.3f), YawTransform.Identity, PaintUv);
            AddBeam(ctx.Orange, new Vec3(0f, frameY + 0.08f, mastZ - 0.8f * s), new Vec3(0f, frameY + 0.9f * s, mastZ - 0.05f), 0.06f, 0.06f);
            // Cross bracket behind the cabinet.
            ctx.Orange.AddBox(new Vec3(0f, cabBottom + cab.Y * 0.35f, cabBack - 0.03f), new Vec3(cab.X * 0.6f, 0.08f, 0.05f), YawTransform.Identity, PaintUv);
            ctx.Orange.AddBox(new Vec3(0f, cabBottom + 0.12f, cabBack - 0.03f), new Vec3(cab.X * 0.6f, 0.08f, 0.05f), YawTransform.Identity, PaintUv);
            // Mast winch.
            ctx.Galvanized.AddCylinder(new Vec3(mastXs[mastXs.Length - 1] + 0.08f, frameY + 0.75f * s, mastZ - 0.02f), Vec3.Right, 0.05f, 0.1f * s, 12, true, true, true, GalvanizedUv);

            // Solar panel tilted on top of the cabinet.
            float panelW = compact ? 0.8f : (type.Display == DisplayKind.Arrow ? 1.4f : 1.5f);
            float panelD = compact ? 0.45f : 0.75f;
            float topY = cabBottom + cab.Y;
            AddSolarPanel(ctx, new Vec3(0f, topY + 0.08f + panelD * 0.25f, cabBack - panelD * 0.3f), panelW, panelD, 0.5f);
            ctx.Galvanized.AddBox(new Vec3(0f, topY + 0.04f, cabBack + cabDepth * 0.5f - 0.05f), new Vec3(0.06f, 0.08f, 0.06f), YawTransform.Identity, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(0f, topY + 0.1f, cabBack - panelD * 0.3f), new Vec3(0.06f, 0.2f, 0.06f), YawTransform.Identity, GalvanizedUv);

            // Collision: only the trailer footprint, so the cabinet can overhang.
            AssemblyMesh m = ctx.Result.Assembly;
            float footprintX = (wheelX + wheelW * 0.5f + 0.2f) * 2f;
            m.LegSize = new Vec3(footprintX, cabBottom - 0.05f, rearZ - hitchZ);
            m.CircularLeg = false;
            m.PostCount = 1;

            // Center the footprint on the object origin so the legs (always centered) cover it.
            ShiftZ(ctx, -(rearZ + hitchZ) * 0.5f);
        }

        private static void AddJack(Context ctx, Vec3 foot, float top)
        {
            ctx.Galvanized.AddBox(new Vec3(foot.X, top * 0.5f + 0.02f, foot.Z), new Vec3(0.05f, top - 0.02f, 0.05f), YawTransform.Identity, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(foot.X, 0.012f, foot.Z), new Vec3(0.16f, 0.024f, 0.16f), YawTransform.Identity, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(foot.X, top + 0.02f, foot.Z), new Vec3(0.03f, 0.03f, 0.12f), YawTransform.Identity, GalvanizedUv);
        }

        /// <summary>Solar module with an aluminum frame, tilted back by <paramref name="tilt"/> radians.</summary>
        private static void AddSolarPanel(Context ctx, Vec3 center, float width, float depth, float tilt)
        {
            float c = (float)Math.Cos(tilt), sn = (float)Math.Sin(tilt);
            // Panel plane axes: X across, "up-slope" rising toward the front (+Z) so it faces the sky and back.
            Vec3 along = new Vec3(0f, sn, c);
            Vec3 normal = new Vec3(0f, c, -sn);
            Vec3 hx = new Vec3(width * 0.5f, 0f, 0f);
            Vec3 hz = along * (depth * 0.5f);
            Vec3 top = center + normal * 0.02f;
            float cells = 1f / 0.156f;
            ctx.Solar.AddQuad(top - hx - hz, top + hx - hz, top + hx + hz, top - hx + hz, normal,
                new Vec2(0, 0), new Vec2(width * cells, 0), new Vec2(width * cells, depth * cells), new Vec2(0, depth * cells));
            // Frame / back as a thin slab.
            Vec3 bottom = center - normal * 0.02f;
            ctx.Aluminum.AddQuad(bottom - hx - hz, bottom + hx - hz, bottom + hx + hz, bottom - hx + hz, -normal,
                new Vec2(0, 0), new Vec2(width, 0), new Vec2(width, depth), new Vec2(0, depth));
            foreach (float sgn in new[] { -1f, 1f })
            {
                Vec3 e = center + hz * sgn;
                ctx.Aluminum.AddQuad(e - hx - normal * 0.02f, e + hx - normal * 0.02f, e + hx + normal * 0.025f, e - hx + normal * 0.025f, along * sgn,
                    new Vec2(0, 0), new Vec2(width, 0), new Vec2(width, 0.05f), new Vec2(0, 0.05f));
                Vec3 s = center + hx * sgn;
                ctx.Aluminum.AddQuad(s - hz - normal * 0.02f, s + hz - normal * 0.02f, s + hz + normal * 0.025f, s - hz + normal * 0.025f, new Vec3(sgn, 0f, 0f),
                    new Vec2(0, 0), new Vec2(depth, 0), new Vec2(depth, 0.05f), new Vec2(0, 0.05f));
            }
        }

        // ----------------------------------------------------------------------------------------
        // Permanent signs
        // ----------------------------------------------------------------------------------------

        private static void BuildPole(Context ctx, DigitalSignType type)
        {
            const float poleR = 0.2f;
            const float cabBottom = 4.9f;
            const float cabDepth = 0.55f;
            const float border = 0.22f;
            float cabBack = poleR + 0.12f;
            float cabFront = cabBack + cabDepth;

            AddFoundation(ctx, Vec3.Zero, 0.6f);
            Vec2 cab = AddCabinet(ctx, type, 0f, cabBottom, cabFront, border, cabDepth);
            float top = cabBottom + cab.Y;

            ctx.Galvanized.AddCylinder(new Vec3(0f, 0.05f, 0f), Vec3.Up, top - 0.35f, poleR, 20, false, true, true, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(0f, 0.17f, 0f), new Vec3(0.75f, 0.04f, 0.75f), YawTransform.Identity, GalvanizedUv);

            // Two mounting rails across the cabinet back, clamped to the pole.
            foreach (float y in new[] { cabBottom + 0.45f, top - 0.45f })
            {
                ctx.Galvanized.AddBox(new Vec3(0f, y, cabBack - 0.07f), new Vec3(cab.X * 0.7f, 0.2f, 0.14f), YawTransform.Identity, GalvanizedUv);
                ctx.Galvanized.AddBox(new Vec3(0f, y, 0f), new Vec3(poleR * 2.4f, 0.3f, poleR * 2.4f), YawTransform.Identity, GalvanizedUv);
            }

            AddBeacons(ctx, type, 0f, cab.X, top, cabFront);

            AssemblyMesh m = ctx.Result.Assembly;
            m.LegSize = new Vec3(poleR * 2f, cabBottom, poleR * 2f);
            m.CircularLeg = true;
            m.PostCount = 1;
        }

        private static void BuildCantilever(Context ctx, DigitalSignType type)
        {
            const float poleR = 0.3f;
            const float cabBottom = 5.4f;
            const float cabDepth = 0.7f;
            const float border = 0.22f;
            DisplaySize(type, out float w, out _);
            float cabW = w + border * 2f;
            float cabX = 1.2f + poleR + cabW * 0.5f;    // the arm reaches out to the viewer's left (+X)
            float armEnd = cabX + cabW * 0.5f + 0.4f;
            float cabBack = 0.14f;
            float cabFront = cabBack + cabDepth;

            AddFoundation(ctx, Vec3.Zero, 0.85f);
            Vec2 cab = AddCabinet(ctx, type, cabX, cabBottom, cabFront, border, cabDepth);
            float top = cabBottom + cab.Y;

            // Arm: two chords with vertical and diagonal lacing (a planar truss) behind the cabinet.
            float lowY = cabBottom + 0.35f;
            float highY = top - 0.25f;
            const float chordR = 0.09f;
            const float armZ = 0f;
            ctx.Galvanized.AddCylinder(new Vec3(poleR * 0.5f, lowY, armZ), Vec3.Right, armEnd - poleR * 0.5f, chordR, 12, false, true, true, GalvanizedUv);
            ctx.Galvanized.AddCylinder(new Vec3(poleR * 0.5f, highY, armZ), Vec3.Right, armEnd - poleR * 0.5f, chordR, 12, false, true, true, GalvanizedUv);
            int bays = Math.Max(2, (int)Math.Round((armEnd - poleR) / 1.3f));
            float bay = (armEnd - poleR - 0.1f) / bays;
            for (int i = 0; i <= bays; i++)
            {
                float x = poleR + 0.05f + i * bay;
                ctx.Galvanized.AddCylinder(new Vec3(x, lowY, armZ), Vec3.Up, highY - lowY, 0.035f, 8, false, false, true, GalvanizedUv);
                if (i < bays)
                {
                    Vec3 from = i % 2 == 0 ? new Vec3(x, lowY, armZ) : new Vec3(x, highY, armZ);
                    Vec3 to = i % 2 == 0 ? new Vec3(x + bay, highY, armZ) : new Vec3(x + bay, lowY, armZ);
                    AddStrut(ctx.Galvanized, from, to, 0.03f);
                }
            }
            // Cabinet hangers.
            foreach (float dx in new[] { -cabW * 0.3f, cabW * 0.3f })
                ctx.Galvanized.AddBox(new Vec3(cabX + dx, (lowY + highY) * 0.5f, (armZ + cabBack) * 0.5f), new Vec3(0.12f, highY - lowY + 0.3f, cabBack - armZ + 0.1f), YawTransform.Identity, GalvanizedUv);

            // Pole with base plate, arm connection plates and cap.
            float poleTop = highY + 0.5f;
            ctx.Galvanized.AddCylinder(new Vec3(0f, 0.05f, 0f), Vec3.Up, poleTop - 0.05f, poleR, 24, false, true, true, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(0f, 0.17f, 0f), new Vec3(1.0f, 0.05f, 1.0f), YawTransform.Identity, GalvanizedUv);
            ctx.Galvanized.AddBox(new Vec3(poleR + 0.02f, (lowY + highY) * 0.5f, 0f), new Vec3(0.05f, highY - lowY + 0.5f, 0.55f), YawTransform.Identity, GalvanizedUv);
            ctx.Galvanized.AddCylinder(new Vec3(0f, poleTop, 0f), Vec3.Up, 0.04f, poleR + 0.03f, 24, true, true, true, GalvanizedUv);
            // Diagonal tie from the pole top to the arm.
            AddStrut(ctx.Galvanized, new Vec3(0f, poleTop - 0.1f, 0f), new Vec3(armEnd * 0.45f, highY, armZ), 0.04f);

            AddBeacons(ctx, type, cabX, cab.X, top, cabFront);

            AssemblyMesh m = ctx.Result.Assembly;
            m.LegSize = new Vec3(poleR * 2f, cabBottom, poleR * 2f);
            m.CircularLeg = true;
            m.PostCount = 1;
        }

        private static void BuildGantry(Context ctx, DigitalSignType type)
        {
            float span = type.Span;
            float half = span * 0.5f;
            const float chordR = 0.09f;
            const float trussDepth = 1.1f;       // front to back
            const float lowY = 6.7f;
            const float highY = 8.1f;
            const float legR = 0.16f;
            const float cabBottom = 5.3f;
            const float cabDepth = 1.1f;
            const float border = 0.25f;
            float zf = trussDepth * 0.5f;
            float zb = -trussDepth * 0.5f;

            // Truss chords running the full span.
            foreach (float y in new[] { lowY, highY })
            {
                foreach (float z in new[] { zf, zb })
                    ctx.Galvanized.AddCylinder(new Vec3(-half - 0.25f, y, z), Vec3.Right, span + 0.5f, chordR, 12, true, true, true, GalvanizedUv);
            }

            int bays = Math.Max(4, (int)Math.Round(span / 1.5f));
            float bay = span / bays;
            for (int i = 0; i <= bays; i++)
            {
                float x = -half + i * bay;
                // Verticals on the front and back faces, cross members on top and bottom.
                ctx.Galvanized.AddCylinder(new Vec3(x, lowY, zf), Vec3.Up, highY - lowY, 0.04f, 8, false, false, true, GalvanizedUv);
                ctx.Galvanized.AddCylinder(new Vec3(x, lowY, zb), Vec3.Up, highY - lowY, 0.04f, 8, false, false, true, GalvanizedUv);
                ctx.Galvanized.AddCylinder(new Vec3(x, lowY, zb), Vec3.Forward, trussDepth, 0.04f, 8, false, false, true, GalvanizedUv);
                ctx.Galvanized.AddCylinder(new Vec3(x, highY, zb), Vec3.Forward, trussDepth, 0.04f, 8, false, false, true, GalvanizedUv);
                if (i == bays)
                    continue;
                bool even = i % 2 == 0;
                float x1 = x + bay;
                AddStrut(ctx.Galvanized, new Vec3(x, even ? lowY : highY, zf), new Vec3(x1, even ? highY : lowY, zf), 0.035f);
                AddStrut(ctx.Galvanized, new Vec3(x, even ? lowY : highY, zb), new Vec3(x1, even ? highY : lowY, zb), 0.035f);
                AddStrut(ctx.Galvanized, new Vec3(x, highY, even ? zb : zf), new Vec3(x1, highY, even ? zf : zb), 0.035f);
                AddStrut(ctx.Galvanized, new Vec3(x, lowY, even ? zb : zf), new Vec3(x1, lowY, even ? zf : zb), 0.035f);
            }

            // Legs: two-column towers with lacing, standing on concrete pedestals.
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * half;
                ctx.Concrete.AddBox(new Vec3(x, 0.1f, 0f), new Vec3(1.0f, 0.6f, trussDepth + 0.9f), YawTransform.Identity, ConcreteUv);
                ctx.Galvanized.AddBox(new Vec3(x, 0.42f, 0f), new Vec3(0.7f, 0.04f, trussDepth + 0.5f), YawTransform.Identity, GalvanizedUv);
                foreach (float z in new[] { zf, zb })
                    ctx.Galvanized.AddCylinder(new Vec3(x, 0.44f, z), Vec3.Up, highY + 0.25f - 0.44f, legR, 16, false, true, true, GalvanizedUv);
                int rungs = (int)Math.Round((lowY - 0.6f) / 1.2f);
                float step = (lowY - 0.6f) / rungs;
                for (int i = 0; i <= rungs; i++)
                {
                    float y = 0.6f + i * step;
                    ctx.Galvanized.AddCylinder(new Vec3(x, y, zb), Vec3.Forward, trussDepth, 0.04f, 8, false, false, true, GalvanizedUv);
                    if (i < rungs)
                        AddStrut(ctx.Galvanized, new Vec3(x, y, i % 2 == 0 ? zb : zf), new Vec3(x, y + step, i % 2 == 0 ? zf : zb), 0.035f);
                }
                // Leg cap plate where the truss sits.
                ctx.Galvanized.AddBox(new Vec3(x, highY + 0.27f, 0f), new Vec3(0.5f, 0.04f, trussDepth + 0.4f), YawTransform.Identity, GalvanizedUv);
            }

            // Walk-in cabinet hung in front of the truss.
            float cabBack = zf + 0.12f;
            float cabFront = cabBack + cabDepth;
            Vec2 cab = AddCabinet(ctx, type, 0f, cabBottom, cabFront, border, cabDepth);
            float top = cabBottom + cab.Y;
            foreach (float dx in new[] { -cab.X * 0.35f, -cab.X * 0.12f, cab.X * 0.12f, cab.X * 0.35f })
                ctx.Galvanized.AddBox(new Vec3(dx, (Math.Max(cabBottom + 0.4f, lowY - 0.3f) + highY + 0.1f) * 0.5f, (zf + cabBack) * 0.5f), new Vec3(0.14f, highY + 0.1f - Math.Max(cabBottom + 0.4f, lowY - 0.3f), cabBack - zf + 0.1f), YawTransform.Identity, GalvanizedUv);
            // Maintenance catwalk under the cabinet with a rail behind it.
            ctx.Galvanized.AddBox(new Vec3(0f, cabBottom - 0.05f, cabBack + cabDepth * 0.5f), new Vec3(cab.X + 0.4f, 0.04f, cabDepth + 0.4f), YawTransform.Identity, GalvanizedUv);
            foreach (float dx in new[] { -cab.X * 0.5f - 0.2f, cab.X * 0.5f + 0.2f })
                ctx.Galvanized.AddCylinder(new Vec3(dx, cabBottom - 0.05f, cabBack + cabDepth * 0.5f), Vec3.Up, lowY - cabBottom + 0.05f, 0.03f, 8, false, false, true, GalvanizedUv);

            AddBeacons(ctx, type, 0f, cab.X, top, cabFront);

            AssemblyMesh m = ctx.Result.Assembly;
            float legW = 1.0f;
            m.LegSize = new Vec3(legW, cabBottom - 0.2f, trussDepth + 0.9f);
            m.LegGapX = span - legW;
            m.CircularLeg = false;
            m.PostCount = 2;
        }

        private static void AddBeacons(Context ctx, DigitalSignType type, float centerX, float cabWidth, float top, float cabFront)
        {
            if (!type.HasBeacons)
                return;
            const float d = 0.25f;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = centerX + side * (cabWidth * 0.5f - 0.45f);
                // Short stand on the cabinet roof.
                ctx.Housing.AddBox(new Vec3(x, top + 0.06f, cabFront - 0.18f), new Vec3(0.16f, 0.12f, 0.16f), YawTransform.Identity, PaintUv);
                AddBeacon(ctx, new Vec3(x, top + 0.12f + d * 0.5f + 0.03f, cabFront - 0.04f), d);
            }
        }

        private static void AddFoundation(Context ctx, Vec3 at, float radius)
        {
            ctx.Concrete.AddCylinder(at + new Vec3(0f, -0.3f, 0f), Vec3.Up, 0.45f, radius, 24, false, true, true, ConcreteUv);
        }

        // ----------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------

        /// <summary>Round strut between two points.</summary>
        private static void AddStrut(MeshPart part, Vec3 from, Vec3 to, float radius)
        {
            Vec3 d = to - from;
            part.AddCylinder(from, d, d.Length, radius, 8, false, false, true, GalvanizedUv);
        }

        /// <summary>Rectangular tube between two points at the same height (yawed box).</summary>
        private static void AddBeam(MeshPart part, Vec3 from, Vec3 to, float width, float height)
        {
            Vec3 d = to - from;
            float len = d.Length;
            Vec3 mid = (from + to) * 0.5f;
            if (Math.Abs(d.Y) > 1e-3f)
            {
                // Sloped: approximate with a square cylinder.
                part.AddCylinder(from, d, len, width * 0.6f, 4, true, true, false, PaintUv);
                return;
            }
            float yaw = (float)Math.Atan2(d.X, d.Z);
            part.AddBox(Vec3.Zero, new Vec3(width, height, len), new YawTransform(mid, yaw), PaintUv);
        }

        private static void ShiftZ(Context ctx, float dz)
        {
            Vec3 offset = new Vec3(0f, 0f, dz);
            foreach (MeshPart part in ctx.Parts)
            {
                for (int i = 0; i < part.Positions.Count; i++)
                    part.Positions[i] = part.Positions[i] + offset;
            }
            DisplayRect display = ctx.Result.Display;
            display.Center = display.Center + offset;
            ctx.Result.Display = display;
            for (int i = 0; i < ctx.Result.Beacons.Count; i++)
            {
                BeaconLens b = ctx.Result.Beacons[i];
                b.Center = b.Center + offset;
                ctx.Result.Beacons[i] = b;
            }
        }

        private static void Finish(Context ctx)
        {
            AssemblyMesh result = ctx.Result.Assembly;
            foreach (MeshPart part in ctx.Parts)
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
            result.PanelArea = ctx.Result.Display.Width * ctx.Result.Display.Height;
        }
    }
}
