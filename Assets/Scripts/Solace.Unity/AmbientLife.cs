// Solace.Unity — ambient wildlife: birds, butterflies, fish.
//
// Pure presentation: deterministic placement from the world seed, procedural
// animation in real time. Writes no sim state and runs no AI — these lives
// are atmosphere. Everything renders instanced: a handful of tiny draw calls,
// sized for integrated graphics.
//
// Self-bootstrapping: created via RuntimeInitializeOnLoadMethod and lazily
// built from the world once the sim exists; rebuilds whenever a new life
// starts (detected via seed change). GameBootstrap is untouched.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class AmbientLife : MonoBehaviour, ISimView
    {
        private const int FlockCount = 3;
        private const int BirdsPerFlock = 7;
        private const int BirdTotal = FlockCount * BirdsPerFlock;
        private const int ButterflyCount = 14;
        private const int ButterflyColors = 3;
        private const int FishCount = 10;
        private const int RipplePoolSize = 6;

        // -- shared meshes (built once, reused across lives) ------------------

        private static bool _meshesBuilt;
        private static Mesh _birdBodyMesh;
        private static Mesh _birdWingRMesh;
        private static Mesh _birdWingLMesh;
        private static Mesh _bflyBodyMesh;
        private static Mesh _bflyWingRMesh;
        private static Mesh _bflyWingLMesh;
        private static Mesh _fishMesh;
        private static Mesh _rippleMesh;

        // -- materials ----------------------------------------------------------

        private Material _birdMat;
        private Material _bflyBodyMat;
        private readonly Material[] _bflyWingMats = new Material[ButterflyColors];
        private Material _fishMat;
        private Material _rippleMat;

        // -- cached sim state ---------------------------------------------------

        private float _foxX, _foxZ, _foxSpeed;
        private float _timeOfDay = 12f;
        private bool _isNight;
        private bool _isRaining;

        // -- world --------------------------------------------------------------

        private WorldData _world;
        private bool _built;
        private int _builtSeed = -1;

        // -- birds --------------------------------------------------------------

        private sealed class BirdFlock
        {
            public Vector3 home;      // circling anchor (y = terrain height)
            public Vector3 perch;     // landing spot (terrain + lift)
            public float circleRadius = 24f;
            public float phase;
            public float angSpeed = 0.3f;
            public float cruiseHeight = 20f;
            public int mode;          // 0 fly, 1 landing, 2 perched, 3 takeoff
            public float modeT;
            public float modeDur = 50f;
            public readonly float[] orbitR = new float[BirdsPerFlock];
            public readonly float[] orbitW = new float[BirdsPerFlock];
            public readonly float[] orbitP = new float[BirdsPerFlock];
            public readonly float[] bobF = new float[BirdsPerFlock];
            public readonly float[] bobP = new float[BirdsPerFlock];
            public readonly float[] flapP = new float[BirdsPerFlock];
            public readonly float[] scale = new float[BirdsPerFlock];
            public readonly float[] yaw = new float[BirdsPerFlock];
            public readonly float[] perchYaw = new float[BirdsPerFlock];
            public readonly Vector3[] prevPos = new Vector3[BirdsPerFlock];
            public readonly Vector3[] perchPos = new Vector3[BirdsPerFlock];
        }

        private readonly List<BirdFlock> _flocks = new List<BirdFlock>();
        private readonly Matrix4x4[] _birdBodyMats = new Matrix4x4[BirdTotal];
        private readonly Matrix4x4[] _birdWingRMats = new Matrix4x4[BirdTotal];
        private readonly Matrix4x4[] _birdWingLMats = new Matrix4x4[BirdTotal];
        private static readonly Vector3 WingRootR = new Vector3(0.07f, 0.06f, 0.02f);
        private static readonly Vector3 WingRootL = new Vector3(-0.07f, 0.06f, 0.02f);

        // -- butterflies --------------------------------------------------------

        private sealed class Butterfly
        {
            public Vector3 home;
            public float w1, w2, w3, p1, p2, p3, a1, a2;
            public float flapF, flapP;
            public float scale;
            public int color;
            public Vector3 prevPos;
            public float yaw;
            public float vis = 1f;
            public bool placed;
        }

        private readonly List<Butterfly> _butterflies = new List<Butterfly>();
        private readonly Matrix4x4[] _bflyBodyMats = new Matrix4x4[ButterflyCount];
        private readonly Matrix4x4[][] _bflyWingRMats = new Matrix4x4[ButterflyColors][];
        private readonly Matrix4x4[][] _bflyWingLMats = new Matrix4x4[ButterflyColors][];
        private static readonly Vector3 BflyWingRootR = new Vector3(0.015f, 0.008f, 0f);
        private static readonly Vector3 BflyWingRootL = new Vector3(-0.015f, 0.008f, 0f);

        // -- fish ---------------------------------------------------------------

        private sealed class Fish
        {
            public float s;           // position along river path (index units)
            public int dir = 1;
            public float worldSpeed = 2f;
            public float phase;
            public float breachT = -1f; // >=0 while breaching
            public float nextBreach = 20f;
            public bool breachInit;
            public bool splashed;
        }

        private readonly List<Fish> _fish = new List<Fish>();
        private readonly List<V2> _river = new List<V2>();
        private readonly Matrix4x4[] _fishMats = new Matrix4x4[FishCount];
        private bool _fishActive;

        private sealed class Ripple
        {
            public GameObject go;
            public Transform tr;
            public float t;
            public bool active;
        }

        private readonly List<Ripple> _ripples = new List<Ripple>();

        // -- bootstrap ----------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            // Singleton guard: scene reloads (editor play-stop-play, menu
            // returns) must not stack duplicate ambient-life systems.
            if (Object.FindObjectOfType<AmbientLife>() != null) return;
            var go = new GameObject("AmbientLife");
            go.AddComponent<AmbientLife>();
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (!_built || boot.Seed != _builtSeed)
            {
                _builtSeed = boot.Seed;
                Build(boot.Sim.State.World);
                _built = true;
            }
            SyncFromState(boot.Sim.State);
            Animate();
        }

        private void OnDestroy()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null) boot.UnregisterView(this);
        }

        public void SyncFromState(GameState state)
        {
            if (state.Agent != null)
            {
                _foxX = state.Agent.X;
                _foxZ = state.Agent.Z;
                _foxSpeed = state.Agent.Speed;
            }
            _timeOfDay = state.TimeOfDay;
            _isNight = state.IsNight;
            _isRaining = state.Weather == Weather.Rain || state.Weather == Weather.Storm;
        }

        // -- build --------------------------------------------------------------

        private void Build(WorldData world)
        {
            _world = world;
            EnsureMeshes();

            _birdMat = MaterialFactory.Lit(new Color(0.07f, 0.07f, 0.09f), 0.3f);
            _bflyBodyMat = MaterialFactory.Lit(new Color(0.08f, 0.07f, 0.06f), 0.4f);
            _bflyWingMats[0] = MaterialFactory.LitEmissive(
                new Color(0.95f, 0.55f, 0.15f), new Color(0.38f, 0.20f, 0.05f), 0.4f); // amber
            _bflyWingMats[1] = MaterialFactory.LitEmissive(
                new Color(0.25f, 0.55f, 0.95f), new Color(0.08f, 0.20f, 0.38f), 0.4f); // azure
            _bflyWingMats[2] = MaterialFactory.LitEmissive(
                new Color(0.95f, 0.35f, 0.55f), new Color(0.36f, 0.12f, 0.20f), 0.4f); // rose
            _fishMat = MaterialFactory.Lit(new Color(0.10f, 0.14f, 0.16f), 0.4f);
            _rippleMat = MaterialFactory.Unlit(new Color(0.70f, 0.88f, 0.90f));

            var rng = SeededRandom.Derive(world.Seed, "ambient-life");
            BuildBirds(world, rng);
            BuildButterflies(world, rng);
            BuildFish(world, rng);
            BuildRipples();

            GameBootstrap.Instance.RegisterView(this);
        }

        private void BuildBirds(WorldData world, SeededRandom rng)
        {
            _flocks.Clear();
            float half = world.HalfSize;
            for (int fi = 0; fi < FlockCount; fi++)
            {
                var f = new BirdFlock();
                float hx = rng.NextFloat(-half * 0.7f, half * 0.7f);
                float hz = rng.NextFloat(-half * 0.7f, half * 0.7f);
                f.home = new Vector3(hx, world.SampleHeight(hx, hz), hz);
                // Perch: nearby terrain, not water.
                Vector3 perch = f.home;
                for (int tries = 0; tries < 8; tries++)
                {
                    float px = hx + rng.NextFloat(-28f, 28f);
                    float pz = hz + rng.NextFloat(-28f, 28f);
                    if (world.IsWater(px, pz)) continue;
                    perch = new Vector3(px, world.SampleHeight(px, pz) + 0.5f, pz);
                    break;
                }
                f.perch = perch;
                f.circleRadius = rng.NextFloat(18f, 30f);
                f.angSpeed = rng.NextFloat(0.25f, 0.45f) * (rng.NextFloat(0f, 1f) < 0.5f ? 1f : -1f);
                f.cruiseHeight = rng.NextFloat(16f, 26f);
                f.phase = rng.NextFloat(0f, Mathf.PI * 2f);
                f.mode = 0;
                f.modeT = 0f;
                f.modeDur = rng.NextFloat(35f, 70f);
                for (int i = 0; i < BirdsPerFlock; i++)
                {
                    f.orbitR[i] = rng.NextFloat(3f, 8f);
                    f.orbitW[i] = rng.NextFloat(0.5f, 1.1f) * Mathf.Sign(f.angSpeed);
                    f.orbitP[i] = rng.NextFloat(0f, Mathf.PI * 2f);
                    f.bobF[i] = rng.NextFloat(0.8f, 1.6f);
                    f.bobP[i] = rng.NextFloat(0f, Mathf.PI * 2f);
                    f.flapP[i] = rng.NextFloat(0f, Mathf.PI * 2f);
                    f.scale[i] = rng.NextFloat(0.9f, 1.25f);
                    f.yaw[i] = rng.NextFloat(0f, 360f);
                    f.perchYaw[i] = rng.NextFloat(0f, 360f);
                    float pa = (i / (float)BirdsPerFlock) * Mathf.PI * 2f;
                    float pr = 0.6f + (i % 3) * 0.5f;
                    float ppx = perch.x + Mathf.Cos(pa) * pr;
                    float ppz = perch.z + Mathf.Sin(pa) * pr;
                    f.perchPos[i] = new Vector3(ppx, world.SampleHeight(ppx, ppz) + 0.32f, ppz);
                    f.prevPos[i] = f.perchPos[i];
                }
                _flocks.Add(f);
            }
        }

        private void BuildButterflies(WorldData world, SeededRandom rng)
        {
            _butterflies.Clear();
            float half = world.HalfSize;
            int placed = 0;
            for (int tries = 0; tries < 80 && placed < ButterflyCount; tries++)
            {
                float x = rng.NextFloat(-half * 0.8f, half * 0.8f);
                float z = rng.NextFloat(-half * 0.8f, half * 0.8f);
                if (world.IsWater(x, z)) continue;
                var b = new Butterfly();
                b.home = new Vector3(x, world.SampleHeight(x, z), z);
                b.w1 = rng.NextFloat(0.5f, 0.9f);
                b.w2 = rng.NextFloat(0.4f, 0.8f);
                b.w3 = rng.NextFloat(1.1f, 1.9f);
                b.p1 = rng.NextFloat(0f, Mathf.PI * 2f);
                b.p2 = rng.NextFloat(0f, Mathf.PI * 2f);
                b.p3 = rng.NextFloat(0f, Mathf.PI * 2f);
                b.a1 = rng.NextFloat(2f, 3.5f);
                b.a2 = rng.NextFloat(1f, 2f);
                b.flapF = rng.NextFloat(9f, 13f);
                b.flapP = rng.NextFloat(0f, Mathf.PI * 2f);
                b.scale = rng.NextFloat(0.8f, 1.2f);
                b.color = placed % ButterflyColors;
                b.yaw = rng.NextFloat(0f, 360f);
                b.prevPos = b.home;
                _butterflies.Add(b);
                placed++;
            }
            for (int c = 0; c < ButterflyColors; c++)
            {
                _bflyWingRMats[c] = new Matrix4x4[ButterflyCount];
                _bflyWingLMats[c] = new Matrix4x4[ButterflyCount];
            }
        }

        private void BuildFish(WorldData world, SeededRandom rng)
        {
            _fish.Clear();
            _river.Clear();
            _fishActive = world.RiverPath != null && world.RiverPath.Count >= 4;
            if (!_fishActive) return;
            _river.AddRange(world.RiverPath);
            int n = _river.Count;
            for (int i = 0; i < FishCount; i++)
            {
                var f = new Fish();
                f.s = rng.NextFloat(1f, n - 2f);
                f.dir = rng.NextFloat(0f, 1f) < 0.5f ? 1 : -1;
                f.worldSpeed = rng.NextFloat(1.5f, 3f);
                f.phase = rng.NextFloat(0f, Mathf.PI * 2f);
                f.nextBreach = rng.NextFloat(6f, 26f);
                _fish.Add(f);
            }
        }

        private void BuildRipples()
        {
            foreach (var r in _ripples)
                if (r.go != null) Object.Destroy(r.go);
            _ripples.Clear();
            for (int i = 0; i < RipplePoolSize; i++)
            {
                var go = new GameObject("Ripple");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _rippleMesh;
                var rend = go.AddComponent<MeshRenderer>();
                rend.sharedMaterial = _rippleMat;
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
                go.SetActive(false);
                _ripples.Add(new Ripple { go = go, tr = go.transform, t = 0f, active = false });
            }
        }

        // -- animation ----------------------------------------------------------

        private void Animate()
        {
            if (_world == null) return;
            float dt = Time.deltaTime;
            float t = Time.time;
            AnimateBirds(dt, t);
            AnimateButterflies(dt, t);
            AnimateFish(dt, t);
            AnimateRipples(dt);
            DrawAll();
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>Dawn/dusk chorus: birds fly faster and livelier near sunrise/sunset.</summary>
        private float ChorusFactor()
        {
            float d = Mathf.Min(Mathf.Abs(_timeOfDay - 7f), Mathf.Abs(_timeOfDay - 18f));
            return d < 2.5f ? 1.35f : 1f;
        }

        private void AnimateBirds(float dt, float t)
        {
            float chorus = ChorusFactor();
            int bi = 0, wi = 0;
            for (int fi = 0; fi < _flocks.Count; fi++)
            {
                BirdFlock f = _flocks[fi];

                // Mode machine. Night grounds every flock.
                if (_isNight)
                {
                    f.mode = 2; f.modeT = 0f;
                }
                else
                {
                    f.modeT += dt;
                    if (f.modeT >= f.modeDur)
                    {
                        f.mode = (f.mode + 1) % 4;
                        f.modeT = 0f;
                        f.modeDur = f.mode == 0 ? Random.Range(35f, 70f)
                                  : f.mode == 1 ? 6f
                                  : f.mode == 2 ? Random.Range(18f, 40f) : 4f;
                    }
                }
                float g = f.mode == 0 ? 0f
                        : f.mode == 2 ? 1f
                        : f.mode == 1 ? Smooth01(f.modeT / f.modeDur)
                        : 1f - Smooth01(f.modeT / f.modeDur);

                f.phase += dt * f.angSpeed * chorus * (1f - g * 0.9f);
                Vector3 fly = new Vector3(
                    f.home.x + Mathf.Cos(f.phase) * f.circleRadius,
                    f.home.y + f.cruiseHeight + Mathf.Sin(t * 0.23f + f.phase) * 2f,
                    f.home.z + Mathf.Sin(f.phase) * f.circleRadius * 0.8f);
                Vector3 center = Vector3.Lerp(fly, f.perch, g);

                for (int i = 0; i < BirdsPerFlock; i++, bi++)
                {
                    float oa = t * f.orbitW[i] * chorus + f.orbitP[i];
                    float spread = 1f - g * 0.9f;
                    Vector3 p = center;
                    p.x += Mathf.Cos(oa) * f.orbitR[i] * spread;
                    p.z += Mathf.Sin(oa) * f.orbitR[i] * spread;
                    p.y += Mathf.Sin(t * f.bobF[i] + f.bobP[i]) * 1.1f * (1f - g);
                    p = Vector3.Lerp(p, f.perchPos[i], g);

                    // Fox startle: scatter when the fox runs close, flush perched birds.
                    bool flying = g < 0.5f;
                    float dx = p.x - _foxX, dz = p.z - _foxZ;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < 144f)
                    {
                        float d = Mathf.Sqrt(d2) + 0.001f;
                        bool startled = d < 6f || (_foxSpeed > 2.5f && d < 12f);
                        if (startled)
                        {
                            if (!flying && (f.mode == 1 || f.mode == 2) && !_isNight)
                            {
                                f.mode = 3; f.modeT = 0f; f.modeDur = 4f;
                            }
                            else if (flying)
                            {
                                float push = (12f - d) * 2.2f;
                                p.x += dx / d * push;
                                p.z += dz / d * push;
                                p.y += push * 0.55f;
                            }
                        }
                    }

                    // Face travel direction, bank into turns.
                    Vector3 prev = f.prevPos[i];
                    float mx = p.x - prev.x, mz = p.z - prev.z;
                    float targetYaw = f.yaw[i];
                    if (mx * mx + mz * mz > 0.000001f)
                        targetYaw = Mathf.Atan2(mx, mz) * Mathf.Rad2Deg;
                    else if (g > 0.5f)
                        targetYaw = f.perchYaw[i];
                    float before = f.yaw[i];
                    float dyaw = Mathf.DeltaAngle(before, targetYaw);
                    float yaw = before + dyaw * Mathf.Min(1f, 8f * dt);
                    f.yaw[i] = yaw;
                    f.prevPos[i] = p;
                    float bank = Mathf.Clamp(-dyaw * 2.5f, -28f, 28f) * (1f - g);

                    // Flap-flap-glide; folded when perched.
                    float glide = 0.5f + 0.5f * Mathf.Sin(t * 0.6f + f.flapP[i]);
                    float amp = (1f - g) * (0.35f + 0.65f * glide);
                    float flap = Mathf.Sin(t * 10.5f + f.flapP[i] * 7f) * 0.9f * amp
                               + g * 1.25f + 0.06f;

                    Quaternion q = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, bank);
                    Matrix4x4 body = Matrix4x4.TRS(p, q, Vector3.one * f.scale[i]);
                    _birdBodyMats[bi] = body;
                    _birdWingRMats[bi] = body * Matrix4x4.TRS(WingRootR,
                        Quaternion.Euler(0f, 0f, -flap * 57.29578f), Vector3.one);
                    _birdWingLMats[bi] = body * Matrix4x4.TRS(WingRootL,
                        Quaternion.Euler(0f, 0f, flap * 57.29578f), Vector3.one);
                    wi++;
                }
            }
        }

        private void AnimateButterflies(float dt, float t)
        {
            int[] wingCount = _bflyWingScratch;
            wingCount[0] = 0; wingCount[1] = 0; wingCount[2] = 0;
            bool show = !_isNight && !_isRaining;
            for (int i = 0; i < _butterflies.Count; i++)
            {
                Butterfly b = _butterflies[i];
                b.vis = Mathf.MoveTowards(b.vis, show ? 1f : 0f, dt * 1.5f);
                if (b.vis < 0.01f)
                {
                    _bflyBodyMats[i] = Matrix4x4.TRS(b.home, Quaternion.identity, Vector3.zero);
                    continue;
                }

                Vector3 p;
                p.x = b.home.x + Mathf.Sin(t * b.w1 + b.p1) * b.a1
                              + Mathf.Sin(t * b.w2 * 0.37f + b.p2) * b.a2 * 0.5f;
                p.z = b.home.z + Mathf.Sin(t * b.w2 + b.p2) * b.a1
                              + Mathf.Sin(t * b.w1 * 0.43f + b.p3) * b.a2 * 0.5f;
                p.y = _world.SampleHeight(p.x, p.z) + 0.7f + Mathf.Sin(t * b.w3 + b.p3) * 0.35f;

                // Drift from the fox.
                float dx = p.x - _foxX, dz = p.z - _foxZ;
                float d2 = dx * dx + dz * dz;
                if (d2 < 25f && d2 > 0.0001f)
                {
                    float d = Mathf.Sqrt(d2);
                    float push = (5f - d) * 1.4f;
                    p.x += dx / d * push;
                    p.z += dz / d * push;
                }

                Vector3 prev = b.prevPos;
                float mx = p.x - prev.x, mz = p.z - prev.z;
                if (mx * mx + mz * mz > 0.000001f)
                {
                    float targetYaw = Mathf.Atan2(mx, mz) * Mathf.Rad2Deg;
                    b.yaw += Mathf.DeltaAngle(b.yaw, targetYaw) * Mathf.Min(1f, 6f * dt);
                }
                b.prevPos = p;

                float glide = 0.5f + 0.5f * Mathf.Sin(t * 0.9f + b.flapP);
                float flap = Mathf.Sin(t * b.flapF + b.flapP * 9f) * 1.0f * (0.4f + 0.6f * glide) + 0.12f;
                float s = b.scale * b.vis;

                Quaternion q = Quaternion.Euler(0f, b.yaw, 0f);
                Matrix4x4 body = Matrix4x4.TRS(p, q, new Vector3(0.035f * s, 0.035f * s, 0.10f * s));
                _bflyBodyMats[i] = body;
                Matrix4x4 wingBase = Matrix4x4.TRS(p, q, Vector3.one * s);
                int c = b.color;
                int k = wingCount[c]++;
                _bflyWingRMats[c][k] = wingBase * Matrix4x4.TRS(BflyWingRootR,
                    Quaternion.Euler(0f, 0f, -flap * 57.29578f), Vector3.one);
                _bflyWingLMats[c][k] = wingBase * Matrix4x4.TRS(BflyWingRootL,
                    Quaternion.Euler(0f, 0f, flap * 57.29578f), Vector3.one);
            }
            _bflyWingCounts[0] = wingCount[0];
            _bflyWingCounts[1] = wingCount[1];
            _bflyWingCounts[2] = wingCount[2];
        }

        private readonly int[] _bflyWingCounts = new int[ButterflyColors];
        private readonly int[] _bflyWingScratch = new int[ButterflyColors];

        private void AnimateFish(float dt, float t)
        {
            if (!_fishActive) return;
            int n = _river.Count;
            for (int i = 0; i < _fish.Count; i++)
            {
                Fish f = _fish[i];
                if (!f.breachInit) { f.breachInit = true; f.nextBreach += t; }
                int i0 = Mathf.Clamp((int)f.s, 0, n - 2);
                float fr = Mathf.Clamp01(f.s - i0);
                V2 a = _river[i0], b2 = _river[i0 + 1];
                float segDx = b2.X - a.X, segDz = b2.Z - a.Z;
                float segLen = Mathf.Sqrt(segDx * segDx + segDz * segDz);
                if (segLen < 0.001f) segLen = 0.001f;
                f.s += f.dir * (f.worldSpeed * dt) / segLen;
                if (f.s >= n - 2) { f.s = n - 2; f.dir = -1; }
                else if (f.s <= 0f) { f.s = 0f; f.dir = 1; }

                float x = a.X + segDx * fr + Mathf.Sin(t * 0.9f + f.phase) * 1.2f;
                float z = a.Z + segDz * fr + Mathf.Cos(t * 0.7f + f.phase) * 1.2f;
                float y = WorldData.WaterLevel + 0.10f;

                if (f.breachT >= 0f)
                {
                    f.breachT += dt;
                    float k = f.breachT / 0.8f;
                    if (k >= 1f)
                    {
                        f.breachT = -1f;
                        f.nextBreach = t + Random.Range(14f, 30f);
                    }
                    else
                    {
                        y += Mathf.Sin(k * Mathf.PI) * 1.25f;
                        if (!f.splashed && k > 0.45f)
                        {
                            f.splashed = true;
                            SpawnRipple(x, z);
                        }
                    }
                }
                else if (t > f.nextBreach)
                {
                    f.breachT = 0f;
                    f.splashed = false;
                }

                float yaw = Mathf.Atan2(segDx * f.dir, segDz * f.dir) * Mathf.Rad2Deg
                          + Mathf.Sin(t * 3.5f + f.phase) * 10f;
                _fishMats[i] = Matrix4x4.TRS(new Vector3(x, y, z),
                    Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            }
        }

        private void SpawnRipple(float x, float z)
        {
            for (int i = 0; i < _ripples.Count; i++)
            {
                Ripple r = _ripples[i];
                if (r.active) continue;
                r.active = true;
                r.t = 0f;
                r.tr.position = new Vector3(x, WorldData.WaterLevel + 0.16f, z);
                r.tr.localScale = Vector3.one * 0.5f;
                r.go.SetActive(true);
                return;
            }
        }

        private void AnimateRipples(float dt)
        {
            for (int i = 0; i < _ripples.Count; i++)
            {
                Ripple r = _ripples[i];
                if (!r.active) continue;
                r.t += dt;
                float k = r.t / 0.9f;
                if (k >= 1f)
                {
                    r.active = false;
                    r.go.SetActive(false);
                }
                else
                {
                    float s = 0.5f + k * 2f;
                    r.tr.localScale = new Vector3(s, 1f, s);
                }
            }
        }

        private void DrawAll()
        {
            MaterialFactory.DrawInstanced(_birdBodyMesh, _birdMat,
                _birdBodyMats, BirdTotal, ShadowCastingMode.Off, false);
            MaterialFactory.DrawInstanced(_birdWingRMesh, _birdMat,
                _birdWingRMats, BirdTotal, ShadowCastingMode.Off, false);
            MaterialFactory.DrawInstanced(_birdWingLMesh, _birdMat,
                _birdWingLMats, BirdTotal, ShadowCastingMode.Off, false);

            if (_butterflies.Count > 0)
                MaterialFactory.DrawInstanced(_bflyBodyMesh, _bflyBodyMat,
                    _bflyBodyMats, _butterflies.Count, ShadowCastingMode.Off, false);
            for (int c = 0; c < ButterflyColors; c++)
            {
                int k = _bflyWingCounts[c];
                if (k == 0) continue;
                MaterialFactory.DrawInstanced(_bflyWingRMesh, _bflyWingMats[c],
                    _bflyWingRMats[c], k, ShadowCastingMode.Off, false);
                MaterialFactory.DrawInstanced(_bflyWingLMesh, _bflyWingMats[c],
                    _bflyWingLMats[c], k, ShadowCastingMode.Off, false);
            }

            if (_fishActive)
                MaterialFactory.DrawInstanced(_fishMesh, _fishMat,
                    _fishMats, _fish.Count, ShadowCastingMode.Off, false);
        }

        // -- mesh builders ------------------------------------------------------

        private static void EnsureMeshes()
        {
            if (_meshesBuilt) return;
            _meshesBuilt = true;

            // Bird body: elongated dart — stretched sphere, head, tail cone.
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            _birdBodyMesh = MeshFactory.Merge(
                new List<Mesh>
                {
                    sphere, sphere,
                    MeshFactory.Cone(0.09f, 0.35f, 4),
                },
                new List<Matrix4x4>
                {
                    Matrix4x4.TRS(new Vector3(0f, 0f, 0.05f), Quaternion.identity,
                        new Vector3(0.16f, 0.14f, 0.42f)),
                    Matrix4x4.TRS(new Vector3(0f, 0.06f, 0.38f), Quaternion.identity,
                        Vector3.one * 0.13f),
                    Matrix4x4.TRS(new Vector3(0f, 0.02f, -0.40f),
                        Quaternion.Euler(-90f, 0f, 0f), Vector3.one),
                });

            _birdWingRMesh = BuildWingMesh(0.60f, false);
            _birdWingLMesh = BuildWingMesh(0.60f, true);

            // Butterfly: pinhead body; broad colorful wings.
            _bflyBodyMesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            _bflyWingRMesh = BuildWingMesh(0.22f, false);
            _bflyWingLMesh = BuildWingMesh(0.22f, true);

            // Fish: sleek body + vertical tail fin.
            _fishMesh = MeshFactory.Merge(
                new List<Mesh> { sphere, BuildTailFin() },
                new List<Matrix4x4>
                {
                    Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                        new Vector3(0.20f, 0.15f, 0.55f)),
                    Matrix4x4.TRS(new Vector3(0f, 0.02f, -0.55f), Quaternion.identity, Vector3.one),
                });

            _rippleMesh = BuildRingMesh(0.72f, 1f, 12);
        }

        /// <summary>Tapered wing in the XZ plane, root at origin extending +X
        /// (mirrored for left). Double-sided triangles — winding-proof.</summary>
        private static Mesh BuildWingMesh(float span, bool mirror)
        {
            float sx = mirror ? -1f : 1f;
            var v0 = new Vector3(0f, 0f, 0.26f * span);
            var v1 = new Vector3(0f, 0f, -0.21f * span);
            var v2 = new Vector3(span * sx, 0f, 0.05f * span);
            var v3 = new Vector3(span * sx, 0f, -0.15f * span);
            var mesh = new Mesh();
            mesh.SetVertices(new List<Vector3> { v0, v1, v2, v3 });
            mesh.SetTriangles(new List<int>
            {
                0, 2, 1, 1, 2, 3,   // +Y
                0, 1, 2, 1, 3, 2,   // -Y (double-sided)
            }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Vertical tail fin in the YZ plane. Double-sided.</summary>
        private static Mesh BuildTailFin()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.20f, -0.30f),
                new Vector3(0f, -0.20f, -0.30f),
            });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 1 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flat annulus in the XZ plane — a ripple ring. Double-sided.</summary>
        private static Mesh BuildRingMesh(float inner, float outer, int segments)
        {
            var verts = new List<Vector3>(segments * 4);
            var tris = new List<int>(segments * 12);
            for (int s = 0; s < segments; s++)
            {
                float a0 = (s / (float)segments) * Mathf.PI * 2f;
                float a1 = ((s + 1) / (float)segments) * Mathf.PI * 2f;
                var o0 = new Vector3(Mathf.Cos(a0) * outer, 0f, Mathf.Sin(a0) * outer);
                var o1 = new Vector3(Mathf.Cos(a1) * outer, 0f, Mathf.Sin(a1) * outer);
                var i0 = new Vector3(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner);
                var i1 = new Vector3(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner);
                int b = verts.Count;
                verts.Add(i0); verts.Add(o0); verts.Add(o1); verts.Add(i1);
                // Both windings — double-sided.
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
