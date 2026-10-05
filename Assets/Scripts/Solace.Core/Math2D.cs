// Solace.Core — 2D math primitives. No Unity types; X/Z plane (Y-up world).
using System;

namespace Solace.Core
{
    /// <summary>2D vector on the X/Z ground plane.</summary>
    public struct V2
    {
        public float X;
        public float Z;

        public V2(float x, float z) { X = x; Z = z; }

        public float Length() { return MathF.Sqrt(X * X + Z * Z); }
        public float LengthSq() { return X * X + Z * Z; }

        public V2 Normalized()
        {
            float len = Length();
            return len > 1e-6f ? new V2(X / len, Z / len) : new V2(0f, 0f);
        }

        public float Dot(V2 other) { return X * other.X + Z * other.Z; }

        public static float Distance(V2 a, V2 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public static float DistanceSq(V2 a, V2 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }

        public static V2 Lerp(V2 a, V2 b, float t)
        {
            return new V2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
        }

        public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Z + b.Z); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Z - b.Z); }
        public static V2 operator *(V2 a, float s) { return new V2(a.X * s, a.Z * s); }
        public static V2 operator /(V2 a, float s) { return new V2(a.X / s, a.Z / s); }
        public static V2 operator -(V2 a) { return new V2(-a.X, -a.Z); }

        public override string ToString() { return "(" + X + ", " + Z + ")"; }
    }

    /// <summary>Scalar helpers. All deterministic IEEE float math.</summary>
    public static class MathX
    {
        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        public static float InverseLerp(float a, float b, float v)
        {
            if (Math.Abs(b - a) < 1e-6f) return 0f;
            return Clamp01((v - a) / (b - a));
        }

        /// <summary>Smoothstep easing in [0,1] for t in [0,1].</summary>
        public static float SmoothStep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Smootherstep easing.</summary>
        public static float SmootherStep(float t)
        {
            t = Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>Wraps an angle to [-PI, PI].</summary>
        public static float WrapAngle(float radians)
        {
            while (radians > MathF.PI) radians -= MathF.PI * 2f;
            while (radians < -MathF.PI) radians += MathF.PI * 2f;
            return radians;
        }

        /// <summary>Yaw (radians) for a ground-plane direction. 0 faces +Z.</summary>
        public static float YawFromDir(V2 dir) { return MathF.Atan2(dir.X, dir.Z); }
    }
}
