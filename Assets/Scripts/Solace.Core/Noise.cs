// Solace.Core — deterministic 2D value noise + fractal Brownian motion.
// Hashing is pure integer math (no Math.Sin tricks), so results are identical
// across runs, platforms, and runtimes. Only IEEE float arithmetic is used.
using System;

namespace Solace.Core
{
    public static class Noise
    {
        /// <summary>Integer lattice hash → uint. Deterministic everywhere.</summary>
        public static uint Hash2(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = seed + 0x9E3779B9u;
                h ^= (uint)x * 374761393u;
                h ^= (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h;
            }
        }

        private static float Lattice01(int x, int y, uint seed)
        {
            return Hash2(x, y, seed) * (1.0f / 4294967295.0f);
        }

        /// <summary>
        /// Value noise in [0,1] with smoothstep interpolation between lattice points.
        /// </summary>
        public static float Value(float x, float y, uint seed)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            float xf = x - xi;
            float yf = y - yi;

            float u = MathX.SmootherStep(xf);
            float v = MathX.SmootherStep(yf);

            float a = Lattice01(xi, yi, seed);
            float b = Lattice01(xi + 1, yi, seed);
            float c = Lattice01(xi, yi + 1, seed);
            float d = Lattice01(xi + 1, yi + 1, seed);

            return MathX.Lerp(MathX.Lerp(a, b, u), MathX.Lerp(c, d, v), v);
        }

        /// <summary>
        /// Fractal Brownian motion in roughly [0,1]. Deterministic for the same
        /// (x, y, octaves, lacunarity, gain, seed).
        /// </summary>
        public static float Fbm(float x, float y, int octaves, float lacunarity, float gain, uint seed)
        {
            float sum = 0f;
            float amp = 0.5f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Value(x * freq, y * freq, seed + (uint)i * 1013904223u);
                norm += amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Ridged fBm in roughly [0,1]; good for craggy high ground.</summary>
        public static float Ridged(float x, float y, int octaves, float lacunarity, float gain, uint seed)
        {
            float sum = 0f;
            float amp = 0.5f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = Value(x * freq, y * freq, seed + (uint)i * 668265263u);
                float r = 1f - Math.Abs(2f * n - 1f);
                sum += amp * r * r;
                norm += amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return norm > 0f ? sum / norm : 0f;
        }
    }
}
