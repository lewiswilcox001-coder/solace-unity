using System.Collections.Generic;
using UnityEngine;

namespace Solace.Unity.Character
{
    /// <summary>
    /// Keyframed animation for the lantern-fox (<see cref="FoxRig"/>).
    ///
    /// Every clip is authored as explicit joint euler-angle keyframes (degrees)
    /// sampled with smoothstep interpolation — no procedural sine-bouncing.
    /// Gait clips are baked from quadruped footfall phasing at build time, then
    /// played back as pure keyframes.
    ///
    /// LOCOMOTION CONTRACT — the glue must move the fox at these speeds while
    /// the matching clip plays, or paws will skate:
    ///   Walk  1.6 m/s × 0.80 s loop = 1.28 m stride
    ///   Trot  3.2 m/s × 0.55 s loop = 1.76 m stride
    ///   Run   6.5 m/s × 0.45 s loop = 2.93 m stride (rotary gallop)
    /// One-shots (Pounce/Hurt/PlayBow) carry a small root offset; either treat
    /// it as root motion or crossfade back to a loop with fadeTime ≥ 0.3 s.
    /// </summary>
    [RequireComponent(typeof(FoxRig))]
    public class FoxAnimator : MonoBehaviour
    {
        public enum Clip { Idle, Walk, Trot, Run, Pounce, Hurt, Sit, Sleep, PlayBow }

        public const float WalkSpeed = 1.6f;
        public const float TrotSpeed = 3.2f;
        public const float GallopSpeed = 6.5f;
        public const float WalkStride = 1.28f;    // 1.6 × 0.80
        public const float TrotStride = 1.76f;    // 3.2 × 0.55
        public const float GallopStride = 2.925f; // 6.5 × 0.45

        public void Play(Clip clip, float fadeTime = 0.25f)
        {
            EnsureBound();
            if (!_bound) return;
            var clips = ClipBank();
            if (clip == _current && _cd != null)
            {
                _time = 0f; _fading = false; // restart same clip
                return;
            }
            // snapshot current pose for the crossfade
            SampleClip(_cd, ClipTime(), _fromA, ref _fromRoot);
            _current = clip;
            _cd = clips[clip];
            _time = 0f;
            _fading = fadeTime > 0.001f;
            _fadeT = 0f;
            _fadeDur = Mathf.Max(fadeTime, 0.001f);
        }

        public Clip Current => _current;

        // ---------------------------------------------------------------- state

        FoxRig _rig;
        readonly Transform[] _jt = new Transform[JCount];
        Vector3 _rootBase;
        bool _bound;

        Clip _current = Clip.Idle;
        ClipData _cd;
        float _time;

        readonly float[] _fromA = new float[JCount * 3];
        Vector3 _fromRoot;
        float _fadeT, _fadeDur;
        bool _fading;

        readonly float[] _sampleA = new float[JCount * 3];
        Vector3 _sampleRoot;
        readonly float[] _outA = new float[JCount * 3];
        Vector3 _outRoot;

        void Awake() { _cd = ClipBank()[Clip.Idle]; }

        void EnsureBound()
        {
            if (_bound) return;
            _rig = GetComponent<FoxRig>();
            if (_rig == null || _rig.Body == null) return;
            _jt[(int)J.Body] = _rig.Body;   _jt[(int)J.Chest] = _rig.Chest;
            _jt[(int)J.Neck] = _rig.Neck;   _jt[(int)J.Head] = _rig.Head;
            _jt[(int)J.EarL] = _rig.EarL;   _jt[(int)J.EarR] = _rig.EarR;
            _jt[(int)J.TailBase] = _rig.TailBase; _jt[(int)J.TailMid] = _rig.TailMid;
            _jt[(int)J.TailTip] = _rig.TailTip;
            _jt[(int)J.FLU] = _rig.LegFL_Upper; _jt[(int)J.FLL] = _rig.LegFL_Lower; _jt[(int)J.FLP] = _rig.LegFL_Paw;
            _jt[(int)J.FRU] = _rig.LegFR_Upper; _jt[(int)J.FRL] = _rig.LegFR_Lower; _jt[(int)J.FRP] = _rig.LegFR_Paw;
            _jt[(int)J.BLU] = _rig.LegBL_Upper; _jt[(int)J.BLL] = _rig.LegBL_Lower; _jt[(int)J.BLP] = _rig.LegBL_Paw;
            _jt[(int)J.BRU] = _rig.LegBR_Upper; _jt[(int)J.BRL] = _rig.LegBR_Lower; _jt[(int)J.BRP] = _rig.LegBR_Paw;
            _rootBase = _rig.Root.localPosition;
            _bound = true;
        }

