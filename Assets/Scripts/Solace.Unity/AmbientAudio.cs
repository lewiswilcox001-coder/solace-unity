// Solace.Unity — procedural ambient audio. Zero audio files: every sound is
// synthesized at startup with AudioClip.Create + SetData, seeded per world.
// A nature-documentary mix: wind, water, birdsong, crickets, owl, fox
// footsteps, rain, thunder (synced to DayNightCycle lightning), and a very
// subtle evolving ambient pad. All 2D (no positional audio — the camera is
// the ear). Volumes are mixed per-frame from sim state; no per-frame allocs.
//
// Self-bootstrapping via RuntimeInitializeOnLoadMethod (same pattern as
// AmbientLife): no changes to existing files required.
using System;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class AmbientAudio : MonoBehaviour, ISimView
    {
        // -- tuning ------------------------------------------------------------
        // Volumes now come from AccessibilitySettings (four channels + master).
        // Kept as a fallback default only.
        private const float DefaultMaster = 0.5f;      // gentle default
        private const float WindBase = 0.30f;
        private const float WaterNear = 0.55f;         // < 15u from river
        private const float PadLevel = 0.10f;         // felt, not heard
        private const float CricketLevel = 0.28f;
        private const KeyCode MuteKey = KeyCode.V;

        // -- state ---------------------------------------------------------------
        private bool _built;
        private int _builtSeed = int.MinValue;
        private bool _muted;
        private SeededRandom _rng;

        // Looping layers (one AudioSource each, loop = true).
        private AudioSource _windSrc;
        private AudioSource _waterSrc;
        private AudioSource _rainSrc;
        private AudioSource _cricketSrc;
        private AudioSource _padDaySrc;
        private AudioSource _padNightSrc;

        // One-shot voice (PlayOneShot, no extra sources needed).
        private AudioSource _oneShotSrc;

        // Synthesized one-shot clips.
        private AudioClip[] _chirps;      // 4 bird chirp variations
        private AudioClip _owlHoot;
        private AudioClip _footstep;
        private AudioClip _chuff;         // greeting bark
        private AudioClip[] _thunders;    // 3 rumble variations

        // Scheduling timers (real seconds).
        private float _nextChirp;
        private float _nextOwl;
        private float _nextStep;
        private float _thunderDelay = -1f; // counts down after a lightning strike
        private int _thunderIdx;

        /// <summary>
        /// Fired when an important audio cue plays (thunder, owl, greeting).
        /// Used by the accessibility caption system for visual indicators.
        /// </summary>
        public static event Action<string> OnAudioCue;

        /// <summary>
        /// Lets sibling audio systems (e.g. DynamicMusic) raise accessibility
        /// captions. Events can only be invoked from inside this class.
        /// </summary>
        public static void EmitCue(string cue)
        {
            OnAudioCue?.Invoke(cue);
        }

        // Edge detection.
        private string _lastGoal = "";
        private float _chuffDelay = -1f;

        // Smoothed volumes (avoid zipper noise from snapping).
        private float _vWind, _vWater, _vRain, _vCricket, _vPadDay, _vPadNight;

        // Cached per-frame sim reads.
        private float _foxX, _foxZ, _foxSpeed;
        private float _camX, _camZ;

        // -- bootstrap ----------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            // Singleton guard: scene reloads must not stack duplicate systems.
            if (UnityEngine.Object.FindObjectOfType<AmbientAudio>() != null) return;
            var go = new GameObject("AmbientAudio");
            go.AddComponent<AmbientAudio>();
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (!_built || boot.Seed != _builtSeed)
            {
                Teardown();
                _builtSeed = boot.Seed;
                Build(boot.Sim.State.World);
                _built = true;
            }
            if (Input.GetKeyDown(MuteKey)) _muted = !_muted;
            // Gamepad: D-down on the HUD toggles mute (V equivalent).
            if (GamepadInput.IsConnected && GamepadInput.GetButtonDown(PadButton.DDown))
            {
                var hud = boot.Hud;
                if (hud == null || !hud.IsAnyPanelOpen()) _muted = !_muted;
            }
            SyncFromState(boot.Sim.State);
            Schedule(boot.Sim.State, Time.deltaTime);
        }

        private void OnDestroy()
        {
            DayNightCycle.OnLightningStrike -= OnLightning;
            var boot = GameBootstrap.Instance;
            if (boot != null) boot.UnregisterView(this);
            Teardown();
        }

        public void SyncFromState(GameState state)
        {
            if (state.Agent != null)
            {
                _foxX = state.Agent.X;
                _foxZ = state.Agent.Z;
                _foxSpeed = state.Agent.Speed;
            }
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Camera != null)
            {
                _camX = boot.Camera.transform.position.x;
                _camZ = boot.Camera.transform.position.z;
            }

            float dt = Time.deltaTime;
            bool night = state.IsNight;
            var weather = state.Weather;

            // -- wind: breathes with weather ------------------------------------
            float windTarget = WindBase;
            if (weather == Weather.Cloudy) windTarget = 0.38f;
            else if (weather == Weather.Rain) windTarget = 0.48f;
            else if (weather == Weather.Storm) windTarget = 0.65f;
            if (night) windTarget += 0.04f;
            _vWind = Damp(_vWind, windTarget, dt);

            // -- water: proximity to the river -----------------------------------
            float waterTarget = 0f;
            float riverD = NearestRiverDistance(state.World, _camX, _camZ);
            if (riverD < 15f) waterTarget = WaterNear;
            else if (riverD < 40f) waterTarget = 0.28f;
            else if (riverD < 90f) waterTarget = 0.12f;
            _vWater = Damp(_vWater, waterTarget, dt);

            // -- rain -------------------------------------------------------------
            float rainTarget = 0f;
            if (weather == Weather.Rain) rainTarget = 0.50f;
            else if (weather == Weather.Storm) rainTarget = 0.68f;
            _vRain = Damp(_vRain, rainTarget, dt);

            // -- crickets: night only ----------------------------------------------
            float cricketTarget = night ? CricketLevel : 0f;
            if (weather == Weather.Storm || weather == Weather.Rain) cricketTarget *= 0.25f;
            _vCricket = Damp(_vCricket, cricketTarget, dt);

            // -- ambient pad: crossfade day/night -----------------------------------
            float dayW = night ? 0f : 1f;
            // Soften the transition around dawn/dusk.
            float t = state.TimeOfDay;
            float dawnDusk = Mathf.Clamp01(1f - Mathf.Abs(t - 6f) / 1.5f)
                           + Mathf.Clamp01(1f - Mathf.Abs(t - 18f) / 1.5f);
            dawnDusk = Mathf.Clamp01(dawnDusk);
            float padDayT = Mathf.Lerp(dayW, 0.5f, dawnDusk) * PadLevel;
            float padNightT = Mathf.Lerp(1f - dayW, 0.5f, dawnDusk) * PadLevel;
            _vPadDay = Damp(_vPadDay, padDayT, dt);
            _vPadNight = Damp(_vPadNight, padNightT, dt);

            // -- apply ---------------------------------------------------------------
            // Four accessibility channels: ambient (wind/water/rain/crickets),
            // music (pads), effects (one-shots via master in Schedule).
            float master = _muted ? 0f : AccessibilitySettings.MasterVolume;
            float ambient = AccessibilitySettings.AmbientVolume;
            float music = AccessibilitySettings.MusicVolume;
            _windSrc.volume = _vWind * master * ambient;
            _waterSrc.volume = _vWater * master * ambient;
            _rainSrc.volume = _vRain * master * ambient;
            _cricketSrc.volume = _vCricket * master * ambient;
            _padDaySrc.volume = _vPadDay * master * music;
            _padNightSrc.volume = _vPadNight * master * music;
        }

        // -- scheduling (one-shots) -------------------------------------------------

        private void Schedule(GameState state, float dt)
        {
            if (_muted) return;
            float master = AccessibilitySettings.MasterVolume * AccessibilitySettings.EffectsVolume;
            float t = state.TimeOfDay;
            bool night = state.IsNight;
            var weather = state.Weather;
            bool wet = weather == Weather.Rain || weather == Weather.Storm;

            // -- birdsong ------------------------------------------------------------
            _nextChirp -= dt;
            if (_nextChirp <= 0f && !night && !wet)
            {
                bool chorus = (t > 5f && t < 8.5f) || (t > 16.5f && t < 20f);
                _oneShotSrc.PlayOneShot(_chirps[_rng.NextInt(_chirps.Length)],
                    (chorus ? 0.55f : 0.35f) * master);
                _nextChirp = chorus ? _rng.NextFloat(1.5f, 6f) : _rng.NextFloat(7f, 18f);
            }

            // -- owl ------------------------------------------------------------------
            _nextOwl -= dt;
            if (_nextOwl <= 0f && night && !wet)
            {
                _oneShotSrc.PlayOneShot(_owlHoot, 0.40f * master);
                OnAudioCue?.Invoke("[owl hoots in the dark]");
                _nextOwl = _rng.NextFloat(45f, 120f);
            }

            // -- fox footsteps -----------------------------------------------------------
            _nextStep -= dt;
            if (_foxSpeed > 1.5f && _nextStep <= 0f)
            {
                // Only audible when the camera is near the fox.
                float d = Mathf.Sqrt((_foxX - _camX) * (_foxX - _camX)
                                   + (_foxZ - _camZ) * (_foxZ - _camZ));
                if (d < 70f)
                {
                    float vol = Mathf.Clamp01(_foxSpeed / 6f) * 0.35f
                              * (1f - d / 70f) * master;
                    _oneShotSrc.PlayOneShot(_footstep, vol);
                }
                _nextStep = 2.4f / _foxSpeed; // gait-timed
            }

            // -- greeting chuff (rising edge on the Greet goal) ---------------------------
            string goal = state.Agent != null ? state.Agent.CurrentGoal : "";
            if (goal == "Greet" && _lastGoal != "Greet") _chuffDelay = 0.6f;
            _lastGoal = goal;
            if (_chuffDelay > 0f)
            {
                _chuffDelay -= dt;
                if (_chuffDelay <= 0f)
                {
                    _oneShotSrc.PlayOneShot(_chuff, 0.45f * master);
                    OnAudioCue?.Invoke("[fox greets a kindred]");
                }
            }

            // -- thunder (delayed after the lightning flash) --------------------------------
            if (_thunderDelay > 0f)
            {
                _thunderDelay -= dt;
                if (_thunderDelay <= 0f)
                {
                    _oneShotSrc.PlayOneShot(_thunders[_thunderIdx], 0.80f * master);
                    OnAudioCue?.Invoke("[thunder rumbles]");
                }
            }
        }

        private void OnLightning()
        {
            // Sound lags light: 1–3.5 s of distance.
            _thunderIdx = _rng.NextInt(_thunders.Length);
            _thunderDelay = _rng.NextFloat(1f, 3.5f);
        }

        // -- construction ---------------------------------------------------------------

        private void Build(WorldData world)
        {
            _rng = SeededRandom.Derive(world.Seed, "audio");
            int rate = AudioSettings.outputSampleRate;

            // Ensure an AudioListener exists (2D sounds still need one).
            if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();

            _windSrc = MakeLoop("Wind", SynthWind(rate));
            _waterSrc = MakeLoop("Water", SynthWater(rate));
            _rainSrc = MakeLoop("Rain", SynthRain(rate));
            _cricketSrc = MakeLoop("Crickets", SynthCrickets(rate));
            _padDaySrc = MakeLoop("PadDay", SynthPad(rate, day: true));
            _padNightSrc = MakeLoop("PadNight", SynthPad(rate, day: false));

            _oneShotSrc = gameObject.AddComponent<AudioSource>();
            _oneShotSrc.loop = false;
            _oneShotSrc.playOnAwake = false;

            _chirps = new AudioClip[4];
            for (int i = 0; i < 4; i++) _chirps[i] = ToClip("Chirp" + i, SynthChirp(rate, i));
            _owlHoot = ToClip("Owl", SynthOwl(rate));
            _footstep = ToClip("Step", SynthFootstep(rate));
            _chuff = ToClip("Chuff", SynthChuff(rate));
            _thunders = new AudioClip[3];
            for (int i = 0; i < 3; i++) _thunders[i] = ToClip("Thunder" + i, SynthThunder(rate, i));

            DayNightCycle.OnLightningStrike += OnLightning;
            GameBootstrap.Instance.RegisterView(this);

            _nextChirp = 2f;
            _nextOwl = 20f;
        }

        private void Teardown()
        {
            DayNightCycle.OnLightningStrike -= OnLightning;
            // Destroy old sources so a seed-change rebuild doesn't stack them.
            foreach (var src in new AudioSource[] {
                _windSrc, _waterSrc, _rainSrc, _cricketSrc,
                _padDaySrc, _padNightSrc, _oneShotSrc })
            {
                if (src != null) UnityEngine.Object.Destroy(src);
            }
            _windSrc = _waterSrc = _rainSrc = _cricketSrc = null;
            _padDaySrc = _padNightSrc = _oneShotSrc = null;
            _built = false;
        }

        private AudioSource MakeLoop(string name, float[] samples)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = ToClip(name, samples);
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            src.Play();
            return src;
        }

        private static AudioClip ToClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1,
                AudioSettings.outputSampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // -- synthesis ------------------------------------------------------------------
        //
        // All generators write mono float arrays normalized to roughly [-1, 1].
        // Loop ends are crossfaded over the last 0.25 s so loops are seamless.

        private float[] SynthWind(int rate)
        {
            int n = rate * 8; // 8s loop (was 10s): saves memory + startup time
            var s = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = _rng.NextFloat(-1f, 1f);
                lp += 0.06f * (w - lp); // soft lowpass: airy, not hissy
                float t = (float)i / rate;
                // Slow breathing: two incommensurate LFOs.
                float lfo = 0.65f + 0.25f * Mathf.Sin(t * 0.45f)
                                 + 0.10f * Mathf.Sin(t * 1.13f + 1.7f);
                s[i] = lp * lfo * 2.2f;
            }
            return LoopFade(s, rate);
        }

        private float[] SynthWater(int rate)
        {
            int n = rate * 6; // 6s loop (was 8s)
            var s = new float[n];
            float lpFast = 0f, lpSlow = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = _rng.NextFloat(-1f, 1f);
                lpFast += 0.35f * (w - lpFast);
                lpSlow += 0.05f * (w - lpSlow);
                float band = lpFast - lpSlow; // crude bandpass: babble range
                float t = (float)i / rate;
                // Burble: amplitude wobble at babbling-brook rates.
                float burble = 0.70f + 0.30f * Mathf.Sin(t * 23f + Mathf.Sin(t * 5.1f) * 2f);
                s[i] = band * burble * 2.6f;
            }
            return LoopFade(s, rate);
        }

        private float[] SynthRain(int rate)
        {
            int n = rate * 6;
            var s = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = _rng.NextFloat(-1f, 1f);
                lp += 0.55f * (w - lp);
                float hiss = w - lp; // highpassed: patter, not rumble
                float t = (float)i / rate;
                float flutter = 0.75f + 0.25f * Mathf.Sin(t * 31f + Mathf.Sin(t * 7.3f));
                s[i] = hiss * flutter * 1.4f;
            }
            return LoopFade(s, rate);
        }

        private float[] SynthCrickets(int rate)
        {
            int n = rate * 4;
            var s = new float[n];
            // Pattern: 3 chirps (each 5 pulses @ 4.2 kHz), then a pause. Loops in 4 s.
            float freq = 4200f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float cycle = t % 1.333f; // 3 chirps per 4 s
                float env = 0f;
                if (cycle < 0.30f)
                {
                    // 5 pulses within the chirp.
                    float pt = (cycle / 0.30f) * 5f;
                    float pulse = Mathf.Max(0f, Mathf.Sin(pt * Mathf.PI * 2f));
                    env = pulse * pulse * (1f - cycle / 0.30f * 0.4f);
                }
                s[i] = Mathf.Sin(t * freq * Mathf.PI * 2f) * env * 0.5f;
            }
            return LoopFade(s, rate);
        }

        private float[] SynthPad(int rate, bool day)
        {
            int n = rate * 12; // 12s loop (was 20s): smooth evolving tones loop fine shorter
            var s = new float[n];
            // Day: warm C major-ish (C3/G3/E4). Night: cool A minor-ish (A2/E3/C4).
            float[] freqs = day
                ? new float[] { 130.81f, 196.00f, 329.63f }
                : new float[] { 110.00f, 164.81f, 261.63f };
            float[] amps = { 0.30f, 0.22f, 0.14f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float v = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    // Gentle detune pair + slow swell per voice.
                    float swell = 0.75f + 0.25f * Mathf.Sin(t * 0.21f + k * 2.1f);
                    v += amps[k] * swell * (
                        Mathf.Sin(t * freqs[k] * Mathf.PI * 2f) * 0.6f +
                        Mathf.Sin(t * freqs[k] * 1.003f * Mathf.PI * 2f) * 0.4f);
                }
                s[i] = v * 0.5f;
            }
            return LoopFade(s, rate);
        }

        private float[] SynthChirp(int rate, int variant)
        {
            // 0.12–0.28 s frequency sweep with vibrato: a tiny birdsong phrase.
            float dur = 0.14f + variant * 0.045f;
            int n = (int)(rate * dur);
            var s = new float[n];
            float f0 = 2600f + variant * 380f;
            float f1 = 3900f + variant * 420f;
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float f = Mathf.Lerp(f0, f1, u) * (1f + 0.06f * Mathf.Sin(u * 40f));
                phase += f / rate * Mathf.PI * 2f;
                float env = Mathf.Sin(u * Mathf.PI); // smooth in/out
                // Two quick syllables: dip in the middle.
                env *= 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2f));
                s[i] = Mathf.Sin(phase) * env * 0.7f;
            }
            return s;
        }

        private float[] SynthOwl(int rate)
        {
            // "hoo-hoo": two low notes, 340 Hz with a gentle downward slide.
            int n = (int)(rate * 1.1f);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = 0f;
                float f = 340f;
                if (t < 0.35f) { env = Mathf.Sin(t / 0.35f * Mathf.PI); f = 350f - t * 60f; }
                else if (t < 0.55f) { env = 0f; }
                else if (t < 0.95f)
                {
                    float u = (t - 0.55f) / 0.40f;
                    env = Mathf.Sin(u * Mathf.PI) * 0.85f;
                    f = 330f - u * 70f;
                }
                phase += f / rate * Mathf.PI * 2f;
                // Breathiness: a touch of filtered noise.
                float breath = _rng.NextFloat(-1f, 1f) * 0.06f;
                s[i] = (Mathf.Sin(phase) * 0.8f + breath) * env * 0.8f;
            }
            return s;
        }

        private float[] SynthFootstep(int rate)
        {
            // 90 ms soft thud: lowpassed noise burst with a fast decay.
            int n = (int)(rate * 0.09f);
            var s = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float w = _rng.NextFloat(-1f, 1f);
                lp += 0.25f * (w - lp);
                float env = Mathf.Exp(-u * 9f);
                s[i] = (lp * 1.6f + Mathf.Sin(u * 12f) * 0.25f) * env;
            }
            return s;
        }

        private float[] SynthChuff(int rate)
        {
            // 0.25 s breathy greeting: banded noise puff with pitch.
            int n = (int)(rate * 0.25f);
            var s = new float[n];
            float lp = 0f;
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float w = _rng.NextFloat(-1f, 1f);
                lp += 0.45f * (w - lp);
                phase += (420f - u * 180f) / rate * Mathf.PI * 2f;
                float env = Mathf.Sin(u * Mathf.PI);
                s[i] = (lp * 0.9f + Mathf.Sin(phase) * 0.35f) * env * 0.8f;
            }
            return s;
        }

        private float[] SynthThunder(int rate, int variant)
        {
            // 3 s rolling rumble: brown noise with slow attack, long decay.
            int n = rate * 3;
            var s = new float[n];
            float brown = 0f;
            float sub = 0f;
            float subF = 48f + variant * 7f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float u = (float)i / n;
                float w = _rng.NextFloat(-1f, 1f);
                brown = (brown + 0.02f * w) / 1.02f; // leaky integrator
                sub += subF / rate * Mathf.PI * 2f;
                // Rolling envelope: quick-ish attack, ragged decay.
                float env = Mathf.Min(1f, t / 0.18f) * Mathf.Exp(-u * 3.2f)
                          * (0.7f + 0.3f * Mathf.Sin(t * 9f + variant));
                s[i] = (brown * 3.2f + Mathf.Sin(sub) * 0.35f) * env;
            }
            return s;
        }

        // -- helpers ---------------------------------------------------------------------

        private static float[] LoopFade(float[] s, int rate)
        {
            // Crossfade the last 0.25 s into the first 0.25 s for seamless loops.
            int fade = rate / 4;
            if (fade * 2 > s.Length) return s;
            for (int i = 0; i < fade; i++)
            {
                float u = (float)i / fade;
                int a = s.Length - fade + i;
                s[i] = s[i] * u + s[a] * (1f - u);
            }
            return s;
        }

        private static float Damp(float cur, float target, float dt)
        {
            // Frame-rate-independent smoothing (~1.5 s to converge).
            return Mathf.Lerp(cur, target, 1f - Mathf.Exp(-2.5f * dt));
        }

        private static float NearestRiverDistance(WorldData world, float x, float z)
        {
            if (world == null || world.RiverPath == null || world.RiverPath.Count == 0)
                return float.MaxValue;
            float best = float.MaxValue;
            // Stride the path: plenty accurate for a volume falloff, 8x cheaper.
            for (int i = 0; i < world.RiverPath.Count; i += 8)
            {
                var p = world.RiverPath[i];
                float dx = x - p.X, dz = z - p.Z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }
    }
}
