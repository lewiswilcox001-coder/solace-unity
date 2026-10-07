// Solace.Unity — the colossal slow life, made visible.
//
// Tree-walkers: kilometer-scale migrating trees. The sim drifts them at tens
// of meters per game-day (invisible in real time), so the view layers a slow,
// majestic presentation drift on top of the sim position: bounded multi-sine
// migration along the sim heading (visible over a minute of watching, like
// clouds), a creaking lean into the view-wind, and a swaying canopy on its own
// pivot. Falling leaves, glowing seed-pods riding the wind, and birds circling
// the crown sell the scale. Fog does the atmospheric perspective for free —
// the crown dissolves into the sky haze. Seed-isles drift on the loch with a
// gentle bob, yaw wobble, and pulsing glow.
//
// Presentation only: the sim is never touched. All motion here is view-local.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class ColossusView : MonoBehaviour, ISimView
    {
        private const float Tau = 6.2831853f;

        private class Walker
        {
            public ColossusState State;
            public Transform Root;        // sim X/Z, terrain Y; yaw = sim heading
            public Transform LeanPivot;   // origin at ground; creaks into the wind
            public Transform CanopyPivot; // origin mid-trunk; the crown sways
            public Transform MistRing;
            public float Height, TrunkR, CanopyTopY, CanopyR;
            public float BaseX, BaseY, BaseZ, BaseYaw;
            public Vector3 CurPos;        // world pos this frame (base + drift)
            // Seeded presentation-motion params.
            public float DriftAmp1, DriftPer1, DriftPh1;
            public float DriftAmp2, DriftPer2, DriftPh2;
            public float MeandAmp, MeandPer, MeandPh;
            public float LeanAmpDeg, LeanPer, LeanPh;
            public float SwayAmpDeg, SwayPer, SwayPh;
            public float YawAmpDeg, YawPer, YawPh;
            public float MistSpin;
            public readonly List<Bird> Birds = new List<Bird>();
            public readonly List<Pod> Pods = new List<Pod>();
            public readonly List<Leaf> Leaves = new List<Leaf>();
        }

        private class Bird
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public float Radius, AngSpeed, Phase, Alt, BobAmp, BobPh, BankDeg;
        }

        private class Pod
        {
            public Vector3 Pos;
            public Vector3 Euler; // accumulated tumble rotation
            public Vector3 Scale;
            public float Seed;
            public float AttachTime, DriftTime;
            public float Timer;
            public bool Released;
            public Vector3 DriftVel;
            public float BaseScale;
        }

        private class Leaf
        {
            public Vector3 Pos;
            public Vector3 Euler; // accumulated tumble rotation
            public Vector3 Scale;
            public float Seed;
            public float FallSpeed, FallTime, Timer;
            public Vector3 Vel;
            public float Tumble1, Tumble2;
            public float BaseScale;
        }

        private class Isle
        {
            public ColossusState State;
            public Transform Root;
            public float BobPhase;
            public float DriftAmp, DriftPer, DriftPh;
            public float YawAmp, YawPer, YawPh;
            public readonly List<Transform> GlowDots = new List<Transform>();
        }

        private readonly List<Walker> _walkers = new List<Walker>();
        private readonly List<Isle> _isles = new List<Isle>();
        private WorldData _world;

        // View-local wind: slow, majestic, seeded per world. Presentation only.
        private float _windBaseAngle;
        private Vector3 _windDir = Vector3.forward;
        private float _windStr = 1f;

        // Instanced ambient: birds, pods, leaves (was 75 GameObjects, now 3 draws).
        private Mesh _birdMesh;
        private Material _birdMat;
        private Mesh _podMesh;
        private Material _podMat;
        private Mesh _leafMesh;
        private Material _leafMat;
        private readonly List<Matrix4x4> _birdMats = new List<Matrix4x4>(16);
        private readonly List<Matrix4x4> _podMats = new List<Matrix4x4>(32);
        private readonly List<Matrix4x4> _leafMats = new List<Matrix4x4>(48);
        private readonly Matrix4x4[] _birdArr = new Matrix4x4[16];
        private readonly Matrix4x4[] _podArr = new Matrix4x4[32];
        private readonly Matrix4x4[] _leafArr = new Matrix4x4[48];

        public void Build(WorldData world)
        {
            _world = world;
            GameBootstrap.Instance.RegisterView(this);
            var wrng = SeededRandom.Derive(world.Seed, "unity-wind");
            _windBaseAngle = wrng.NextFloat(0f, Tau);
            foreach (var c in world.Colossi)
            {
                if (c.Kind == ColossusKind.TreeWalker) BuildWalker(c);
                else BuildIsle(c);
            }
        }

        // -- tree-walkers ------------------------------------------------------

        private void BuildWalker(ColossusState c)
        {
            var rng = SeededRandom.Derive(c.Id * 104729 + 7, "unity-walker");
            var root = new GameObject("TreeWalker_" + c.Id).transform;
            root.SetParent(transform, false);

            // Kilometer-scale: the sim gives RadiusMeters = 500, so honor it —
            // clamp keeps the crown inside the camera far plane and readable
            // through the haze.
            float height = Mathf.Clamp(c.RadiusMeters * 2.2f, 380f, 620f);
            float trunkR = height * 0.045f;
            float canopyTopY = height * 0.98f;
            float canopyR = height * 0.26f;

            var bark = MaterialFactory.Lit(new Color(0.23f, 0.16f, 0.11f), 0.3f);
            var canopy = MaterialFactory.Lit(new Color(0.10f, 0.26f, 0.17f), 0.25f);
            var mist = MaterialFactory.Lit(new Color(0.68f, 0.74f, 0.78f), 0.4f);

            var lean = new GameObject("LeanPivot").transform;
            lean.SetParent(root, false);
            var canopyPivot = new GameObject("CanopyPivot").transform;
            canopyPivot.SetParent(lean, false);
            canopyPivot.localPosition = new Vector3(0f, height * 0.55f, 0f);

            // Trunk: a great tapered column.
            MeshFactory.AddMesh(lean.gameObject, "Trunk", MeshFactory.Cone(trunkR, height, 9), bark,
                Vector3.zero, Vector3.one, Quaternion.identity);
            // Root flare so the giant reads as planted, not stuck on.
            MeshFactory.AddMesh(lean.gameObject, "RootFlare",
                MeshFactory.Cone(trunkR * 2.4f, height * 0.05f, 9), bark,
                Vector3.zero, Vector3.one, Quaternion.identity);

            // Canopy: broad flattened shelves near the crown, on the sway pivot
            // (y relative to the pivot at 0.55 * height).
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float y = height * (0.72f + 0.26f * t) - height * 0.55f;
                float r = height * (0.16f + 0.10f * Mathf.Sin(t * Mathf.PI));
                float a = rng.NextFloat(0f, Tau);
                MeshFactory.AddMesh(canopyPivot.gameObject, "Canopy" + i, sphere, canopy,
                    new Vector3(Mathf.Cos(a) * r * 0.5f, y, Mathf.Sin(a) * r * 0.5f),
                    new Vector3(r, r * 0.28f, r), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
            }
            // Hanging moss strands (thin cones dangling from the shelves).
            for (int i = 0; i < 8; i++)
            {
                float a = rng.NextFloat(0f, Tau);
                float r = trunkR * rng.NextFloat(1.5f, 3f);
                float y = height * rng.NextFloat(0.55f, 0.8f) - height * 0.55f;
                MeshFactory.AddMesh(canopyPivot.gameObject, "Moss" + i,
                    MeshFactory.Cone(1.2f, height * 0.08f, 5), canopy,
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
                float a = (i / 4f) * Tau;
                MeshFactory.AddMesh(mistRing.gameObject, "Mist" + i, sphere, mist,
                    new Vector3(Mathf.Cos(a) * trunkR * 2.2f, 6f, Mathf.Sin(a) * trunkR * 2.2f),
                    new Vector3(trunkR * 2.6f, trunkR * 1.1f, trunkR * 2.6f), Quaternion.identity);
            }

            var w = new Walker
            {
                State = c, Root = root, LeanPivot = lean, CanopyPivot = canopyPivot,
                MistRing = mistRing,
                Height = height, TrunkR = trunkR, CanopyTopY = canopyTopY, CanopyR = canopyR,
                // Bounded multi-sine migration: peak drift ~0.5 m/s, incommensurate
                // periods so it never reads as a loop. Like watching clouds move.
                DriftAmp1 = rng.NextFloat(22f, 34f), DriftPer1 = rng.NextFloat(480f, 660f),
                DriftPh1 = rng.NextFloat(0f, Tau),
                DriftAmp2 = rng.NextFloat(8f, 16f), DriftPer2 = rng.NextFloat(190f, 280f),
                DriftPh2 = rng.NextFloat(0f, Tau),
                MeandAmp = rng.NextFloat(12f, 22f), MeandPer = rng.NextFloat(560f, 800f),
                MeandPh = rng.NextFloat(0f, Tau),
                LeanAmpDeg = rng.NextFloat(0.35f, 0.6f), LeanPer = rng.NextFloat(140f, 220f),
                LeanPh = rng.NextFloat(0f, Tau),
                SwayAmpDeg = rng.NextFloat(0.35f, 0.55f), SwayPer = rng.NextFloat(38f, 58f),
                SwayPh = rng.NextFloat(0f, Tau),
                YawAmpDeg = rng.NextFloat(1.5f, 3f), YawPer = rng.NextFloat(420f, 600f),
                YawPh = rng.NextFloat(0f, Tau),
                MistSpin = rng.NextFloat(0.008f, 0.02f),
            };

            BuildBirds(w, rng);
            BuildPods(w, rng);
            BuildLeaves(w, rng);

            _walkers.Add(w);
            SyncWalker(w);
        }

        private Transform AddWorldMesh(string name, Mesh mesh, Material mat, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = scale;
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        private void BuildBirds(Walker w, SeededRandom rng)
        {
            // Lazily create shared instancing resources.
            if (_birdMesh == null)
            {
                _birdMesh = MeshFactory.Cone(0.5f, 2.4f, 4);
                _birdMat = MaterialFactory.Lit(new Color(0.08f, 0.09f, 0.11f), 0.35f);
            }
            for (int i = 0; i < 5; i++)
            {
                float radius = w.CanopyR * rng.NextFloat(1.1f, 1.7f);
                float tangential = rng.NextFloat(10f, 18f); // m/s, a soaring glide
                w.Birds.Add(new Bird
                {
                    Pos = Vector3.zero,
                    Rot = Quaternion.identity,
                    Radius = radius,
                    AngSpeed = tangential / radius * (rng.NextFloat() < 0.5f ? 1f : -1f),
                    Phase = rng.NextFloat(0f, Tau),
                    Alt = w.CanopyTopY * rng.NextFloat(0.92f, 1.05f),
                    BobAmp = rng.NextFloat(3f, 7f),
                    BobPh = rng.NextFloat(0f, Tau),
                    BankDeg = rng.NextFloat(12f, 20f),
                });
            }
        }

        private void BuildPods(Walker w, SeededRandom rng)
        {
            if (_podMesh == null)
            {
                _podMesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
                _podMat = MaterialFactory.LitEmissive(new Color(0.85f, 0.68f, 0.30f),
                    new Color(1.0f, 0.78f, 0.38f), 0.4f);
            }
            for (int i = 0; i < 8; i++)
            {
                float s = rng.NextFloat(1.0f, 1.7f);
                w.Pods.Add(new Pod
                {
                    Pos = Vector3.zero,
                    Euler = Vector3.zero,
                    Scale = new Vector3(s, s * 1.5f, s),
                    Seed = rng.NextFloat(0f, 1f),
                    AttachTime = rng.NextFloat(6f, 14f),
                    DriftTime = rng.NextFloat(30f, 45f),
                    Timer = rng.NextFloat(0f, 40f), // stagger the cycles
                    BaseScale = s,
                });
            }
        }

        private void BuildLeaves(Walker w, SeededRandom rng)
        {
            if (_leafMesh == null)
            {
                _leafMesh = MeshFactory.Disc(0.7f, 5);
                _leafMat = MaterialFactory.Lit(new Color(0.35f, 0.52f, 0.22f), 0.3f);
            }
            for (int i = 0; i < 12; i++)
            {
                float s = rng.NextFloat(0.8f, 1.4f);
                w.Leaves.Add(new Leaf
                {
                    Pos = Vector3.zero,
                    Euler = Vector3.zero,
                    Scale = Vector3.one * s,
                    Seed = rng.NextFloat(0f, 1f),
                    FallSpeed = rng.NextFloat(2.2f, 3.6f),
                    FallTime = rng.NextFloat(10f, 16f),
                    Timer = rng.NextFloat(0f, 14f), // stagger the cycles
                    Tumble1 = rng.NextFloat(1.5f, 3.5f),
                    Tumble2 = rng.NextFloat(1.0f, 2.5f),
                    BaseScale = s,
                });
            }
        }

        // -- seed-isles ----------------------------------------------------------

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
                float a = rng.NextFloat(0f, Tau);
                float d = rng.NextFloat(0f, r * 0.6f);
                var tp = new Vector3(Mathf.Cos(a) * d, 1.2f, Mathf.Sin(a) * d);
                MeshFactory.AddMesh(root.gameObject, "Tree" + i, MeshFactory.PineCrown(), leaf,
                    tp, Vector3.one * 1.6f, Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
                MeshFactory.AddMesh(root.gameObject, "Trunk" + i,
                    MeshFactory.GetPrimitive(PrimitiveType.Cylinder),
                    MaterialFactory.Lit(new Color(0.25f, 0.16f, 0.10f), 0.25f),
                    tp, new Vector3(0.5f, 1.2f, 0.5f), Quaternion.identity);
            }
            var isle = new Isle
            {
                State = c, Root = root, BobPhase = rng.NextFloat(0f, Tau),
                DriftAmp = rng.NextFloat(8f, 14f), DriftPer = rng.NextFloat(300f, 480f),
                DriftPh = rng.NextFloat(0f, Tau),
                YawAmp = rng.NextFloat(2f, 4f), YawPer = rng.NextFloat(240f, 400f),
                YawPh = rng.NextFloat(0f, Tau),
            };
            for (int i = 0; i < 6; i++)
            {
                float a = rng.NextFloat(0f, Tau);
                float d = rng.NextFloat(0f, r * 0.8f);
                var gr = MeshFactory.AddMesh(root.gameObject, "Glow" + i, sphere, glow,
                    new Vector3(Mathf.Cos(a) * d, 1.6f, Mathf.Sin(a) * d),
                    Vector3.one * 0.8f, Quaternion.identity);
                isle.GlowDots.Add(gr.transform);
            }

            _isles.Add(isle);
            SyncIsle(isle);
        }

        // -- per-frame -----------------------------------------------------------

        public void SyncFromState(GameState state)
        {
            for (int i = 0; i < _walkers.Count; i++) SyncWalker(_walkers[i]);
            for (int i = 0; i < _isles.Count; i++) SyncIsle(_isles[i]);
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);

            float t = Time.time;
            float dt = Time.deltaTime;

            // View-local wind: direction wheels slowly, strength breathes.
            float windAng = _windBaseAngle
                + 0.7f * Mathf.Sin(t * Tau / 613f)
                + 0.3f * Mathf.Sin(t * Tau / 197f);
            _windDir = new Vector3(Mathf.Sin(windAng), 0f, Mathf.Cos(windAng));
            _windStr = 1f + 0.5f * Mathf.Sin(t * Tau / 431f + 1.7f);

            _birdMats.Clear();
            _podMats.Clear();
            _leafMats.Clear();
            for (int i = 0; i < _walkers.Count; i++) UpdateWalker(_walkers[i], t, dt);
            for (int i = 0; i < _isles.Count; i++) UpdateIsle(_isles[i], t);

            // Instanced draws: 3 draw calls for all birds/pods/leaves.
            if (_birdMats.Count > 0 && _birdMesh != null)
            {
                _birdMats.CopyTo(_birdArr);
                Graphics.DrawMeshInstanced(_birdMesh, 0, _birdMat, _birdArr,
                    _birdMats.Count, null, ShadowCastingMode.Off, false);
            }
            if (_podMats.Count > 0 && _podMesh != null)
            {
                _podMats.CopyTo(_podArr);
                Graphics.DrawMeshInstanced(_podMesh, 0, _podMat, _podArr,
                    _podMats.Count, null, ShadowCastingMode.Off, false);
            }
            if (_leafMats.Count > 0 && _leafMesh != null)
            {
                _leafMats.CopyTo(_leafArr);
                Graphics.DrawMeshInstanced(_leafMesh, 0, _leafMat, _leafArr,
                    _leafMats.Count, null, ShadowCastingMode.Off, false);
            }
        }

        private void UpdateWalker(Walker w, float t, float dt)
        {
            // 1. Migration drift on top of the sim position.
            float d1 = w.DriftAmp1 * Mathf.Sin(t * Tau / w.DriftPer1 + w.DriftPh1);
            float d2 = w.DriftAmp2 * Mathf.Sin(t * Tau / w.DriftPer2 + w.DriftPh2);
            float lat = w.MeandAmp * Mathf.Sin(t * Tau / w.MeandPer + w.MeandPh);
            var hdir = new Vector3(Mathf.Sin(w.State.Heading), 0f, Mathf.Cos(w.State.Heading));
            var hperp = new Vector3(hdir.z, 0f, -hdir.x);
            w.CurPos = new Vector3(w.BaseX, w.BaseY, w.BaseZ) + hdir * (d1 + d2) + hperp * lat;

            float yawWob = w.YawAmpDeg * Mathf.Sin(t * Tau / w.YawPer + w.YawPh);
            w.Root.position = w.CurPos;
            w.Root.rotation = Quaternion.Euler(0f, w.BaseYaw + yawWob, 0f);

            // 2. Creaking lean into the wind (world-space axis, tiny angle).
            float leanDeg = w.LeanAmpDeg * (0.65f + 0.35f * Mathf.Sin(t * Tau / w.LeanPer + w.LeanPh));
            var leanAxis = Vector3.Cross(Vector3.up, _windDir);
            if (leanAxis.sqrMagnitude > 0.0001f)
                w.LeanPivot.rotation = Quaternion.AngleAxis(leanDeg, leanAxis.normalized);
            else
                w.LeanPivot.rotation = Quaternion.identity;

            // 3. Canopy sway, out of phase with the lean.
            float sx = w.SwayAmpDeg * Mathf.Sin(t * Tau / w.SwayPer + w.SwayPh);
            float sz = w.SwayAmpDeg * 0.7f * Mathf.Sin(t * Tau / (w.SwayPer * 1.31f) + w.SwayPh * 1.7f);
            w.CanopyPivot.rotation = Quaternion.Euler(sx, 0f, sz);

            // 4. Slow swirl of the base mist.
            if (w.MistRing != null)
                w.MistRing.Rotate(0f, w.MistSpin * dt * 57.3f, 0f);

            UpdateBirds(w, t);
            UpdatePods(w, t, dt);
            UpdateLeaves(w, t, dt);
        }

        private void UpdateBirds(Walker w, float t)
        {
            for (int i = 0; i < w.Birds.Count; i++)
            {
                Bird b = w.Birds[i];
                float a = b.Phase + t * b.AngSpeed;
                float y = w.CurPos.y + b.Alt + Mathf.Sin(t * 0.5f + b.BobPh) * b.BobAmp;
                b.Pos = new Vector3(
                    w.CurPos.x + Mathf.Cos(a) * b.Radius, y,
                    w.CurPos.z + Mathf.Sin(a) * b.Radius);
                // Face along the velocity; bank into the turn.
                float dirSign = b.AngSpeed >= 0f ? 1f : -1f;
                var vel = new Vector3(-Mathf.Sin(a) * dirSign, 0f, Mathf.Cos(a) * dirSign);
                float yawDeg = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
                b.Rot = Quaternion.AngleAxis(yawDeg, Vector3.up)
                      * Quaternion.AngleAxis(b.BankDeg * dirSign, Vector3.forward)
                      * Quaternion.Euler(90f, 0f, 0f); // tip cone forward, +Z = beak
                // Bird body scale (was on the child GameObject).
                _birdMats.Add(Matrix4x4.TRS(b.Pos, b.Rot, new Vector3(1.6f, 1.6f, 1.6f)));
            }
        }

        private void UpdatePods(Walker w, float t, float dt)
        {
            for (int i = 0; i < w.Pods.Count; i++)
            {
                Pod p = w.Pods[i];
                p.Timer += dt;
                float total = p.AttachTime + p.DriftTime + 3f;
                if (p.Timer >= total)
                {
                    p.Timer = 0f;
                    p.Released = false;
                }

                if (p.Timer < p.AttachTime)
                {
                    // Clinging to the canopy rim, swaying.
                    float ang = p.Seed * Tau + t * 0.06f;
                    float rr = w.CanopyR * (0.8f + 0.15f * p.Seed);
                    p.Pos = w.CurPos + new Vector3(
                        Mathf.Cos(ang) * rr,
                        w.CanopyTopY - 6f - p.Seed * 30f + Mathf.Sin(t * 0.8f + p.Seed * 9f) * 1.5f,
                        Mathf.Sin(ang) * rr);
                    p.Scale = new Vector3(p.BaseScale, p.BaseScale * 1.5f, p.BaseScale);
                    p.Euler = Vector3.zero;
                }
                else if (p.Timer < p.AttachTime + p.DriftTime)
                {
                    if (!p.Released)
                    {
                        p.Released = true;
                        p.DriftVel = _windDir * _windStr * (5f + 4f * p.Seed);
                    }
                    p.Pos += (p.DriftVel + Vector3.down * 1.2f) * dt;
                    p.Euler += new Vector3(dt * 40f * (0.5f + p.Seed), dt * 25f, 0f);
                }
                else
                {
                    // Shrink out instead of fading (no transparency anywhere).
                    float k = 1f - (p.Timer - p.AttachTime - p.DriftTime) / 3f;
                    float s = p.BaseScale * Mathf.Max(0.001f, k);
                    p.Scale = new Vector3(s, s * 1.5f, s);
                }
                _podMats.Add(Matrix4x4.TRS(p.Pos, Quaternion.Euler(p.Euler), p.Scale));
            }
        }

        private void UpdateLeaves(Walker w, float t, float dt)
        {
            var perp = new Vector3(_windDir.z, 0f, -_windDir.x);
            for (int i = 0; i < w.Leaves.Count; i++)
            {
                Leaf l = w.Leaves[i];
                l.Timer += dt;
                if (l.Timer >= l.FallTime)
                {
                    // Respawn at a random shelf edge.
                    l.Timer = 0f;
                    float ang = l.Seed * 39.7f + t * 0.13f;
                    float rr = w.CanopyR * (0.55f + 0.4f * l.Seed);
                    float shelfY = w.Height * (0.72f + 0.26f * l.Seed);
                    l.Pos = w.CurPos + new Vector3(
                        Mathf.Cos(ang) * rr, shelfY, Mathf.Sin(ang) * rr);
                    l.Vel = _windDir * _windStr * (1.5f + l.Seed * 2f);
                    l.Scale = Vector3.one * l.BaseScale;
                    l.Euler = Vector3.zero;
                }
                else
                {
                    float flutter = Mathf.Sin(t * 2.5f + l.Seed * 20f) * 1.5f;
                    l.Pos += (l.Vel + Vector3.down * l.FallSpeed + perp * flutter) * dt;
                    l.Euler += new Vector3(l.Tumble1 * dt * 57.3f, l.Tumble2 * dt * 57.3f, 0f);
                    if (l.Timer > l.FallTime - 2f)
                    {
                        float k = Mathf.Max(0.001f, (l.FallTime - l.Timer) / 2f);
                        l.Scale = Vector3.one * (l.BaseScale * k);
                    }
                }
                _leafMats.Add(Matrix4x4.TRS(l.Pos, Quaternion.Euler(l.Euler), l.Scale));
            }
        }

        private void UpdateIsle(Isle isle, float t)
        {
            float dx = isle.DriftAmp * Mathf.Sin(t * Tau / isle.DriftPer + isle.DriftPh);
            float dz = isle.DriftAmp * 0.7f * Mathf.Sin(t * Tau / (isle.DriftPer * 1.37f) + isle.DriftPh * 1.3f);
            Vector3 p = isle.Root.position;
            p.x = isle.State.X + dx;
            p.z = isle.State.Z + dz;
            p.y = WorldData.WaterLevel + 0.9f + Mathf.Sin(t * 0.25f + isle.BobPhase) * 0.5f;
            isle.Root.position = p;
            float yawWob = isle.YawAmp * Mathf.Sin(t * Tau / isle.YawPer + isle.YawPh);
            isle.Root.rotation = Quaternion.Euler(0f, isle.State.Heading * Mathf.Rad2Deg + yawWob, 0f);
            // Breathing glow.
            for (int i = 0; i < isle.GlowDots.Count; i++)
            {
                float s = 0.8f * (0.8f + 0.35f * Mathf.Sin(t * 1.8f + isle.BobPhase + i * 1.1f));
                isle.GlowDots[i].localScale = Vector3.one * s;
            }
        }

        // -- sim mirror (caches base values; presentation is layered in Update) --

        private void SyncWalker(Walker w)
        {
            w.BaseX = w.State.X;
            w.BaseZ = w.State.Z;
            w.BaseY = _world.SampleHeight(w.State.X, w.State.Z) - 3f;
            w.BaseYaw = w.State.Heading * Mathf.Rad2Deg;
        }

        private void SyncIsle(Isle isle)
        {
            // X/Z/Y are animated in UpdateIsle; keep a sane initial placement.
            if (isle.Root.position.sqrMagnitude < 0.001f)
                isle.Root.position = new Vector3(isle.State.X, WorldData.WaterLevel + 0.9f, isle.State.Z);
        }
    }
}