        void Update()
        {
            EnsureBound();
            if (!_bound || _cd == null) return;
            _time += Time.deltaTime;
            SampleClip(_cd, ClipTime(), _sampleA, ref _sampleRoot);
            if (_fading)
            {
                _fadeT += Time.deltaTime;
                float u = S(Mathf.Clamp01(_fadeT / _fadeDur));
                for (int i = 0; i < _outA.Length; i++)
                    _outA[i] = _fromA[i] + (_sampleA[i] - _fromA[i]) * u;
                _outRoot = Vector3.Lerp(_fromRoot, _sampleRoot, u);
                if (_fadeT >= _fadeDur) _fading = false;
            }
            else
            {
                _sampleA.CopyTo(_outA, 0);
                _outRoot = _sampleRoot;
            }
            for (int j = 0; j < JCount; j++)
                _jt[j].localEulerAngles = new Vector3(_outA[j * 3], _outA[j * 3 + 1], _outA[j * 3 + 2]);
            _rig.Root.localPosition = _rootBase + _outRoot;
        }

        float ClipTime() => _cd.loop ? _time % _cd.dur : Mathf.Min(_time, _cd.dur);

        // ------------------------------------------------------- keyframe core

        enum J
        {
            Body, Chest, Neck, Head, EarL, EarR,
            TailBase, TailMid, TailTip,
            FLU, FLL, FLP, FRU, FRL, FRP, BLU, BLL, BLP, BRU, BRL, BRP,
        }
        const int JCount = 21;

        sealed class Key
        {
            public readonly float t;
            public readonly float[] a; // JCount*3 euler degrees
            public readonly Vector3 root;
            public Key(float t, float[] a, Vector3 root) { this.t = t; this.a = a; this.root = root; }
        }

        sealed class ClipData
        {
            public float dur;
            public bool loop;
            public Key[] keys;
        }

        /// <summary>Rest pose every keyframe builds on (tail curve, rear-leg digitigrade set).</summary>
        static float[] _rest;
        static float[] Rest()
        {
            if (_rest != null) return _rest;
            var r = new float[JCount * 3];
            Set(r, J.TailBase, 12, 0, 0); Set(r, J.TailMid, 10, 0, 0); Set(r, J.TailTip, 14, 0, 0);
            Set(r, J.BLU, 7, 0, 0); Set(r, J.BLL, -9, 0, 0); Set(r, J.BLP, 2, 0, 0);
            Set(r, J.BRU, 7, 0, 0); Set(r, J.BRL, -9, 0, 0); Set(r, J.BRP, 2, 0, 0);
            return _rest = r;
        }
        static void Set(float[] a, J j, float x, float y, float z)
        { int o = (int)j * 3; a[o] = x; a[o + 1] = y; a[o + 2] = z; }

        /// <summary>Pose builder: starts from rest, absolute joint sets per key.</summary>
        sealed class PB
        {
            public readonly float[] a = (float[])Rest().Clone();
            public Vector3 root;
            public PB Jx(J j, float x, float y, float z) { Set(a, j, x, y, z); return this; }
            public PB R(float x, float y, float z) { root = new Vector3(x, y, z); return this; }
            public Key K(float t) => new Key(t, a, root);
        }

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static float L(float a, float b, float t) => a + (b - a) * t;

        static void SampleClip(ClipData cd, float t, float[] a, ref Vector3 root)
        {
            Key[] k = cd.keys;
            int n = k.Length;
            if (!cd.loop && t >= k[n - 1].t) // one-shot: hold final pose
            {
                k[n - 1].a.CopyTo(a, 0);
                root = k[n - 1].root;
                return;
            }
            int i = n - 1;
            for (int j = 0; j < n; j++)
                if (k[j].t > t) { i = j - 1; break; }
            if (i < 0) i = 0;
            Key k0 = k[i];
            Key k1 = (i == n - 1) ? k[0] : k[i + 1];
            float t1 = (i == n - 1) ? cd.dur : k1.t;
            float u = S((t - k0.t) / Mathf.Max(1e-5f, t1 - k0.t));
            for (int j = 0; j < a.Length; j++)
                a[j] = k0.a[j] + (k1.a[j] - k0.a[j]) * u;
            root = Vector3.Lerp(k0.root, k1.root, u);
        }

