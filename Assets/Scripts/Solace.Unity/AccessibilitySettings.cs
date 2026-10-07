// Solace.Unity — accessibility & comfort settings.
//
// Central, PlayerPrefs-backed settings for accessibility options.
// All values are static with a Changed event so live UI and systems
// can react immediately. Presentation only — never touches sim state.
using System;
using UnityEngine;

namespace Solace.Unity
{
    /// <summary>
    /// Global accessibility settings. Persisted via PlayerPrefs.
    /// Fire <see cref="Changed"/> after mutating to notify live systems.
    /// </summary>
    public static class AccessibilitySettings
    {
        // -- colorblind ---------------------------------------------------------
        // 0 = off, 1 = Protanopia, 2 = Deuteranopia, 3 = Tritanopia.
        // These select CVD *simulation* filters (Machado 2009 matrices) in the
        // photo-grade shader — useful for verifying that important colors
        // (fox glow, berries, predator eyes) stay distinguishable.

        public static int ColorblindMode
        {
            get { return PlayerPrefs.GetInt("solace.a11y.cvd", 0); }
            set { PlayerPrefs.SetInt("solace.a11y.cvd", Mathf.Clamp(value, 0, 3)); Changed?.Invoke(); }
        }

        public static string ColorblindName(int mode)
        {
            switch (mode)
            {
                case 1: return "Protanopia";
                case 2: return "Deuteranopia";
                case 3: return "Tritanopia";
                default: return "Off";
            }
        }

        // -- UI scale ------------------------------------------------------------
        // Index into UiScales: 0 = small, 1 = medium, 2 = large.

        public static readonly float[] UiScales = { 0.85f, 1.0f, 1.25f };
        public static readonly string[] UiScaleNames = { "Small", "Medium", "Large" };

        public static int UiScaleIndex
        {
            get { return PlayerPrefs.GetInt("solace.a11y.uiscale", 1); }
            set { PlayerPrefs.SetInt("solace.a11y.uiscale", Mathf.Clamp(value, 0, 2)); Changed?.Invoke(); }
        }

        public static float UiScale => UiScales[Mathf.Clamp(UiScaleIndex, 0, 2)];

        // -- motion sensitivity ----------------------------------------------------
        // CameraSway: 0 = off, 1 = reduced, 2 = full (default).
        // ReduceMotion: when true, the opening vista sweep becomes a gentle
        // fade and auto camera reframes use fades instead of cuts.

        public static int CameraSway
        {
            get { return PlayerPrefs.GetInt("solace.a11y.sway", 2); }
            set { PlayerPrefs.SetInt("solace.a11y.sway", Mathf.Clamp(value, 0, 2)); Changed?.Invoke(); }
        }

        public static string CameraSwayName(int v)
        {
            switch (v)
            {
                case 0: return "Off";
                case 1: return "Reduced";
                default: return "Full";
            }
        }

        /// <summary>Multiplier for handheld drift amplitude (0, 0.3, 1).</summary>
        public static float SwayFactor
        {
            get
            {
                switch (CameraSway)
                {
                    case 0: return 0f;
                    case 1: return 0.3f;
                    default: return 1f;
                }
            }
        }

        public static bool ReduceMotion
        {
            get { return PlayerPrefs.GetInt("solace.a11y.reducemotion", 0) != 0; }
            set { PlayerPrefs.SetInt("solace.a11y.reducemotion", value ? 1 : 0); Changed?.Invoke(); }
        }

        // -- audio ------------------------------------------------------------------
        // Four independent channels, each 0..1. Master scales everything.
        // Ambient = wind/water/rain/crickets. Music = day/night pads.
        // Effects = one-shots (chirps, owl, footsteps, chuff, thunder).

        public static float MasterVolume
        {
            get { return PlayerPrefs.GetFloat("solace.a11y.vol.master", 0.5f); }
            set { PlayerPrefs.SetFloat("solace.a11y.vol.master", Mathf.Clamp01(value)); Changed?.Invoke(); }
        }

        public static float AmbientVolume
        {
            get { return PlayerPrefs.GetFloat("solace.a11y.vol.ambient", 1f); }
            set { PlayerPrefs.SetFloat("solace.a11y.vol.ambient", Mathf.Clamp01(value)); Changed?.Invoke(); }
        }

        public static float MusicVolume
        {
            get { return PlayerPrefs.GetFloat("solace.a11y.vol.music", 1f); }
            set { PlayerPrefs.SetFloat("solace.a11y.vol.music", Mathf.Clamp01(value)); Changed?.Invoke(); }
        }

        public static float EffectsVolume
        {
            get { return PlayerPrefs.GetFloat("solace.a11y.vol.effects", 1f); }
            set { PlayerPrefs.SetFloat("solace.a11y.vol.effects", Mathf.Clamp01(value)); Changed?.Invoke(); }
        }

        /// <summary>
        /// When true, important audio cues also show a brief text caption
        /// (e.g. "[thunder rumbles]"). Thunder already flashes visually.
        /// </summary>
        public static bool AudioCaptions
        {
            get { return PlayerPrefs.GetInt("solace.a11y.captions", 0) != 0; }
            set { PlayerPrefs.SetInt("solace.a11y.captions", value ? 1 : 0); Changed?.Invoke(); }
        }

        // -- events -------------------------------------------------------------------

        public static event Action Changed;

        public static void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey("solace.a11y.cvd");
            PlayerPrefs.DeleteKey("solace.a11y.uiscale");
            PlayerPrefs.DeleteKey("solace.a11y.sway");
            PlayerPrefs.DeleteKey("solace.a11y.reducemotion");
            PlayerPrefs.DeleteKey("solace.a11y.vol.master");
            PlayerPrefs.DeleteKey("solace.a11y.vol.ambient");
            PlayerPrefs.DeleteKey("solace.a11y.vol.music");
            PlayerPrefs.DeleteKey("solace.a11y.vol.effects");
            PlayerPrefs.DeleteKey("solace.a11y.captions");
            Changed?.Invoke();
        }
    }
}
