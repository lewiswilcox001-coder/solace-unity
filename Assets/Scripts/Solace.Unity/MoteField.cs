// Solace.Unity — lightweight instanced mote system (fireflies, rain, embers).
//
// Replaces Unity's built-in particle module with allocation-free instanced draws,
// following the same pattern as the star dome in DayNightCycle: one mesh, one
// material, per-frame matrix updates, Graphics.DrawMeshInstanced in <=1023
// chunks. The codebase renders opaque only, so motes appear/disappear by
// scaling to zero — never alpha fading.
//
// All mote randomness is seeded via SeededRandom for deterministic worlds.
// Uses only UnityEngine core types (Graphics, Mesh, Material, Matrix4x4).
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public enum MoteBehavior
    {
        Drift, // slow sine wander around a base point (fireflies)
        Fall,  // fast downward, recycled to the top of the volume (rain)
        Rise,  // slow upward with flicker (embers)
    }

    public struct MoteConfig
    {
        public Mesh Mesh;
        public Material Material;
        public int Count;
        public MoteBehavior Behavior;
        public bool Spherical;   // true: base points in a sphere of Volume.x radius
        public Vector3 Volume;   // sphere: (radius,0,0); box: (x,y,z) extents
        public float MinScale;
        public float MaxScale;
        public float MinSpeed;
        public float MaxSpeed;
        public float VerticalRange; // Fall/Rise: cycle height; Drift: wander amplitude
    }

    /// <summary>
    /// Attach to a GameObject to render a field of instanced motes in its
    /// local space. Call Setup once, then SetIntensity (0..1) to fade the
    /// field in/out by scaling motes to zero. Update runs every frame and
    /// draws via Graphics.DrawMeshInstanced; no per-frame allocation.
    /// </summary>
    public class MoteField : MonoBehaviour
    {
        private const int MaxBatch = 1023; // DrawMeshInstanced per-call limit

        private Mesh _mesh;
        private Material _material;
        private MoteBehavior _behavior;
        private float _verticalRange;

        private Vector3[] _base;  // local-space anchors
        private float[] _phase;   // 0..2π wander/flicker phase
        private float[] _phase01; // 0..1 cycle offset for Fall/Rise
        private float[] _speed;
        private float[] _scale;

        private Matrix4x4[] _matrices;
        private Matrix4x4[][] _chunks;
        private int _count;
        private float _intensity = 1f;

        public void Setup(MoteConfig config, SeededRandom rng)
        {
            _mesh = config.Mesh;
            _material = config.Material;
            _behavior = config.Behavior;
            _verticalRange = Mathf.Max(config.VerticalRange, 0.001f);
            _count = Mathf.Max(config.Count, 1);

            _base = new Vector3[_count];
            _phase = new float[_count];
            _phase01 = new float[_count];
            _speed = new float[_count];
            _scale = new float[_count];
            _matrices = new Matrix4x4[_count];

            for (int i = 0; i < _count; i++)
            {
                if (config.Spherical)
                {
                    Vector3 d = new Vector3(rng.NextFloat(-1f, 1f),
                                            rng.NextFloat(-1f, 1f),
                                            rng.NextFloat(-1f, 1f));
                    if (d.sqrMagnitude < 0.0001f) d = Vector3.up;
                    d.Normalize();
                    float r = config.Volume.x * Mathf.Pow(rng.NextFloat(), 1f / 3f);
                    _base[i] = d * r;
                }
                else
                {
                    _base[i] = new Vector3(
                        rng.NextFloat(-config.Volume.x * 0.5f, config.Volume.x * 0.5f),
                        0f,
                        rng.NextFloat(-config.Volume.z * 0.5f, config.Volume.z * 0.5f));
                }
                _phase[i] = rng.NextFloat(0f, Mathf.PI * 2f);
                _phase01[i] = rng.NextFloat();
                _speed[i] = rng.NextFloat(config.MinSpeed, config.MaxSpeed);
                _scale[i] = rng.NextFloat(config.MinScale, config.MaxScale);
            }

            int chunkCount = (_count + MaxBatch - 1) / MaxBatch;
            _chunks = new Matrix4x4[chunkCount][];
            for (int c = 0; c < chunkCount; c++)
            {
                int n = Mathf.Min(MaxBatch, _count - c * MaxBatch);
                _chunks[c] = new Matrix4x4[n];
            }
        }

        public void SetIntensity(float v)
        {
            _intensity = Mathf.Clamp01(v);
        }

        private void Update()
        {
            if (_mesh == null || _material == null || _count == 0) return;
            int visible = Mathf.RoundToInt(_count * _intensity);
            if (visible <= 0) return;

            float t = Time.time;
            Matrix4x4 root = transform.localToWorldMatrix;
            Matrix4x4 zero = Matrix4x4.zero;
            Quaternion ident = Quaternion.identity;

            for (int i = 0; i < _count; i++)
            {
                if (i >= visible)
                {
                    _matrices[i] = zero;
                    continue;
                }
                Vector3 p;
                float s = _scale[i];
                switch (_behavior)
                {
                    case MoteBehavior.Drift:
                    {
                        float ph = _phase[i];
                        float w = 0.35f + 0.25f * Mathf.Sin(ph);
                        float amp = _verticalRange;
                        p = _base[i] + new Vector3(
                            Mathf.Sin(t * w + ph) * amp,
                            Mathf.Sin(t * w * 0.7f + ph * 1.7f) * amp * 0.5f,
                            Mathf.Cos(t * w * 0.85f + ph * 0.6f) * amp);
                        break;
                    }
                    case MoteBehavior.Fall:
                    {
                        float y = -(((t * _speed[i]) + _phase01[i] * _verticalRange) % _verticalRange);
                        p = new Vector3(_base[i].x, y, _base[i].z);
                        break;
                    }
                    default: // Rise
                    {
                        float y = ((t * _speed[i]) + _phase01[i] * _verticalRange) % _verticalRange;
                        p = new Vector3(_base[i].x, y, _base[i].z);
                        s *= 0.55f + 0.45f * Mathf.Sin(t * (1.5f + _speed[i]) + _phase[i]);
                        break;
                    }
                }
                _matrices[i] = root * Matrix4x4.TRS(p, ident, new Vector3(s, s, s));
            }

            for (int c = 0; c < _chunks.Length; c++)
            {
                Matrix4x4[] chunk = _chunks[c];
                System.Array.Copy(_matrices, c * MaxBatch, chunk, 0, chunk.Length);
                Graphics.DrawMeshInstanced(_mesh, 0, _material, chunk, chunk.Length,
                    null, ShadowCastingMode.Off, false);
            }
        }
    }
}