        // ------------------------------------------------------- gait synthesis
        // Baked at build time into keyframes: phase-driven leg angles with a
        // stance (paw planted, sweeping back) and swing (fold, reach forward).

        static void LegAngles(float phase, float duty,
                              float swingF, float swingB, float fold, bool rear,
                              out float u, out float l, out float p)
        {
            phase -= Mathf.Floor(phase);
            if (phase < duty) // stance
            {
                float s = S(phase / duty);
                u = L(-swingF, swingB, s);
                float give = Mathf.Sin(Mathf.PI * phase / duty);
                l = (rear ? -1f : 1f) * 3.5f * give; // slight mid-stance give
                p = -u * 0.35f;                      // paw stays flat-ish
            }
            else // swing
            {
                float q = (phase - duty) / (1f - duty);
                u = L(swingB, -swingF, S(q));
                float f = Mathf.Sin(Mathf.PI * q);
                l = (rear ? -1f : 1f) * fold * f;    // elbow/hock fold
                p = (rear ? -1f : 1f) * 10f * f - u * 0.25f;
            }
        }

        static Key[] WalkKeys()
        {
            const float dur = 0.8f, duty = 0.62f;
            var keys = new Key[8];
            for (int i = 0; i < 8; i++)
            {
                float p = i / 8f, w = p * Mathf.PI * 2f;
                var b = new PB();
                float u, l, pp;
                // lateral-sequence walk: RH → RF → LH → LF
                LegAngles(p - 0.00f, duty, 28, 32, 38, true, out u, out l, out pp);
                b.Jx(J.BRU, u + 7, l - 9, pp + 2);
                LegAngles(p - 0.25f, duty, 30, 30, 42, false, out u, out l, out pp);
                b.Jx(J.FRU, u, l, pp);
                LegAngles(p - 0.50f, duty, 28, 32, 38, true, out u, out l, out pp);
                b.Jx(J.BLU, u + 7, l - 9, pp + 2);
                LegAngles(p - 0.75f, duty, 30, 30, 42, false, out u, out l, out pp);
                b.Jx(J.FLU, u, l, pp);
                // body: gentle sway; head counter-stabilises (gaze stays forward)
                float roll = 3f * Mathf.Sin(w), yaw = 2.5f * Mathf.Sin(w + 0.6f);
                float pitch = 2f * Mathf.Sin(2 * w + 1f);
                b.Jx(J.Body, pitch, yaw, roll)
                 .Jx(J.Chest, 1.2f * Mathf.Sin(2 * w + 1.4f), 0, 1.5f * Mathf.Sin(w + 0.3f))
                 .Jx(J.Neck, -pitch * 0.5f, -yaw * 0.5f, 0)
                 .Jx(J.Head, -pitch * 0.8f + 1f * Mathf.Sin(w + 2.2f), -yaw * 0.8f, -roll * 0.6f)
                 .Jx(J.EarL, 0, 4 * Mathf.Sin(w * 0.5f + 1f), 0)
                 .Jx(J.EarR, 0, 4 * Mathf.Sin(w * 0.5f + 2.6f), 0)
                 .Jx(J.TailBase, 12 + 2 * Mathf.Sin(2 * w), -4 * Mathf.Sin(w - 0.5f), 0)
                 .Jx(J.TailMid, 10 + 1.5f * Mathf.Sin(2 * w - 0.4f), -5 * Mathf.Sin(w - 0.8f), 0)
                 .Jx(J.TailTip, 14 + 2 * Mathf.Sin(2 * w - 0.8f), -7 * Mathf.Sin(w - 1.1f), 0)
                 .R(0.012f * Mathf.Sin(w), 0.010f * Mathf.Sin(2 * w + 1.9f), 0);
                keys[i] = b.K(p * dur);
            }
            return keys;
        }

