using System;

namespace USRoadSigns.Geometry
{
    public struct Vec2
    {
        public float X, Y;

        public Vec2(float x, float y)
        {
            X = x; Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-8f ? new Vec2(X / l, Y / l) : new Vec2(0f, 0f);
            }
        }

        /// <summary>Left-hand perpendicular (rotate +90 degrees counter-clockwise).</summary>
        public Vec2 PerpLeft => new Vec2(-Y, X);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public override string ToString() => "(" + X + ", " + Y + ")";
    }

    public struct Vec3
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Down = new Vec3(0f, -1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);
        public static readonly Vec3 Back = new Vec3(0f, 0f, -1f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);
        public static readonly Vec3 Left = new Vec3(-1f, 0f, 0f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-8f ? new Vec3(X / l, Y / l, Z / l) : new Vec3(0f, 0f, 0f);
            }
        }

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static Vec3 Min(Vec3 a, Vec3 b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static Vec3 Max(Vec3 a, Vec3 b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }

    /// <summary>Rigid transform restricted to a rotation around the vertical (Y) axis plus translation.</summary>
    public struct YawTransform
    {
        public Vec3 Offset;
        public float Cos, Sin;

        public YawTransform(Vec3 offset, float yawRadians)
        {
            Offset = offset;
            Cos = (float)Math.Cos(yawRadians);
            Sin = (float)Math.Sin(yawRadians);
        }

        public static YawTransform Identity => new YawTransform(Vec3.Zero, 0f);

        public Vec3 Point(Vec3 p) => new Vec3(p.X * Cos + p.Z * Sin, p.Y, -p.X * Sin + p.Z * Cos) + Offset;
        public Vec3 Direction(Vec3 d) => new Vec3(d.X * Cos + d.Z * Sin, d.Y, -d.X * Sin + d.Z * Cos);
    }

    public static class MathUtil
    {
        public const float Pi = (float)Math.PI;
        public const float Sqrt2 = 1.41421356f;
        public const float Sqrt3 = 1.73205081f;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
