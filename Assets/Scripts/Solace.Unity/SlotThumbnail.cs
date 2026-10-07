// Solace.Unity — procedural save-slot thumbnails.
//
// Each slot gets a small deterministic icon painted in code: a glowing
// lantern-core (the fox's light) over a seeded mountain dusk. No assets,
// no screenshots — the same seed always paints the same icon.
using System;
using System.IO;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public static class SlotThumbnail
    {
        public const int Size = 96;

        /// <summary>Renders and writes slotN-thumb.png. Best-effort: never throws.</summary>
        public static void WriteSlotThumbnail(string dir, int slot, int seed)
        {
            try
            {
                Texture2D tex = Render(seed);
                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                if (png == null || png.Length == 0) return;
                File.WriteAllBytes(SaveSlots.SlotThumbFile(dir, slot), png);
            }
            catch
            {
                // Thumbnails are cosmetic; a missing icon must never break a save.
            }
        }

        /// <summary>Loads a slot thumbnail, or null when absent/corrupt.</summary>
        public static Texture2D LoadSlotThumbnail(string dir, int slot)
        {
            try
            {
                string path = SaveSlots.SlotThumbFile(dir, slot);
                if (!File.Exists(path)) return null;
                byte[] png = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(png)) { UnityEngine.Object.Destroy(tex); return null; }
                return tex;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Paints the icon. Deterministic in seed.</summary>
        public static Texture2D Render(int seed)
        {
            var rng = new System.Random(seed);
            int S = Size;
            var px = new Color[S * S];

            // Dusk palette, hue-shifted by seed.
            float huePick = (float)rng.NextDouble();
            Color top, bottom;
            if (huePick < 0.34f)      { top = new Color(0.10f, 0.08f, 0.22f); bottom = new Color(0.16f, 0.22f, 0.30f); }
            else if (huePick < 0.67f) { top = new Color(0.06f, 0.14f, 0.20f); bottom = new Color(0.10f, 0.26f, 0.28f); }
            else                      { top = new Color(0.20f, 0.10f, 0.18f); bottom = new Color(0.32f, 0.20f, 0.22f); }

            for (int y = 0; y < S; y++)
            {
                float t = (float)y / (S - 1);
                Color c = Color.Lerp(bottom, top, t);
                for (int x = 0; x < S; x++) px[y * S + x] = c;
            }

            // Stars in the upper sky.
            for (int i = 0; i < 42; i++)
            {
                int x = rng.Next(S), y = rng.Next(S / 3, S);
                float b = 0.35f + 0.65f * (float)rng.NextDouble();
                px[y * S + x] = Color.Lerp(px[y * S + x], Color.white, b * 0.8f);
            }

            // Mountain silhouettes, two layers.
            DrawRidge(px, S, rng, 0.30f, new Color(0.08f, 0.10f, 0.16f));
            DrawRidge(px, S, rng, 0.16f, new Color(0.05f, 0.07f, 0.12f));

            // The lantern core: a glowing diamond with fox-ear hints.
            float cx = S * 0.5f, cy = S * 0.42f, r = S * 0.16f;
            Color glow = new Color(1f, 0.78f, 0.38f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = Math.Abs(x - cx), dy = Math.Abs(y - cy);
                    float d = dx + dy; // diamond distance
                    if (d < r * 2.2f)
                    {
                        float g = Math.Max(0f, 1f - d / (r * 2.2f));
                        g *= g;
                        int idx = y * S + x;
                        px[idx] = Color.Lerp(px[idx], glow, g * 0.9f);
                        if (d < r) px[idx] = Color.Lerp(px[idx], Color.white, (1f - d / r) * 0.75f);
                    }
                }
            // Ears: two small triangles above the core.
            DrawTriangle(px, S, cx - r * 0.55f, cy + r * 0.75f, cx - r * 0.10f, cy + r * 0.75f, cx - r * 0.38f, cy + r * 1.55f, new Color(0.85f, 0.55f, 0.25f));
            DrawTriangle(px, S, cx + r * 0.10f, cy + r * 0.75f, cx + r * 0.55f, cy + r * 0.75f, cx + r * 0.38f, cy + r * 1.55f, new Color(0.85f, 0.55f, 0.25f));

            // Vignette.
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - cx) / (S * 0.5f), dy = (y - cy) / (S * 0.5f);
                    float v = 1f - 0.45f * Math.Min(1f, dx * dx + dy * dy);
                    int idx = y * S + x;
                    px[idx] = new Color(px[idx].r * v, px[idx].g * v, px[idx].b * v, 1f);
                }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private static void DrawRidge(Color[] px, int S, System.Random rng, float baseY, Color c)
        {
            float y0 = S * baseY;
            int peaks = 3 + rng.Next(3);
            float[] xs = new float[peaks + 1];
            float[] ys = new float[peaks + 1];
            for (int i = 0; i <= peaks; i++)
            {
                xs[i] = S * i / peaks;
                ys[i] = y0 + (float)rng.NextDouble() * S * 0.16f;
            }
            for (int x = 0; x < S; x++)
            {
                float t = (float)x / S * peaks;
                int i0 = Math.Min((int)t, peaks - 1);
                float f = t - i0;
                float ridge = ys[i0] * (1f - f) + ys[i0 + 1] * f;
                for (int y = 0; y < ridge && y < S; y++)
                    px[y * S + x] = c;
            }
        }

        private static void DrawTriangle(Color[] px, int S, float x0, float y0, float x1, float y1, float x2, float y2, Color c)
        {
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
            int maxX = Math.Min(S - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
            int maxY = Math.Min(S - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
            float denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (Math.Abs(denom) < 0.0001f) return;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float w0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / denom;
                    float w1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / denom;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                        px[y * S + x] = c;
                }
        }
    }
}
