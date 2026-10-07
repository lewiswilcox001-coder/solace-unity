// Solace.Unity — the den's hearth heartbeat.
//
// Breathes the entrance hearth-glow's emission so the den flickers like a
// lived-in fire. Purely emissive animation — no lights touched, no per-frame
// allocations, and each den owns its material so flickers never sync up.
using UnityEngine;

namespace Solace.Unity
{
    public class DenHearth : MonoBehaviour
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private Material _mat;
        private Color _baseEmission = Color.white;
        private float _phase;

        public void Init(Material mat, Color baseEmission, float phase)
        {
            _mat = mat;
            _baseEmission = baseEmission;
            _phase = phase;
        }

        private void Update()
        {
            if (_mat == null) return;
            float t = Time.time + _phase;
            // Two detuned sines: a slow breath plus a fast lick of flame.
            float f = 0.82f + 0.12f * Mathf.Sin(t * 6.3f) + 0.06f * Mathf.Sin(t * 15.7f);
            _mat.SetColor(EmissionId, _baseEmission * f);
        }
    }
}
