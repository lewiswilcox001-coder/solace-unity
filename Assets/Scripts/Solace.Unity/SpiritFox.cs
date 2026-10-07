// Solace.Unity — the ancient fox spirit (easter egg).
//
// On full-moon nights, the sim may decide the old one is watching. This is
// PURELY a visual: it never touches gameplay. A pale fox-shape stands on a
// ridge near the protagonist, faces it for a while, then sinks away.
//
// No transparency anywhere in this codebase (URP keyword risk), so the ghost
// is opaque pale moon-glow emissive, and "vanishing" is done by sinking into
// the ground + shrinking. It reads as a spirit, not a solid animal.
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class SpiritFox : MonoBehaviour, ISimView
    {
        private GameObject _root;
        private Material _ghostMat;
        private Material _eyeMat;
        private bool _visible;
        private float _fade; // 0..1, eased
        private WorldData _world;

        public void Build(WorldData world)
        {
            _world = world;
            GameBootstrap.Instance.RegisterView(this);

            // Pale moonlight body, faintly emissive. Opaque — no transparency.
            _ghostMat = MaterialFactory.LitEmissive(
                new Color(0.82f, 0.87f, 0.95f),
                new Color(0.55f, 0.65f, 0.9f) * 1.2f, 0.3f);
            _eyeMat = MaterialFactory.LitEmissive(
                new Color(0.9f, 0.95f, 1f),
                new Color(0.7f, 0.85f, 1f) * 2.5f, 0.2f);

            _root = new GameObject("SpiritFox");
            _root.transform.SetParent(transform, false);
            BuildGhost(_root);
            _root.SetActive(false);
        }

        private void AddPart(GameObject parent, string name, Mesh mesh, Material mat,
                            Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void BuildGhost(GameObject root)
        {
            Mesh sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            // Body: long low ghost-fox.
            AddPart(root, "Body", sphere, _ghostMat,
                new Vector3(0f, 0.62f, 0f), Quaternion.identity, new Vector3(0.55f, 0.5f, 1.15f));
            // Chest slightly raised, head up — the watching pose.
            AddPart(root, "Head", sphere, _ghostMat,
                new Vector3(0f, 1.05f, 0.72f), Quaternion.identity, new Vector3(0.34f, 0.32f, 0.36f));
            AddPart(root, "Snout", MeshFactory.Cone(0.11f, 0.3f, 6), _ghostMat,
                new Vector3(0f, 0.98f, 1.0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            // Ears.
            AddPart(root, "EarL", MeshFactory.Cone(0.09f, 0.28f, 5), _ghostMat,
                new Vector3(-0.13f, 1.32f, 0.66f), Quaternion.Euler(-12f, 0f, -10f), Vector3.one);
            AddPart(root, "EarR", MeshFactory.Cone(0.09f, 0.28f, 5), _ghostMat,
                new Vector3(0.13f, 1.32f, 0.66f), Quaternion.Euler(-12f, 0f, 10f), Vector3.one);
            // Tail: long plume curling up behind.
            AddPart(root, "Tail", sphere, _ghostMat,
                new Vector3(0f, 0.85f, -0.85f), Quaternion.Euler(-35f, 0f, 0f),
                new Vector3(0.3f, 0.3f, 0.85f));
            // Legs: simple, still.
            Mesh leg = MeshFactory.GetPrimitive(PrimitiveType.Cylinder);
            AddPart(root, "LegFL", leg, _ghostMat,
                new Vector3(-0.18f, 0.28f, 0.42f), Quaternion.identity, new Vector3(0.11f, 0.56f, 0.11f));
            AddPart(root, "LegFR", leg, _ghostMat,
                new Vector3(0.18f, 0.28f, 0.42f), Quaternion.identity, new Vector3(0.11f, 0.56f, 0.11f));
            AddPart(root, "LegBL", leg, _ghostMat,
                new Vector3(-0.18f, 0.28f, -0.42f), Quaternion.identity, new Vector3(0.11f, 0.56f, 0.11f));
            AddPart(root, "LegBR", leg, _ghostMat,
                new Vector3(0.18f, 0.28f, -0.42f), Quaternion.identity, new Vector3(0.11f, 0.56f, 0.11f));
            // Star eyes.
            AddPart(root, "EyeL", sphere, _eyeMat,
                new Vector3(-0.11f, 1.1f, 0.98f), Quaternion.identity, Vector3.one * 0.055f);
            AddPart(root, "EyeR", sphere, _eyeMat,
                new Vector3(0.11f, 1.1f, 0.98f), Quaternion.identity, Vector3.one * 0.055f);
        }

        public void SyncFromState(GameState state)
        {
            bool shouldShow = state.Eggs.SpiritVisibleUntil > state.ElapsedSeconds;
            if (shouldShow && !_visible)
            {
                // Appear: place at the spirit's spot, on the ground, facing the fox.
                _visible = true;
                _root.SetActive(true);
                float x = state.Eggs.SpiritX, z = state.Eggs.SpiritZ;
                float y = _world != null ? _world.SampleHeight(x, z) : 0f;
                _root.transform.position = new Vector3(x, y, z);
                Vector3 toFox = new Vector3(state.Agent.X - x, 0f, state.Agent.Z - z);
                if (toFox.sqrMagnitude > 0.01f)
                    _root.transform.rotation = Quaternion.LookRotation(toFox.normalized);
            }
            else if (!shouldShow && _visible)
            {
                _visible = false;
            }

            // Ease the fade; sink + shrink as it vanishes.
            float target = _visible ? 1f : 0f;
            _fade = Mathf.MoveTowards(_fade, target, Time.deltaTime * 0.5f);
            if (_fade <= 0f && !_visible)
            {
                _root.SetActive(false);
                return;
            }
            _root.SetActive(true);
            // Gentle hover-bob while visible; sinks as it fades.
            float bob = Mathf.Sin(Time.time * 0.8f) * 0.15f * _fade;
            Vector3 p = _root.transform.position;
            // Sink 1.2m as it vanishes.
            float baseY = _world != null ? _world.SampleHeight(p.x, p.z) : p.y;
            _root.transform.position = new Vector3(p.x, baseY + bob - (1f - _fade) * 1.2f, p.z);
            float s = 0.9f + 0.1f * _fade;
            _root.transform.localScale = new Vector3(s, _fade, s);
            // Slowly turn to keep watching the fox.
            if (_visible)
            {
                Vector3 toFox = new Vector3(state.Agent.X - p.x, 0f, state.Agent.Z - p.z);
                if (toFox.sqrMagnitude > 0.01f)
                {
                    Quaternion want = Quaternion.LookRotation(toFox.normalized);
                    _root.transform.rotation = Quaternion.Slerp(
                        _root.transform.rotation, want, Time.deltaTime * 0.6f);
                }
            }
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            SyncFromState(boot.Sim.State);
        }
    }
}