        static Key[] TrotKeys()
        {
            const float dur = 0.55f, duty = 0.45f;
            var keys = new Key[8];
            for (int i = 0; i < 8; i++)
            {
                float p = i / 8f, w = p * Mathf.PI * 2f;
                var b = new PB();
                float u, l, pp;
                // diagonal pairs: (LF + BR) then (RF + BL)
                LegAngles(p - 0.00f, duty, 32, 36, 48, true, out u, out l, out pp);
                b.Jx(J.BRU, u + 7, l - 9, pp + 2);
                LegAngles(p - 0.00f, duty, 34, 34, 50, false, out u, out l, out pp);
                b.Jx(J.FLU, u, l, pp);
                LegAngles(p - 0.50f, duty, 32, 36, 48, true, out u, out l, out pp);
                b.Jx(J.BLU, u + 7, l - 9, pp + 2);
                LegAngles(p - 0.50f, duty, 34, 34, 50, false, out u, out l, out pp);
                b.Jx(J.FRU, u, l, pp);
                // light two-beat bounce; head bob kept minimal
                float pitch = 3.5f * Mathf.Sin(2 * w + 0.6f);
                b.Jx(J.Body, pitch, 0, 1.5f * Mathf.Sin(w))
                 .Jx(J.Chest, 2f * Mathf.Sin(2 * w + 0.9f), 0, 0)
                 .Jx(J.Neck, -pitch * 0.4f, 0, 0)
                 .Jx(J.Head, -pitch * 0.7f + 1.5f * Mathf.Sin(2 * w + 2.4f), 0, 0)
                 .Jx(J.EarL, -4, 0, 0).Jx(J.EarR, -4, 0, 0)
                 .Jx(J.TailBase, 10 + 3 * Mathf.Sin(2 * w - 0.6f), -2 * Mathf.Sin(w), 0)
                 .Jx(J.TailMid, 8 + 2 * Mathf.Sin(2 * w - 1f), -3 * Mathf.Sin(w - 0.4f), 0)
                 .Jx(J.TailTip, 12 + 2.5f * Mathf.Sin(2 * w - 1.4f), -4 * Mathf.Sin(w - 0.8f), 0)
                 .R(0, 0.020f * Mathf.Sin(2 * w + 0.8f) - 0.004f, 0);
                keys[i] = b.K(p * dur);
            }
            return keys;
        }

        static Key[] RunKeys()
        {
            const float dur = 0.45f, duty = 0.34f;
            var keys = new Key[8];
            for (int i = 0; i < 8; i++)
            {
                float p = i / 8f, w = p * Mathf.PI * 2f;
                var b = new PB();
                float u, l, pp;
                // rotary gallop footfalls: RH → LH → RF → LF, gathered suspension
                LegAngles(p - 0.00f, duty, 40, 34, 55, true, out u, out l, out pp);
                b.Jx(J.BRU, u + 7, l - 9, pp + 2);
                LegAngles(p - 0.12f, duty, 40, 34, 55, true, out u, out l, out pp);
                b.Jx(J.BLU, u + 7, l - 9, pp + 2);
                LegAngles(p - 0.50f, duty, 32, 28, 60, false, out u, out l, out pp);
                b.Jx(J.FRU, u, l, pp);
                LegAngles(p - 0.62f, duty, 32, 28, 60, false, out u, out l, out pp);
                b.Jx(J.FLU, u, l, pp);
                // spine gathers and extends; head holds the horizon
                float gather = Mathf.Sin(w + 0.9f); // +1 gathered, -1 extended
                float bx = 1f + 6f * gather, cx = 0.5f + 5f * Mathf.Sin(w + 1.1f);
                b.Jx(J.Body, bx, 0, 1.5f * Mathf.Sin(w))
                 .Jx(J.Chest, cx, 0, 0)
                 .Jx(J.Neck, -3f - 2f * gather, 0, 0)
                 .Jx(J.Head, -(bx + cx) * 0.55f - 6f, 0, 0)
                 .Jx(J.EarL, -22, 0, 0).Jx(J.EarR, -22, 0, 0) // ears folded back
                 .Jx(J.TailBase, -2 + 2 * Mathf.Sin(w + 2f), 0, 0)  // tail streams behind
                 .Jx(J.TailMid, 2 + 1.5f * Mathf.Sin(w + 1.6f), 0, 0)
                 .Jx(J.TailTip, 8 + 2 * Mathf.Sin(w + 1.2f), 0, 0)
                 .R(0, 0.055f * Mathf.Sin(w - 0.5f), 0);
                keys[i] = b.K(p * dur);
            }
            return keys;
        }

