// Solace.Unity — dynamic emotional music. Zero audio files: every layer is
// synthesized at startup with AudioClip.Create + SetData, seeded per world.
// Five emotional layers crossfade seamlessly (2-3 s) driven by game state:
//   Peace     — the musical bed; warm pad + gentle pentatonic plucks. Hushes
//               everything else during rest/sleep.
//   Danger    — low pulsing drone + dissonant rub; swells as predators close in.
//               Intensity scales with predator distance and the fox's health.
//   Combat    — driving D-minor ostinato + drum pulse; answers InCombat.
//   Discovery — shimmering bell arpeggio; sparkles on Discovery journal events.
//   Sad       — sparse cello lament in A minor; mourns on death/succession and
//               pre-mourns when health is critical.
//
// All 2D (the camera is the ear). Volumes ride the Music channel from
// AccessibilitySettings. No per-frame allocations; journal is only scanned
// when its count changes. Self-bootstrapping via RuntimeInitializeOnLoadMethod
// (same pattern as AmbientAudio): no changes to existing files required.
using System.Collections.Generic;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class DynamicMusic : MonoBehaviour, ISimView
    {
        // -- tuning -----------------------------------------------------------------
        private const int LoopSec = 12;          // every layer loops over 12 s
        private const float PeaceLevel = 0.16f;  // felt bed, never loud
        private const float DangerLevel = 0.30f;
        private const float CombatLevel = 0.32f;
        private const float DiscoveryLevel = 0.28f;
        private const float SadLevel = 0.26f;
        private const float CrossfadeK = 1.4f;   // ~2.1 s to 95% — the 2-3 s crossfade
        private const float CombatAttackK = 2.0f;// combat answers a touch faster (~1.6 s)
        private const KeyCode MuteKey = KeyCode.V;

        // -- state -------------------------------------------------------------------
        private bool _built;
        private int _builtSeed = int.MinValue;
        private bool _muted = true; // start muted until procedural audio is tuned
        private SeededRandom _rng;

        // One looping source per emotional layer.
        private AudioSource _peaceSrc;
        private AudioSource _dangerSrc;
        private AudioSource _combatSrc;
        private AudioSource _discoverySrc;
        private AudioSource _sadSrc;

        // Smoothed layer gains (no zipper noise, no popping).
        private float _vPeace, _vDanger, _vCombat, _vDiscovery, _vSad;

        // Impulse envelopes (real seconds): discovery sparkle, death lament.
        private bool _discoveryActive;
        private float _discoveryT;
        private bool _sadActive;
        private float _sadT;

        // Edge detection.
        private int _lastJournalCount = -1;
        private float _lastEventTime = -1f;
        private int _lastGeneration = 1;

        // -- bootstrap -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            // Singleton guard: scene reloads must not stack duplicate systems.
            if (UnityEngine.Object.FindObjectOfType<DynamicMusic>() != null) return;
            var go = new GameObject("DynamicMusic");
            go.AddComponent<DynamicMusic>();
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (!_built || boot.Seed != _builtSeed)
            {
                Teardown();
                _builtSeed = boot.Seed;
                Build();
                _built = true;
            }
            if (Input.GetKeyDown(MuteKey)) _muted = !_muted;
            SyncFromState(boot.Sim.State);
        }

        private void OnDestroy()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null) boot.UnregisterView(this);
            Teardown();
        }

        // -- adaptive mixing ---------------------------------------------------------------

        public void SyncFromState(GameState state)
        {
            float dt = Time.deltaTime;
            var agent = state.Agent;

            // -- sense: nearest living predator -------------------------------------------
            float nearestPred = float.MaxValue;
            if (agent != null)
            {
                foreach (var e in state.Entities)
                {
                    if (e.Kind != EntityKind.Predator || !e.IsAlive) continue;
                    float dx = e.X - agent.X, dz = e.Z - agent.Z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < nearestPred) nearestPred = d;
                }
            }

            // -- events: discovery sparkles (only scan when the journal grows) --------------
            int jc = state.Journal.Count;
            if (jc != _lastJournalCount)
            {
                _lastJournalCount = jc;
                List<JournalEntry> recent = state.Journal.Recent(8);
                for (int i = 0; i < recent.Count; i++)
                {
                    JournalEntry entry = recent[i];
                    if (entry.Time <= _lastEventTime) continue;
                    _lastEventTime = entry.Time;
                    if (entry.Category == JournalCategory.Discovery && !_discoveryActive)
                    {
                        _discoveryActive = true;
                        _discoveryT = 0f;
                        AmbientAudio.EmitCue("[music sparkles — a discovery]");
                    }
                }
            }

            // -- events: death / succession --------------------------------------------------
            if (agent != null && agent.Generation > _lastGeneration)
            {
                _lastGeneration = agent.Generation;
                _sadActive = true;
                _sadT = 0f;
                AmbientAudio.EmitCue("[mournful strings — a life ends]");
            }

            // -- impulse envelopes (real time; they play even through death) -----------------------
            float discoveryT = 0f, sadT = 0f;
            if (_discoveryActive)
            {
                _discoveryT += dt;
                discoveryT = Envelope(_discoveryT, 2f, 6f, 15f); // attack 2 s, hold 6 s, release 15 s
                if (_discoveryT > 23f) _discoveryActive = false;
            }
            if (_sadActive)
            {
                _sadT += dt;
                sadT = Envelope(_sadT, 4f, 20f, 30f); // attack 4 s, hold 20 s, release 30 s
                if (_sadT > 54f) _sadActive = false;
            }

            // -- targets ----------------------------------------------------------------------
            float dangerT = 0f, combatT = 0f, peaceT = 0f;

            if (agent != null && agent.IsAlive)
            {
                // Danger: swells with predator proximity; sharper when hurt or fleeing.
                float prox = 1f - Mathf.Clamp01((nearestPred - 12f) / 48f);
                if (agent.CurrentGoal == "Flee") prox = Mathf.Max(prox, 0.75f);
                float healthMod = 0.7f + 0.6f * (1f - Mathf.Clamp01(agent.Health / 100f));
                dangerT = Mathf.Clamp01(prox * healthMod);

                // Combat: answers InCombat.
                combatT = agent.InCombat ? 1f : 0f;

                // Pre-mourn: the music grieves before death when health is critical.
                if (agent.Health < 20f) sadT = Mathf.Max(sadT, 0.45f);

                // Peace: the bed. Hushes during rest/sleep; ducks under the other layers.
                peaceT = 0.45f;
                if (agent.CurrentGoal == "Rest") peaceT = 0.85f;
                if (state.IsNight) peaceT += 0.10f;
                float other = Mathf.Max(Mathf.Max(_vDanger, _vCombat), Mathf.Max(_vDiscovery, _vSad));
                peaceT *= 1f - 0.65f * Mathf.Clamp01(other);
                peaceT = Mathf.Clamp01(peaceT);
            }

            // -- smooth: 2-3 s crossfades, never a pop ------------------------------------------
            _vDanger = Damp(_vDanger, dangerT, CrossfadeK, dt);
            _vCombat = DampCombat(_vCombat, combatT, dt);
            _vDiscovery = Damp(_vDiscovery, discoveryT, CrossfadeK, dt);
            _vSad = Damp(_vSad, sadT, CrossfadeK, dt);
            _vPeace = Damp(_vPeace, peaceT, CrossfadeK, dt);

            // -- apply ---------------------------------------------------------------------------
            float master = _muted ? 0f : AccessibilitySettings.MasterVolume;
            float music = AccessibilitySettings.MusicVolume;
            _peaceSrc.volume = _vPeace * PeaceLevel * master * music;
            _dangerSrc.volume = _vDanger * DangerLevel * master * music;
            _combatSrc.volume = _vCombat * CombatLevel * master * music;
            _discoverySrc.volume = _vDiscovery * DiscoveryLevel * master * music;
            _sadSrc.volume = _vSad * SadLevel * master * music;
        }

        // -- construction -------------------------------------------------------------------------

        private void Build()
        {
            _rng = SeededRandom.Derive(_builtSeed, "music");
            int rate = AudioSettings.outputSampleRate;

            if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();

            _peaceSrc = MakeLoop("MusicPeace", SynthPeace(rate));
            _dangerSrc = MakeLoop("MusicDanger", SynthDanger(rate));
            _combatSrc = MakeLoop("MusicCombat", SynthCombat(rate));
            _discoverySrc = MakeLoop("MusicDiscovery", SynthDiscovery(rate));
            _sadSrc = MakeLoop("MusicSad", SynthSad(rate));

            GameBootstrap.Instance.RegisterView(this);

            // Initialize edge detectors from current state so loading a save
            // (or a seed-change rebuild) doesn't retrigger old events.
            var sim = GameBootstrap.Instance.Sim;
            _lastJournalCount = -1;
            _lastEventTime = -1f;
            _lastGeneration = 1;
            if (sim != null)
            {
                _lastJournalCount = sim.State.Journal.Count;
                List<JournalEntry> newest = sim.State.Journal.Recent(1);
                if (newest.Count > 0) _lastEventTime = newest[0].Time;
                if (sim.State.Agent != null) _lastGeneration = sim.State.Agent.Generation;
            }
            _discoveryActive = false;
            _sadActive = false;
            _vPeace = _vDanger = _vCombat = _vDiscovery = _vSad = 0f;
        }

        private void Teardown()
        {
            foreach (var src in new AudioSource[] {
                _peaceSrc, _dangerSrc, _combatSrc, _discoverySrc, _sadSrc })
            {
                if (src != null) UnityEngine.Object.Destroy(src);
            }
            _peaceSrc = _dangerSrc = _combatSrc = _discoverySrc = _sadSrc = null;
            _built = false;
        }

        private AudioSource MakeLoop(string name, float[] samples)
        {
            var src = gameObject.AddComponent<AudioSource>();
            var clip = AudioClip.Create(name, samples.Length, 1,
                AudioSettings.outputSampleRate, false);
            clip.SetData(samples, 0);
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            src.Play();
            return src;
        }

        // -- synthesis ------------------------------------------------------------------------------
        //
        // Five 12-second loops, all mono, normalized to roughly [-1, 1].
        // Loop ends are crossfaded over the last 0.25 s so loops are seamless.
        // Melodies are picked from precomposed phrases by world seed — each
        // world gets its own musical theme.

        private float[] SynthPeace(int rate)
        {
            int n = rate * LoopSec;
            var s = new float[n];
            // Warm C-major pad through the whole loop.
            AddPad(s, rate, 0f, LoopSec, 130.81f, 0.070f); // C3
            AddPad(s, rate, 0f, LoopSec, 196.00f, 0.060f); // G3
            AddPad(s, rate, 0f, LoopSec, 261.63f, 0.050f); // C4
            AddPad(s, rate, 0f, LoopSec, 329.63f, 0.040f); // E4
            // Gentle pentatonic pluck phrase (seeded).
            int phrase = _rng.NextInt(3);
            float[][] phrases = {
                new float[] { 329.63f, 392.00f, 440.00f, 392.00f, 329.63f, 293.66f, 261.63f },
                new float[] { 261.63f, 329.63f, 392.00f, 523.25f, 493.88f, 392.00f, 329.63f },
                new float[] { 392.00f, 440.00f, 392.00f, 329.63f, 293.66f, 329.63f, 261.63f },
            };
            float[] times = { 0.5f, 2.0f, 3.5f, 5.5f, 7.0f, 8.5f, 10.0f };
            float[] ph = phrases[phrase];
            for (int i = 0; i < ph.Length; i++)
                AddPluck(s, rate, times[i], ph[i], 0.16f);
            return LoopFade(s, rate);
        }

        private float[] SynthDanger(int rate)
        {
            int n = rate * LoopSec;
            var s = new float[n];
            // Low pulsing drone: A1 + E2, slow menacing throb.
            AddDrone(s, rate, 0f, LoopSec, 55.00f, 0.120f, 0.7f);  // A1
            AddDrone(s, rate, 0f, LoopSec, 82.41f, 0.100f, 0.7f);  // E2
            // Minor-2nd rub: Bb1 beating against the A1 — the unease.
            AddDrone(s, rate, 0f, LoopSec, 58.27f, 0.045f, 0.45f); // Bb1
            // Held-breath high tone swelling mid-loop.
            AddPad(s, rate, 3f, 6f, 880.00f, 0.025f);             // A5
            return LoopFade(s, rate);
        }

        private float[] SynthCombat(int rate)
        {
            int n = rate * LoopSec;
            var s = new float[n];
            // Driving D-minor ostinato: eighth notes every 0.3 s.
            float D3 = 146.83f, E3 = 164.81f, F3 = 174.61f;
            float G3 = 196.00f, A3 = 220.00f, C3 = 130.81f;
            float[][] bars = {
                new float[] { D3, D3, F3, D3, E3, D3, C3, D3, D3, D3 },
                new float[] { D3, D3, F3, D3, G3, F3, E3, D3, C3, D3 },
                new float[] { D3, D3, F3, D3, A3, G3, F3, E3, D3, C3 },
                new float[] { D3, D3, F3, D3, E3, F3, G3, F3, E3, D3 },
            };
            for (int b = 0; b < 4; b++)
                for (int i = 0; i < 10; i++)
                    AddPluck(s, rate, b * 3f + i * 0.3f, bars[b][i], 0.14f, 0.5f);
            // Drum pulse every 0.6 s.
            for (int i = 0; i < 20; i++)
                AddDrum(s, rate, i * 0.6f, 0.22f);
            // Bass throb every 1.2 s.
            for (int i = 0; i < 10; i++)
                AddPluck(s, rate, i * 1.2f, 73.42f, 0.10f, 0.6f); // D2
            return LoopFade(s, rate);
        }

        private float[] SynthDiscovery(int rate)
        {
            int n = rate * LoopSec;
            var s = new float[n];
            // Shimmering bell arpeggio climbing, then settling.
            float[] notes = {
                523.25f, 659.26f, 783.99f, 1046.50f, 1318.51f,
                1567.98f, 1318.51f, 1046.50f, 783.99f
            };
            for (int i = 0; i < notes.Length; i++)
                AddBell(s, rate, 0.5f + i * 1.2f, notes[i], 0.15f);
            // Faint high shimmer pad underneath.
            AddPad(s, rate, 0f, LoopSec, 523.25f, 0.030f);  // C5
            AddPad(s, rate, 0f, LoopSec, 783.99f, 0.025f);  // G5
            return LoopFade(s, rate);
        }

        private float[] SynthSad(int rate)
        {
            int n = rate * LoopSec;
            var s = new float[n];
            // Descending cello lament.
            AddCello(s, rate, 0.0f, 2.5f, 220.00f, 0.20f);  // A3
            AddCello(s, rate, 2.5f, 2.5f, 196.00f, 0.20f);  // G3
            AddCello(s, rate, 5.0f, 2.0f, 174.61f, 0.19f);  // F3
            AddCello(s, rate, 7.0f, 2.5f, 164.81f, 0.20f);  // E3
            AddCello(s, rate, 9.5f, 2.5f, 146.83f, 0.21f);  // D3
            // Low mourning pad.
            AddPad(s, rate, 0f, LoopSec, 110.00f, 0.050f);  // A2
            AddPad(s, rate, 0f, LoopSec, 164.81f, 0.040f);  // E3
            return LoopFade(s, rate);
        }

        // -- note voices -------------------------------------------------------------------------------

        private static void AddPad(float[] s, int rate, float startSec, float durSec, float freq, float amp)
        {
            int start = (int)(startSec * rate);
            int n = (int)(durSec * rate);
            if (start >= s.Length) return;
            n = System.Math.Min(n, s.Length - start);
            if (n <= 0) return;
            float attack = durSec * 0.3f;
            float release = durSec * 0.3f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float envA = Mathf.Min(1f, t / attack);
                float envR = Mathf.Min(1f, (durSec - t) / release);
                float env = envA * envR;
                env *= env; // gentler shoulders
                float v = Mathf.Sin(t * freq * Mathf.PI * 2f)
                        + 0.30f * Mathf.Sin(t * freq * 2f * Mathf.PI * 2f)
                        + 0.15f * Mathf.Sin(t * freq * 3f * Mathf.PI * 2f);
                s[start + i] += v * env * amp * 0.5f;
            }
        }

        private static void AddPluck(float[] s, int rate, float startSec, float freq, float amp, float decaySec = 1.5f)
        {
            int start = (int)(startSec * rate);
            if (start >= s.Length || start < 0) return;
            int n = System.Math.Min((int)(decaySec * 4f * rate), s.Length - start);
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-t / decaySec * 3f) * Mathf.Min(1f, t / 0.01f);
                float v = Mathf.Sin(t * freq * Mathf.PI * 2f)
                        + 0.50f * Mathf.Sin(t * freq * 2f * Mathf.PI * 2f) * Mathf.Exp(-t * 2f)
                        + 0.25f * Mathf.Sin(t * freq * 3f * Mathf.PI * 2f) * Mathf.Exp(-t * 4f);
                s[start + i] += v * env * amp * 0.5f;
            }
        }

        private static void AddBell(float[] s, int rate, float startSec, float freq, float amp)
        {
            int start = (int)(startSec * rate);
            if (start >= s.Length || start < 0) return;
            int n = System.Math.Min(3 * rate, s.Length - start); // 3 s tail
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-t * 1.8f) * Mathf.Min(1f, t / 0.008f);
                float v = Mathf.Sin(t * freq * Mathf.PI * 2f)
                        + 0.60f * Mathf.Sin(t * freq * 2.76f * Mathf.PI * 2f) * Mathf.Exp(-t * 1.5f)
                        + 0.30f * Mathf.Sin(t * freq * 5.40f * Mathf.PI * 2f) * Mathf.Exp(-t * 3f);
                s[start + i] += v * env * amp * 0.5f;
            }
        }

        private static void AddDrone(float[] s, int rate, float startSec, float durSec, float freq, float amp, float pulseHz)
        {
            int start = (int)(startSec * rate);
            int n = (int)(durSec * rate);
            if (start >= s.Length) return;
            n = System.Math.Min(n, s.Length - start);
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float pulse = 0.72f + 0.28f * Mathf.Sin(t * pulseHz * Mathf.PI * 2f);
                float fade = Mathf.Min(1f, t / 1f) * Mathf.Min(1f, (durSec - t) / 1f);
                float v = Mathf.Sin(t * freq * Mathf.PI * 2f)
                        + 0.40f * Mathf.Sin(t * freq * 2f * Mathf.PI * 2f)
                        + 0.25f * Mathf.Sin(t * freq * 3f * Mathf.PI * 2f)
                        + 0.12f * Mathf.Sin(t * freq * 4f * Mathf.PI * 2f);
                s[start + i] += v * pulse * fade * amp * 0.4f;
            }
        }

        private static void AddDrum(float[] s, int rate, float startSec, float amp)
        {
            int start = (int)(startSec * rate);
            if (start >= s.Length || start < 0) return;
            int n = System.Math.Min((int)(0.35f * rate), s.Length - start);
            if (n <= 0) return;
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float f = Mathf.Lerp(110f, 45f, u);
                phase += f / rate * Mathf.PI * 2f;
                float env = Mathf.Exp(-u * 12f);
                float v = Mathf.Sin(phase) * 0.9f
                        + Mathf.Sin(u * 57f) * 0.20f * Mathf.Exp(-u * 30f);
                s[start + i] += v * env * amp * 0.6f;
            }
        }

        private static void AddCello(float[] s, int rate, float startSec, float durSec, float freq, float amp)
        {
            int start = (int)(startSec * rate);
            int n = (int)(durSec * rate);
            if (start >= s.Length || start < 0) return;
            n = System.Math.Min(n, s.Length - start);
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float envA = Mathf.Min(1f, t / 0.45f);
                float envR = Mathf.Min(1f, (durSec - t) / 0.6f);
                float env = envA * envR;
                float vib = 1f + 0.005f * Mathf.Sin(t * 5f * Mathf.PI * 2f + 1f);
                float v = Mathf.Sin(t * freq * vib * Mathf.PI * 2f)
                        + 0.40f * Mathf.Sin(t * freq * vib * 2f * Mathf.PI * 2f)
                        + 0.20f * Mathf.Sin(t * freq * vib * 3f * Mathf.PI * 2f);
                s[start + i] += v * env * amp * 0.5f;
            }
        }

        // -- helpers ----------------------------------------------------------------------------------------

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

        private static float Envelope(float t, float attack, float hold, float release)
        {
            if (t < attack) return t / attack;
            if (t < attack + hold) return 1f;
            float u = (t - attack - hold) / release;
            if (u >= 1f) return 0f;
            return 1f - u * u;
        }

        private static float Damp(float cur, float target, float k, float dt)
        {
            // Frame-rate-independent exponential smoothing.
            return Mathf.Lerp(cur, target, 1f - Mathf.Exp(-k * dt));
        }

        private static float DampCombat(float cur, float target, float dt)
        {
            // Combat answers danger a touch faster, but releases slowly — no popping either way.
            float k = target > cur ? CombatAttackK : 0.8f;
            return Damp(cur, target, k, dt);
        }
    }
}
