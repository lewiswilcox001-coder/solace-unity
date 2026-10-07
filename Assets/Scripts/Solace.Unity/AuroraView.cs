// Solace.Unity — aurora borealis on clear nights.
//
// Three soft curtain ribbons arc across the night sky, breathing slowly in
// greens, teals and violets. Purely visual: it never touches gameplay.
//
// Rendering: a single merged mesh (~2.2k verts) with per-vertex colors, drawn
// in ONE draw call through the tiny Solace/AuroraUnlit shader (additive
// Blend One One — no alpha, no textures, no keywords, no fog code, so zero
// URP variant risk). Vertices and colors are CPU-animated each frame:
// layered sine sways shape the curtains, and the color field mixes a bright
// lower border (the classic auroral band) fading to black at the top.
//
// Appears only on clear nights, fading in/out over ~4 s. A ~40 s breathing
// cycle lets the lights wax and wane through the night — calm, never neon.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class AuroraView : MonoBehaviour, ISimView
    {
        private const float Radius = 1050f; // inside the star field (1080/1100), outside everything else
        private const int Curtains = 3;
        private const int Segs = 72;        // columns per curtain
        private const int Rows = 9;         // vertical rows per curtain
        private const float FadeRate = 0.25f; // 0..1 over ~4 s

        // Soft dreamy palette (kept dim on purpose — never neon).
        private static readonly Color Green = new Color(0.30f, 0.95f, 0.55f);
        private static readonly Color Teal = new Color(0.25f, 0.75f, 0.80f);
        private static readonly Color Violet = new Color(0.58f, 0.42f, 0.92f);
        private const float MaxGlow = 0.55f; // global intensity cap: gentle

        private GameObject _root;
        private Mesh _mesh;
        private Camera _cam;
        private float _fade; // 0..1 eased visibility

        // Per-vertex animation state (root-local space).
        private Vector3[] _basePos;
        private Vector3[] _tangent;   // horizontal sway direction per vertex
        private float[] _u;           // 0..1 along each curtain
        private float[] _v;           // 0..1 bottom..top
        private int[] _curtain;       // which curtain (for palette/phase)
        private float[] _phaseA, _phaseB, _phaseC; // seeded wave phases per column
        private Color[] _hueA, _hueB; // palette pair per curtain
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private float _slowPhase;

        public void Build(WorldData world)
        {
            GameBootstrap.Instance.RegisterView(this);
            _cam = GameBootstrap.Instance.Camera.GetComponent<Camera>();

            var shader = Shader.Find("Solace/AuroraUnlit");
            if (shader == null)
            {
                Debug.LogWarning("[Solace] Solace/AuroraUnlit shader not found — aurora disabled. " +
                                 "Ensure Assets/Shaders/ is in the project.");
                enabled = false;
                return;
            }

            _root = new GameObject("Aurora");
            _root.transform.SetParent(transform, false);

            var rng = SeededRandom.Derive(world.Seed, "unity-aurora");
            _slowPhase = rng.NextFloat(0f, Mathf.PI * 2f);

            int cols = Segs + 1, rows = Rows + 1;
            int vertsPer = cols * rows;
            _basePos = new Vector3[Curtains * vertsPer];
            _tangent = new Vector3[Curtains * vertsPer];
            _u = new float[Curtains * vertsPer];
            _v = new float[Curtains * vertsPer];
            _curtain = new int[Curtains * vertsPer];
            _phaseA = new float[Curtains * cols];
            _phaseB = new float[Curtains * cols];
            _phaseC = new float[Curtains * cols];
            _hueA = new Color[Curtains];
            _hueB = new Color[Curtains];

            var positions = new List<Vector3>(Curtains * vertsPer);
            var colors = new List<Color>(Curtains * vertsPer);
            var tris = new List<int>(Curtains * Segs * Rows * 6);

            Color[] palette = { Green, Teal, Violet };
            for (int c = 0; c < Curtains; c++)
            {
                // Spread the curtains around the sky; arcs never overlap much.
                float centerAz = rng.NextFloat(0f, Mathf.PI * 2f);
                float arcWidth = rng.NextFloat(1.1f, 1.7f);   // ~63..97 degrees
                float elBottom = rng.NextFloat(0.20f, 0.32f); // radians above horizon
                float elTop = elBottom + rng.NextFloat(0.55f, 0.85f);

                _hueA[c] = palette[c % palette.Length];
                _hueB[c] = palette[(c + 1) % palette.Length];

                for (int i = 0; i < cols; i++)
                {
                    int cp = c * cols + i;
                    _phaseA[cp] = rng.NextFloat(0f, Mathf.PI * 2f);
                    _phaseB[cp] = rng.NextFloat(0f, Mathf.PI * 2f);
                    _phaseC[cp] = rng.NextFloat(0f, Mathf.PI * 2f);

                    float u = i / (float)Segs;
                    float az = centerAz + (u - 0.5f) * arcWidth;
                    float cosAz = Mathf.Cos(az), sinAz = Mathf.Sin(az);
                    for (int r = 0; r < rows; r++)
                    {
                        float v = r / (float)Rows;
                        float el = elBottom + v * (elTop - elBottom);
                        float cosEl = Mathf.Cos(el), sinEl = Mathf.Sin(el);
                        int vi = c * vertsPer + i * rows + r;
                        _basePos[vi] = new Vector3(cosAz * cosEl, sinEl, sinAz * cosEl) * Radius;
                        _tangent[vi] = new Vector3(-sinAz, 0f, cosAz); // horizontal, unit length
                        _u[vi] = u;
                        _v[vi] = v;
                        _curtain[vi] = c;
                        positions.Add(_basePos[vi]);
                        colors.Add(Color.black);
                    }
                }

                int base_ = c * vertsPer;
                for (int i = 0; i < Segs; i++)
                    for (int r = 0; r < Rows; r++)
                    {
                        int a = base_ + i * rows + r;
                        int b = base_ + (i + 1) * rows + r;
                        // Cull is off; winding only needs consistency.
                        tris.Add(a); tris.Add(b); tris.Add(b + 1);
                        tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
                    }
            }

            _mesh = new Mesh();
            _mesh.SetVertices(positions);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(tris, 0);
            // Static generous bounds — vertices sway, never recompute per frame.
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (Radius * 2.6f));

            var mf = _root.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var mr = _root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(shader);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _root.SetActive(false);
        }

        public void SyncFromState(GameState state)
        {
            if (_mesh == null || _root == null) return;

            bool night = state.IsNight;
            bool clear = state.Weather == Weather.Clear;
            float target = (night && clear) ? 1f : 0f;
            float dt = Time.deltaTime;
            _fade = Mathf.MoveTowards(_fade, target, dt * FadeRate);

            if (_fade <= 0.001f)
            {
                if (_root.activeSelf) _root.SetActive(false);
                return;
            }
            if (!_root.activeSelf) _root.SetActive(true);

            // The veils are celestial: glued to the camera, fixed orientation.
            if (_cam != null) _root.transform.position = _cam.transform.position;

            float t = Time.time;
            // ~40 s breathing cycle: the lights wax and wane through the night.
            float breathe = 0.55f + 0.45f * Mathf.Sin(t * 0.157f + _slowPhase);
            float glow = _fade * breathe * MaxGlow;

            _verts.Clear();
            _colors.Clear();
            int cols = Segs + 1, rows = Rows + 1;
            int vertsPer = cols * rows;

            for (int vi = 0; vi < _basePos.Length; vi++)
            {
                int c = _curtain[vi];
                int col = (vi % vertsPer) / rows;
                int cp = c * cols + col;
                float u = _u[vi], v = _v[vi];

                // Slow layered sway: broad folds + finer ripple. Gentle.
                float sway = Mathf.Sin(u * 9.4f + t * 0.12f + _phaseA[cp]) * 16f
                           + Mathf.Sin(u * 23.0f + t * 0.07f + _phaseB[cp]) * 8f;
                float lift = Mathf.Sin(u * 7.0f + t * 0.10f + _phaseC[cp]) * 6f * v;
                _verts.Add(_basePos[vi] + _tangent[vi] * sway + new Vector3(0f, lift, 0f));

                // Curtain brightness: ray streaks drifting slowly sideways.
                float rays = 0.60f + 0.40f * Mathf.Sin(u * 40.0f + t * 0.18f + _phaseC[cp] * 2f);
                float folds = 0.55f + 0.45f * Mathf.Sin(u * 9.4f + t * 0.12f + _phaseA[cp] + 1.3f);
                float shape = rays * folds;

                // Vertical profile: bright lower border, soft fade to the top.
                float border = Mathf.Exp(-Mathf.Pow((v - 0.30f) * 3.0f, 2f));
                float topFade = Mathf.Pow(1f - v, 1.2f);
                float bright = border * topFade * shape;

                // Slow hue drift between the curtain's palette pair.
                float mix = 0.5f + 0.5f * Mathf.Sin(u * 3.0f + t * 0.05f + _phaseB[cp]);
                Color col4 = Color.Lerp(_hueA[c], _hueB[c], mix) * (bright * glow);
                _colors.Add(col4);
            }

            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
        }
    }
}