        // ------------------------------------------------------- authored clips

        static float Br(float t, float period, float amp) => amp * Mathf.Sin(t / period * Mathf.PI * 2f);

        static Key[] IdleKeys()
        {
            var k = new List<Key>();
            // 5 s loop: breathing, two slow look-arounds, one weight shift, tail adjustment.
            k.Add(new PB().Jx(J.Chest, Br(0f, 2.5f, 1.6f), 0, 0).Jx(J.EarL, 0, 6, 0).K(0f));
            k.Add(new PB().Jx(J.Chest, Br(0.5f, 2.5f, 1.6f), 0, 0).Jx(J.EarL, 0, 8, 0).K(0.5f));
            k.Add(new PB().Jx(J.Chest, Br(1f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, 0, 14, 0).Jx(J.Neck, 0, 6, 0).Jx(J.EarR, 0, -6, 0).K(1f));
            k.Add(new PB().Jx(J.Chest, Br(1.5f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, -2, 28, 0).Jx(J.Neck, 0, 12, 0).Jx(J.EarL, 0, 10, 0).K(1.5f));
            k.Add(new PB().Jx(J.Chest, Br(2f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, 0, 10, 0).Jx(J.Neck, 0, 4, 0).K(2f));
            k.Add(new PB().Jx(J.Chest, Br(2.5f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, 0, -6, 0).Jx(J.Body, 0, 0, 3.5f).R(0.035f, 0, 0).K(2.5f)); // weight shift
            k.Add(new PB().Jx(J.Chest, Br(3f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, 0, -14, 0).Jx(J.Neck, 0, -6, 0)
                .Jx(J.Body, 0, 0, 3.5f).R(0.035f, 0, 0).K(3f));
            k.Add(new PB().Jx(J.Chest, Br(3.5f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, 2, -24, 0).Jx(J.Neck, 0, -10, 0).Jx(J.EarL, 0, -6, 0).K(3.5f));
            k.Add(new PB().Jx(J.Chest, Br(4f, 2.5f, 1.6f), 0, 0)
                .Jx(J.Head, 0, -8, 0).Jx(J.Body, 0, 0, 1f).Jx(J.TailTip, 14, 16, 0).R(0.01f, 0, 0).K(4f));
            k.Add(new PB().Jx(J.Chest, Br(4.5f, 2.5f, 1.6f), 0, 0).Jx(J.TailTip, 14, 8, 0).K(4.5f));
            return k.ToArray();
        }

        static PB SitBase(float t)
        {
            float b = Br(t, 3f, 1.4f);
            return new PB().R(0, -0.30f, 0)
                .Jx(J.Body, -24, 0, 0).Jx(J.Chest, -12 + b, 0, 0)
                .Jx(J.Neck, 18, 0, 0).Jx(J.Head, 8, 0, 0)
                .Jx(J.FLU, -6, 0, 0).Jx(J.FLL, 4, 0, 0).Jx(J.FLP, 2, 0, 0)
                .Jx(J.FRU, -6, 0, 0).Jx(J.FRL, 4, 0, 0).Jx(J.FRP, 2, 0, 0)
                .Jx(J.BLU, -45, 0, 0).Jx(J.BLL, 55, 0, 0).Jx(J.BLP, -12, 0, 0)
                .Jx(J.BRU, -45, 0, 0).Jx(J.BRL, 55, 0, 0).Jx(J.BRP, -12, 0, 0)
                .Jx(J.TailBase, 6, 40, 0).Jx(J.TailMid, 4, 32, 0).Jx(J.TailTip, 6, 30, 0);
        }

        static Key[] SitKeys()
        {
            var k = new List<Key>();
            k.Add(SitBase(0f).K(0f));
            k.Add(SitBase(0.5f).Jx(J.Head, 8, 4, 0).K(0.5f));
            k.Add(SitBase(1f).Jx(J.Head, 8, 6, 0).K(1f));
            k.Add(SitBase(1.5f).Jx(J.Head, 8, 6, 0).Jx(J.EarR, -14, 0, 0).K(1.5f)); // ear flick
            k.Add(SitBase(2f).Jx(J.TailTip, 6, 36, 0).K(2f));
            k.Add(SitBase(2.5f).Jx(J.Head, 8, -4, 0).Jx(J.TailTip, 6, 30, 0).K(2.5f));
            return k.ToArray();
        }

        static PB SleepBase(float t)
        {
            float b = Br(t, 3f, 2.6f);                       // slow, deep breaths
            float dy = 0.010f * Mathf.Sin(t / 3f * Mathf.PI * 2f);
            return new PB().R(0, -0.33f + dy, 0)
                .Jx(J.Body, -8, 52, 0).Jx(J.Chest, -6 + b, 18, 0)
                .Jx(J.Neck, 10, 22, 0).Jx(J.Head, 18, 50, 0)  // nose tucked toward tail
                .Jx(J.FLU, -48, 0, 0).Jx(J.FLL, 75, 0, 0).Jx(J.FLP, -20, 0, 0)
                .Jx(J.FRU, -44, 0, 0).Jx(J.FRL, 72, 0, 0).Jx(J.FRP, -22, 0, 0)
                .Jx(J.BLU, -45, 0, 0).Jx(J.BLL, -50, 0, 0).Jx(J.BLP, 40, 0, 0)
                .Jx(J.BRU, -42, 0, 0).Jx(J.BRL, -48, 0, 0).Jx(J.BRP, 38, 0, 0)
                .Jx(J.TailBase, 8, -55, 0).Jx(J.TailMid, 4, -45, 0).Jx(J.TailTip, 6, -40, 0)
                .Jx(J.EarL, -32, 0, 0).Jx(J.EarR, -32, 0, 0); // ears laid back
        }

        static Key[] SleepKeys()
        {
            var k = new List<Key>();
            k.Add(SleepBase(0f).K(0f));
            k.Add(SleepBase(1f).K(1f));
            k.Add(SleepBase(2f).K(2f));
            k.Add(SleepBase(3f).Jx(J.TailTip, 10, -40, 0).K(3f));
            k.Add(SleepBase(4f).Jx(J.EarL, -18, 0, 0).K(4f)); // ear twitch
            k.Add(SleepBase(5f).Jx(J.Head, 18, 54, 0).K(5f));
            return k.ToArray();
        }

        static Key[] PounceKeys()
        {
            var k = new List<Key>();
            k.Add(new PB().K(0f)); // neutral
            // crouch-coil: tail lashes, ears back
            k.Add(new PB().R(0, -0.16f, 0.05f)
                .Jx(J.Body, 10, 0, 0).Jx(J.Chest, 14, 0, 0).Jx(J.Neck, -10, 0, 0).Jx(J.Head, -12, 0, 0)
                .Jx(J.FLU, 25, 0, 0).Jx(J.FLL, 35, 0, 0).Jx(J.FLP, -15, 0, 0)
                .Jx(J.FRU, 25, 0, 0).Jx(J.FRL, 35, 0, 0).Jx(J.FRP, -15, 0, 0)
                .Jx(J.BLU, 38, 0, 0).Jx(J.BLL, -35, 0, 0).Jx(J.BLP, 10, 0, 0)
                .Jx(J.BRU, 38, 0, 0).Jx(J.BRL, -35, 0, 0).Jx(J.BRP, 10, 0, 0)
                .Jx(J.TailBase, 12, 18, 0).Jx(J.TailMid, 10, -14, 0).Jx(J.TailTip, 14, 16, 0)
                .Jx(J.EarL, -18, 0, 0).Jx(J.EarR, -18, 0, 0).K(0.30f));
            // launch: body extended, forepaws strike down
            k.Add(new PB().R(0, 0.10f, 0.30f)
                .Jx(J.Body, -8, 0, 0).Jx(J.Chest, -6, 0, 0).Jx(J.Neck, 8, 0, 0).Jx(J.Head, 6, 0, 0)
                .Jx(J.FLU, -42, 0, 0).Jx(J.FLL, 10, 0, 0).Jx(J.FLP, 20, 0, 0)
                .Jx(J.FRU, -42, 0, 0).Jx(J.FRL, 10, 0, 0).Jx(J.FRP, 20, 0, 0)
                .Jx(J.BLU, 38, 0, 0).Jx(J.BLL, -8, 0, 0).Jx(J.BLP, 5, 0, 0)
                .Jx(J.BRU, 38, 0, 0).Jx(J.BRL, -8, 0, 0).Jx(J.BRP, 5, 0, 0)
                .Jx(J.TailBase, -2, 0, 0).Jx(J.TailMid, 2, 0, 0).Jx(J.TailTip, 8, 0, 0)
                .Jx(J.EarL, -22, 0, 0).Jx(J.EarR, -22, 0, 0).K(0.42f));
            // strike / land
            k.Add(new PB().R(0, -0.06f, 0.42f)
                .Jx(J.Body, 6, 0, 0).Jx(J.Chest, 8, 0, 0).Jx(J.Neck, -6, 0, 0).Jx(J.Head, -8, 0, 0)
                .Jx(J.FLU, 18, 0, 0).Jx(J.FLL, 30, 0, 0).Jx(J.FLP, -10, 0, 0)
                .Jx(J.FRU, 18, 0, 0).Jx(J.FRL, 30, 0, 0).Jx(J.FRP, -10, 0, 0)
                .Jx(J.BLU, 20, 0, 0).Jx(J.BLL, -15, 0, 0).Jx(J.BLP, 5, 0, 0)
                .Jx(J.BRU, 20, 0, 0).Jx(J.BRL, -15, 0, 0).Jx(J.BRP, 5, 0, 0)
                .Jx(J.TailBase, 10, 10, 0).Jx(J.TailMid, 8, -8, 0).Jx(J.TailTip, 12, 8, 0).K(0.55f));
            // settle
            k.Add(new PB().R(0, -0.02f, 0.45f)
                .Jx(J.Body, 3, 0, 0).Jx(J.Chest, 4, 0, 0)
                .Jx(J.FLU, 6, 0, 0).Jx(J.FLL, 10, 0, 0)
                .Jx(J.FRU, 6, 0, 0).Jx(J.FRL, 10, 0, 0)
                .Jx(J.BLU, 12, 0, 0).Jx(J.BLL, -8, 0, 0)
                .Jx(J.BRU, 12, 0, 0).Jx(J.BRL, -8, 0, 0).K(0.68f));
            k.Add(new PB().R(0, 0, 0.45f).K(0.80f));
            return k.ToArray();
        }

        static Key[] HurtKeys()
        {
            var k = new List<Key>();
            k.Add(new PB().K(0f));
            // flinch peak: head snaps away, body recoils
            k.Add(new PB().R(0, 0, -0.10f)
                .Jx(J.Body, 8, 0, 0).Jx(J.Chest, 10, 6, 0).Jx(J.Neck, 0, 18, 0).Jx(J.Head, -14, 38, 0)
                .Jx(J.FLU, 20, 0, 0).Jx(J.FLL, 25, 0, 0)
                .Jx(J.FRU, 20, 0, 0).Jx(J.FRL, 25, 0, 0)
                .Jx(J.EarL, -25, 0, 0).Jx(J.EarR, -25, 10, 0)
                .Jx(J.TailBase, 20, 0, 0).K(0.08f));
            // stumbling step back
            k.Add(new PB().R(0, -0.04f, -0.25f)
                .Jx(J.Body, 5, 0, 0).Jx(J.Chest, 6, 0, 0).Jx(J.Neck, 0, 10, 0).Jx(J.Head, -8, 20, 0)
                .Jx(J.FLU, 8, 0, 0).Jx(J.FLL, 10, 0, 0)
                .Jx(J.BLU, 15, 0, 0).Jx(J.BLL, -12, 0, 0)
                .Jx(J.BRU, 15, 0, 0).Jx(J.BRL, -12, 0, 0)
                .Jx(J.EarL, -15, 0, 0).Jx(J.EarR, -15, 0, 0).K(0.20f));
            k.Add(new PB().R(0, -0.02f, -0.12f)
                .Jx(J.Body, 2, 0, 0).Jx(J.Head, -4, 8, 0)
                .Jx(J.EarL, -6, 0, 0).Jx(J.EarR, -6, 0, 0).K(0.35f));
            k.Add(new PB().K(0.50f));
            return k.ToArray();
        }

        static PB BowBase()
        {
            return new PB().R(0, -0.10f, 0)
                .Jx(J.Body, 20, 0, 0).Jx(J.Chest, 15, 0, 0)
                .Jx(J.Neck, -25, 0, 0).Jx(J.Head, -10, 0, 0)
                .Jx(J.FLU, 45, 0, 0).Jx(J.FLL, 50, 0, 0).Jx(J.FLP, -25, 0, 0)
                .Jx(J.FRU, 45, 0, 0).Jx(J.FRL, 50, 0, 0).Jx(J.FRP, -25, 0, 0)
                .Jx(J.BLU, -12, 0, 0).Jx(J.BLL, -5, 0, 0).Jx(J.BLP, 8, 0, 0)
                .Jx(J.BRU, -12, 0, 0).Jx(J.BRL, -5, 0, 0).Jx(J.BRP, 8, 0, 0)
                .Jx(J.TailBase, 25, 0, 0)
                .Jx(J.EarL, -6, 0, 0).Jx(J.EarR, -6, 0, 0);
        }

        static Key[] PlayBowKeys()
        {
            var k = new List<Key>();
            k.Add(new PB().K(0f));
            k.Add(BowBase().Jx(J.TailMid, 10, 10, 0).Jx(J.TailTip, 15, 20, 0).K(0.30f));
            k.Add(BowBase().Jx(J.TailMid, 10, -10, 0).Jx(J.TailTip, 15, -24, 0).K(0.42f));
            k.Add(BowBase().Jx(J.TailMid, 10, 10, 0).Jx(J.TailTip, 15, 24, 0).K(0.54f));
            k.Add(BowBase().Jx(J.TailMid, 10, -10, 0).Jx(J.TailTip, 15, -24, 0).K(0.66f));
            k.Add(BowBase().Jx(J.TailMid, 10, 10, 0).Jx(J.TailTip, 15, 24, 0).K(0.78f));
            k.Add(BowBase().R(0, -0.04f, 0).Jx(J.Body, 8, 0, 0).Jx(J.TailTip, 12, 0, 0).K(0.90f));
            // spring back up
            k.Add(new PB().R(0, 0.06f, 0)
                .Jx(J.Body, -8, 0, 0).Jx(J.Chest, -6, 0, 0)
                .Jx(J.FLU, -20, 0, 0).Jx(J.FLL, 8, 0, 0)
                .Jx(J.FRU, -20, 0, 0).Jx(J.FRL, 8, 0, 0).K(1.05f));
            k.Add(new PB().K(1.20f));
            return k.ToArray();
        }

        // ------------------------------------------------------- clip bank

        static Dictionary<Clip, ClipData> _clips;

        static Dictionary<Clip, ClipData> ClipBank()
        {
            if (_clips != null) return _clips;
            _clips = new Dictionary<Clip, ClipData>
            {
                { Clip.Idle,    new ClipData { dur = 5.0f,  loop = true,  keys = IdleKeys() } },
                { Clip.Walk,    new ClipData { dur = 0.8f,  loop = true,  keys = WalkKeys() } },
                { Clip.Trot,    new ClipData { dur = 0.55f, loop = true,  keys = TrotKeys() } },
                { Clip.Run,     new ClipData { dur = 0.45f, loop = true,  keys = RunKeys() } },
                { Clip.Pounce,  new ClipData { dur = 0.8f,  loop = false, keys = PounceKeys() } },
                { Clip.Hurt,    new ClipData { dur = 0.5f,  loop = false, keys = HurtKeys() } },
                { Clip.Sit,     new ClipData { dur = 3.0f,  loop = true,  keys = SitKeys() } },
                { Clip.Sleep,   new ClipData { dur = 6.0f,  loop = true,  keys = SleepKeys() } },
                { Clip.PlayBow, new ClipData { dur = 1.2f,  loop = false, keys = PlayBowKeys() } },
            };
            return _clips;
        }
    }
}
