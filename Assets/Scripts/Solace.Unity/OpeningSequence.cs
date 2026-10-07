// Solace.Unity — the first-time opening sequence.
//
// A cinematic director for new lives: golden-hour vista, slow push-in,
// title card, whispered onboarding, and a celebration for the first
// discovery. Like the opening of a nature documentary, not a game loading.
//
// Flow:
//   1. Vista (0-8s):   camera starts wide, pushes in toward the fox.
//                      "SOLACE" fades in, holds, dissolves.
//   2. Whispers (8-20s): camera hands off to ObserverCamera.
//                      three fading hints: fox → autonomy → watch.
//   3. Discovery:      watches for the first POI discovery, then
//                      celebrates it (golden moment, slow push).
//
// This is presentation only: it never touches sim state, only the camera
// and HUD. Destroys itself when done.
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class OpeningSequence : MonoBehaviour
    {
        private GameBootstrap _boot;
        private ObserverCamera _observer;
        private Camera _cam;
        private bool _running;
        private float _t;

        // Cinematic camera path.
        private Vector3 _vistaPos;
        private Vector3 _vistaLook;
        private Vector3 _foxPos;
        private bool _camInit;

        // Whisper sequence.
        private readonly string[] _whispers = {
            "This is your fox.",
            "It lives its own life.",
            "Watch."
        };
        private int _whisperIdx = -1;
        private float _whisperT;

        // First-discovery watch.
        private bool _watchingDiscovery;
        private bool _celebrated;
        private float _celebrateT;
        private string _discoveredName = "";

        // Accessibility: fade overlay for reduced-motion transitions.
        private UnityEngine.UI.Image _fade;
        private float _fadeTarget;
        private float _fadeSpeed = 2.5f;
        private System.Action _fadeDone;

        // Phase timing.
        private const float TitleIn = 1.5f;    // SOLACE starts fading in
        private const float TitleHold = 5.5f;  // SOLACE fully visible
        private const float TitleOut = 8.0f;   // SOLACE gone
        private const float Handoff = 9.0f;    // camera → ObserverCamera
        private const float WhisperStart = 10f;
        private const float WhisperGap = 4.5f;

        public static void PlayForNewLife(GameBootstrap boot)
        {
            var go = new GameObject("OpeningSequence");
            var seq = go.AddComponent<OpeningSequence>();
            seq._boot = boot;
            seq._observer = boot.Camera;
            seq._cam = boot.Camera.GetComponent<Camera>();
        }

        private void Start()
        {
            _running = true;
            _t = 0f;
            BuildFadeOverlay();
            // Take over the camera: disable the follow behaviour during the intro.
            if (_observer != null) _observer.enabled = false;

            if (AccessibilitySettings.ReduceMotion)
            {
                // Reduced motion: skip the sweeping vista. Start at the fox,
                // fading in gently from black — no camera moves, no cuts.
                var sim = _boot.Sim;
                var a = sim.State.Agent;
                var world = sim.State.World;
                Vector3 fox = new Vector3(a.X, world.SampleHeight(a.X, a.Z) + 1.2f, a.Z);
                _cam.transform.position = fox + new Vector3(0f, 4f, -9f);
                _cam.transform.LookAt(fox);
                SetFade(1f); // start black
                FadeTo(0f, 1.2f, null); // gentle fade in
                // Jump straight to the handoff phase (shortened).
                _t = Handoff - 0.1f;
            }
            else
            {
                SetupVista();
            }
            if (_boot != null && _boot.Hud != null)
                _boot.Hud.ShowTitleCard("SOLACE", "a life, unfolding");
        }

        // -- accessibility fade overlay --------------------------------------------

        private void BuildFadeOverlay()
        {
            var canvas = UiKit.CreateCanvas("OpeningFade", 100);
            var rt = UiKit.Rect(canvas.transform, "Fade", 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            _fade = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            _fade.color = new Color(0f, 0f, 0f, 0f);
            _fade.raycastTarget = false;
        }

        private void SetFade(float a)
        {
            if (_fade == null) return;
            var c = _fade.color; c.a = a; _fade.color = c;
            _fadeTarget = a;
            _fadeDone = null;
        }

        private void FadeTo(float target, float speed, System.Action onDone)
        {
            _fadeTarget = target;
            _fadeSpeed = speed;
            _fadeDone = onDone;
        }

        private void UpdateFade()
        {
            if (_fade == null) return;
            var c = _fade.color;
            if (Mathf.Abs(c.a - _fadeTarget) < 0.01f)
            {
                if (c.a != _fadeTarget)
                {
                    c.a = _fadeTarget; _fade.color = c;
                    var done = _fadeDone; _fadeDone = null;
                    done?.Invoke();
                }
                return;
            }
            c.a = Mathf.MoveTowards(c.a, _fadeTarget, _fadeSpeed * Time.deltaTime);
            _fade.color = c;
            if (Mathf.Abs(c.a - _fadeTarget) < 0.01f)
            {
                var done = _fadeDone; _fadeDone = null;
                done?.Invoke();
            }
        }

        private void SetupVista()
        {
            var sim = _boot.Sim;
            var a = sim.State.Agent;
            var world = sim.State.World;
            _foxPos = new Vector3(a.X, world.SampleHeight(a.X, a.Z) + 1.2f, a.Z);

            // Find a high point behind the fox for the vista: sample a ring
            // and pick the highest ground with a clear view toward the fox.
            float bestH = float.MinValue;
            Vector3 best = _foxPos;
            var rng = new System.Random(sim.State.Seed);
            for (int i = 0; i < 24; i++)
            {
                float ang = (float)(i / 24.0 * Mathf.PI * 2.0);
                float d = 55f + (float)rng.NextDouble() * 25f;
                float x = a.X + Mathf.Sin(ang) * d;
                float z = a.Z + Mathf.Cos(ang) * d;
                float half = world.HalfSize - 10f;
                x = Mathf.Clamp(x, -half, half);
                z = Mathf.Clamp(z, -half, half);
                float h = world.SampleHeight(x, z);
                if (h > bestH) { bestH = h; best = new Vector3(x, h + 3f, z); }
            }
            _vistaPos = best;
            _vistaLook = _foxPos;
            _camInit = true;

            // Start the frame wide: camera at vista, looking at the fox.
            _cam.transform.position = _vistaPos;
            _cam.transform.LookAt(_vistaLook);
        }

        private void Update()
        {
            if (!_running) return;
            _t += Time.deltaTime;
            UpdateFade();

            if (_t < Handoff)
                UpdateVista();
            else if (!_watchingDiscovery)
                DoHandoff();

            if (_watchingDiscovery && !_celebrated)
                WatchForDiscovery();

            if (_celebrated)
                UpdateCelebration();

            // Self-destruct when the show is over.
            if (_celebrated && _celebrateT > 6f)
            {
                _running = false;
                Destroy(gameObject);
            }
            // Safety: if no discovery in 10 minutes, just end quietly.
            if (_watchingDiscovery && !_celebrated && _t > 610f)
            {
                _running = false;
                Destroy(gameObject);
            }
        }

        private void UpdateVista()
        {
            // Slow push-in: vista → halfway to the fox over 9 seconds.
            // Ease with smoothstep so it decelerates into the handoff.
            float k = Mathf.Clamp01(_t / Handoff);
            k = k * k * (3f - 2f * k); // smoothstep
            Vector3 targetPos = Vector3.Lerp(_vistaPos, _foxPos + (_vistaPos - _foxPos).normalized * 12f, k * 0.55f);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, targetPos,
                1f - Mathf.Exp(-2f * Time.deltaTime));
            // The look target drifts from the landscape to the fox.
            Vector3 look = Vector3.Lerp(_vistaLook, _foxPos, k);
            Quaternion want = Quaternion.LookRotation((look - _cam.transform.position).normalized, Vector3.up);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want,
                1f - Mathf.Exp(-2f * Time.deltaTime));
        }

        private void DoHandoff()
        {
            _watchingDiscovery = true;
            _whisperT = 0f;
            // Hand the camera back to the documentary follow.
            if (_observer != null)
            {
                if (AccessibilitySettings.ReduceMotion)
                {
                    // Fade through black instead of an instant cut.
                    FadeTo(1f, 3f, () =>
                    {
                        _observer.enabled = true;
                        _observer.SnapToAgent();
                        FadeTo(0f, 1.8f, null);
                    });
                }
                else
                {
                    _observer.enabled = true;
                    _observer.SnapToAgent();
                }
            }
        }

        private void WatchForDiscovery()
        {
            // Whisper sequence runs during the watch.
            _whisperT += Time.deltaTime;
            int want = -1;
            if (_whisperT >= 1f) want = 0;
            if (_whisperT >= 1f + WhisperGap) want = 1;
            if (_whisperT >= 1f + WhisperGap * 2) want = 2;
            if (want != _whisperIdx && want < _whispers.Length && want >= 0)
            {
                _whisperIdx = want;
                if (_boot.Hud != null) _boot.Hud.ShowWhisper(_whispers[want], 3.8f);
            }

            // Check for first discovery (any POI except the den).
            var sim = _boot.Sim;
            if (sim == null) return;
            foreach (var p in sim.State.World.Pois)
            {
                if (p.Discovered && p.Type != PoiType.Den)
                {
                    _celebrated = true;
                    _celebrateT = 0f;
                    _discoveredName = p.DisplayName;
                    CelebrateDiscovery(p);
                    break;
                }
            }
        }

        private void CelebrateDiscovery(PointOfInterest p)
        {
            // Golden moment: brief slow push-in + named toast.
            // Reduced motion: skip the camera move, just celebrate with text.
            if (!AccessibilitySettings.ReduceMotion && _observer != null)
                _observer.enabled = false; // take over briefly
            if (_boot.Hud != null)
            {
                _boot.Hud.ShowWhisper("Discovered — " + _discoveredName, 5f);
                _boot.Hud.ShowToast("The world reveals itself.", 4f);
            }
            Debug.Log("[Solace] First discovery celebrated: " + _discoveredName);
        }

        private void UpdateCelebration()
        {
            _celebrateT += Time.deltaTime;
            if (AccessibilitySettings.ReduceMotion)
            {
                // No camera push — just wait, then release.
                if (_celebrateT >= 4f && _observer != null && !_observer.enabled)
                {
                    _observer.enabled = true;
                    _observer.SnapToAgent();
                }
                return;
            }
            // Gentle 4-second push toward the discovery, then release.
            if (_celebrateT < 4f)
            {
                var sim = _boot.Sim;
                var a = sim.State.Agent;
                var world = sim.State.World;
                Vector3 fox = new Vector3(a.X, world.SampleHeight(a.X, a.Z) + 1.2f, a.Z);
                Vector3 dir = (_cam.transform.position - fox).normalized;
                Vector3 want = fox + dir * 5.5f;
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, want,
                    1f - Mathf.Exp(-1.2f * Time.deltaTime));
                Quaternion q = Quaternion.LookRotation((fox - _cam.transform.position).normalized, Vector3.up);
                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, q,
                    1f - Mathf.Exp(-2f * Time.deltaTime));
            }
            else if (_observer != null && !_observer.enabled)
            {
                _observer.enabled = true;
                _observer.SnapToAgent();
            }
        }
    }
}
