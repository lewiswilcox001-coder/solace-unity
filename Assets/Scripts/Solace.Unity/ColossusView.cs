// Solace.Unity — the colossal slow life, made visible.
//
// Tree-walkers: 150–300m trees drifting on their sim headings. Seed-isles:
// floating islands drifting on the loch. Positions come straight from
// ColossusState each frame (the sim moves them coarsely; we just mirror).
// Opaque mist puffs ring each tree-walker's base and swirl slowly. These are
// the awe shots — silhouette and scale do the work.
using System.Collections.Generic;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class ColossusView : MonoBehaviour, ISimView
    {
        private class Walker
        {
            public ColossusState State;
            public Transform Root;
            public Transform MistRing;
            public float Spin;
        }

        private class Isle
        {
            public ColossusState State;
            public Transform Root;
            public float BobPhase;
        }

        private readonly List<Walker> _walkers = new List<Walker>();
        private readonly List<Isle> _isles = new List<Isle>();
        private WorldData _world;

        public void Build(WorldData world)
        {
            _world = world;
            GameBootstrap.Instance.RegisterView(this);
            foreach (var c in world.Colossi)
            {
                if (c.Kind == ColossusKind.TreeWalker) BuildWalker(c);
                else BuildIsle(c);
            }
        }

        private void BuildWalker(ColossusState c)
        {
            var root = new GameObject("TreeWalker_" + c.Id).transform;
            root.SetParent(transform, false);

            float height = Mathf.Clamp(c.RadiusMeters * 2.2f, 150f, 300f);
            float trunkR = height * 0.045f;
            var bark = MaterialFactory.Lit(new Color(0.16f, 0.11f, 0.08f), 0.25f);
            var canopy = MaterialFactory.Lit(new Color(0.07f, 0.20f, 0.14f), 0.20f);
            var mist = MaterialFactory.Lit(new Color(0.68f, 0.74f, 0.78f), 0.4f);

            // Trunk: a great tapered column.
            MeshFactory.AddMesh(root.gameObject, "Trunk", MeshFactory.Cone(trunkR, height, 9), bark,
                Vector3.zero, Vector3.one, Quaternion.identity);
            // Canopy: broad flattened shelves near the crown.
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            var rng = SeededRandom.Derive(c.Id * 104729 + 7, "unity-walker");
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float y = height * (0.72f + 0.26f * t);
                float r = height * (0.16f + 0.10f * Mathf.Sin(t * Mathf.PI));
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                MeshFactory.AddMesh(root.gameObject, "Canopy" + i, sphere, canopy,
                    new Vector3(Mathf.Cos(a) * r * 0.5f, y, Mathf.Sin(a) * r * 0.5f),
                    new Vector3(r, r * 0.28f, r), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
            }
            // Hanging moss strands (thin cones dangling from the shelves).
            for (int i = 0; i < 8; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = trunkR * rng.NextFloat(1.5f, 3f);
                float y = height * rng.NextFloat(0.55f, 0.8f);
                MeshFactory.AddMesh(root.gameObject, "Moss" + i, MeshFactory.Cone(1.2f, height * 0.08f, 5), canopy,
                    new Vector3(Mathf.Cos(a) * r, y - height * 0.08f, Mathf.Sin(a) * r),
                    Vector3.one, Quaternion.Euler(180f, 0f, 0f));
            }

            // Mist puffs at the base (opaque pale ellipsoids — they read as fog
            // at distance and need no transparency). On their own ring so the
            // heading sync never fights the slow swirl.
            var mistRing = new GameObject("MistRing").transform;
            mistRing.SetParent(root, false);
            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f) * Mathf.PI * 2f;
                MeshFactory.AddMesh(mistRing.gameObject, "Mist" + i, sphere, mist,
                    new Vector3(Mathf.Cos(a) * trunkR * 2.2f, 6f, Mathf.Sin(a) * trunkR * 2.2f),
                    new Vector3(trunkR * 2.6f, trunkR * 1.1f, trunkR * 2.6f), Quaternion.identity);
            }

            _walkers.Add(new Walker
            {
                State = c, Root = root, MistRing = mistRing,
                Spin = rng.NextFloat(0.008f, 0.02f)
            });
            SyncWalker(_walkers[_walkers.Count - 1]);
        }

        private void BuildIsle(ColossusState c)
        {
            var root = new GameObject("SeedIsle_" + c.Id).transform;
            root.SetParent(transform, false);

            float r = Mathf.Max(10f, c.RadiusMeters * 0.5f);
            var rock = MaterialFactory.Lit(new Color(0.30f, 0.28f, 0.31f), 0.35f);
            var grass = MaterialFactory.Lit(new Color(0.24f, 0.40f, 0.21f), 0.25f);
            var leaf = MaterialFactory.Lit(new Color(0.08f, 0.22f, 0.16f), 0.20f);
            var glow = MaterialFactory.LitEmissive(new Color(0.12f, 0.5f, 0.45f),
                                                    new Color(0.15f, 0.7f, 0.62f), 0.5f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);

            // Island: grassy disc over an inverted rock cone.
            MeshFactory.AddMesh(root.gameObject, "Top", MeshFactory.Disc(r, 14), grass,
                new Vector3(0f, 1.2f, 0f), Vector3.one, Quaternion.identity);
            MeshFactory.AddMesh(root.gameObject, "Keel", MeshFactory.Cone(r * 0.9f, r * 1.6f, 9), rock,
                new Vector3(0f, 1.2f - r * 1.6f, 0f), Vector3.one, Quaternion.identity);
            // A couple of small trees + glow dots.
            var rng = SeededRandom.Derive(c.Id * 104729 + 31, "unity-isle");
            for (int i = 0; i < 3; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float d = rng.NextFloat(0f, r * 0.6f);
                var tp = new Vector3(Mathf.Cos(a) * d, 1.2f, Mathf.Sin(a) * d);
                MeshFactory.AddMesh(root.gameObject, "Tree" + i, MeshFactory.PineCrown(), leaf,
                    tp, Vector3.one * 1.6f, Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
                MeshFactory.AddMesh(root.gameObject, "Trunk" + i,
                    MeshFactory.GetPrimitive(PrimitiveType.Cylinder),
                    MaterialFactory.Lit(new Color(0.25f, 0.16f, 0.10f), 0.25f),
                    tp, new Vector3(0.5f, 1.2f, 0.5f), Quaternion.identity);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float d = rng.NextFloat(0f, r * 0.8f);
                MeshFactory.AddMesh(root.gameObject, "Glow" + i, sphere, glow,
                    new Vector3(Mathf.Cos(a) * d, 1.6f, Mathf.Sin(a) * d),
                    Vector3.one * 0.8f, Quaternion.identity);
            }

            _isles.Add(new Isle { State = c, Root = root, BobPhase = rng.NextFloat(0f, 6.28f) });
            SyncIsle(_isles[_isles.Count - 1]);
        }

        public void SyncFromState(GameState state)
        {
            for (int i = 0; i < _walkers.Count; i++) SyncWalker(_walkers[i]);
            for (int i = 0; i < _isles.Count; i++) SyncIsle(_isles[i]);
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);

            // Slow swirl of the base mist; gentle bob of the isles.
            float t = Time.time;
            for (int i = 0; i < _walkers.Count; i++)
            {
                Walker w = _walkers[i];
                if (w.MistRing != null)
                    w.MistRing.Rotate(0f, w.Spin * Time.deltaTime * 57.3f, 0f);
            }
            for (int i = 0; i < _isles.Count; i++)
            {
                Isle isle = _isles[i];
                Vector3 p = isle.Root.position;
                p.y = WorldData.WaterLevel + 0.9f + Mathf.Sin(t * 0.25f + isle.BobPhase) * 0.5f;
                isle.Root.position = p;
            }
        }

        private void SyncWalker(Walker w)
        {
            float y = _world.SampleHeight(w.State.X, w.State.Z) - 3f;
            w.Root.position = new Vector3(w.State.X, y, w.State.Z);
            w.Root.rotation = Quaternion.Euler(0f, w.State.Heading * Mathf.Rad2Deg, 0f);
        }

        private void SyncIsle(Isle isle)
        {
            // Y is animated in Update (bob); X/Z mirror the sim.
            Vector3 p = isle.Root.position;
            p.x = isle.State.X; p.z = isle.State.Z;
            isle.Root.position = p;
            isle.Root.rotation = Quaternion.Euler(0f, isle.State.Heading * Mathf.Rad2Deg, 0f);
        }
    }
}
