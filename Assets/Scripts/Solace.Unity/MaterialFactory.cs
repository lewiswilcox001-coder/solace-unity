// Solace.Unity — cached, code-generated materials. Everything is opaque:
// no alpha-blended transparency anywhere (avoids URP keyword/variant risk).
// Uses only UnityEngine core APIs: Shader.Find, Material, Color.
using System.Collections.Generic;
using UnityEngine;

namespace Solace.Unity
{
    /// <summary>
    /// Factory for the handful of shared URP materials this layer needs.
    /// Materials are cached by key; callers must never mutate a returned
    /// material except through SetEmission on a material they own (water).
    /// </summary>
    public static class MaterialFactory
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static Shader _lit;
        private static Shader _unlit;
        private static bool _shadersResolved;

        private static void ResolveShaders()
        {
            if (_shadersResolved) return;
            _shadersResolved = true;
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            _unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (_lit == null)
            {
                Debug.LogError("[Solace] URP Lit shader not found. Materials will be broken; " +
                               "ensure the Universal RP package is installed.");
            }
        }

        private static string Key(string kind, Color c, float a, float b)
        {
            return kind + "|" + ColorToKey(c) + "|" + a.ToString("F3") + "|" + b.ToString("F3");
        }

        private static string ColorToKey(Color c)
        {
            return ((int)(c.r * 255)) + "," + ((int)(c.g * 255)) + "," +
                   ((int)(c.b * 255)) + "," + ((int)(c.a * 255));
        }

        /// <summary>Opaque URP Lit material, flat color.</summary>
        public static Material Lit(Color color, float smoothness = 0.7f)
        {
            ResolveShaders();
            string k = Key("lit", color, smoothness, 0f);
            Material m;
            if (Cache.TryGetValue(k, out m)) return m;
            m = new Material(_lit);
            m.color = color;
            m.SetFloat("_Smoothness", smoothness);
            Cache[k] = m;
            return m;
        }

        /// <summary>Opaque URP Lit material with an emissive term (glow-moss, berries, eyes, water).</summary>
        public static Material LitEmissive(Color color, Color emission, float smoothness = 0.5f)
        {
            ResolveShaders();
            string k = "lite" + "|" + ColorToKey(color) + "|" + ColorToKey(emission) + "|" + smoothness.ToString("F3");
            Material m;
            if (Cache.TryGetValue(k, out m)) return m;
            m = new Material(_lit);
            m.color = color;
            m.SetFloat("_Smoothness", smoothness);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColorId, emission);
            Cache[k] = m;
            return m;
        }

        /// <summary>Opaque unlit material (sky dots, distant accents). Falls back to Lit if Unlit is missing.</summary>
        public static Material Unlit(Color color)
        {
            ResolveShaders();
            string k = Key("unlit", color, 0f, 0f);
            Material m;
            if (Cache.TryGetValue(k, out m)) return m;
            m = new Material(_unlit != null ? _unlit : _lit);
            m.color = color;
            Cache[k] = m;
            return m;
        }

        /// <summary>
        /// Animates the emission of a material the caller owns (not a cached
        /// shared one — pass a material created via NewLitEmissiveInstance).
        /// </summary>
        public static void SetEmission(Material m, Color emission)
        {
            if (m == null) return;
            m.SetColor(EmissionColorId, emission);
        }

        /// <summary>An owned (non-cached) emissive material for things animated per-frame, e.g. water.</summary>
        public static Material NewLitEmissiveInstance(Color color, Color emission, float smoothness = 0.5f)
        {
            ResolveShaders();
            var m = new Material(_lit);
            m.color = color;
            m.SetFloat("_Smoothness", smoothness);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColorId, emission);
            return m;
        }
    }
}
