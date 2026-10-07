// Solace.Unity — time of day, weather and atmosphere from sim state.
//
// Sun + moon + fill directionals light the world (URP Lit). The SKY itself
// is fully procedural and fog-free (custom Solace/SkyUnlit + Solace/SkyLambert
// shaders — scene fog would wash out anything past ~300m):
//   - 8-band gradient sky dome (horizon glow -> zenith), per-frame colors
//   - sun & moon discs with warm/cool halos (spheres: no billboarding needed)
//   - 240 bright stars + 200-star milky-way band, merged into 2 draw calls
//   - 40 faceted low-poly clouds, instanced, drifting on the wind, lit by a
//     single animated sun/moon direction in the custom lambert shader
//   - storm lightning: randomized flashes that spike ambient, fog and sky
// Fireflies and rain are mesh-particle systems (opaque — no transparency
// anywhere). All particle emission rates are driven by IsNight / Weather.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class DayNightCycle : MonoBehaviour, ISimView
    {
        private Light _sun;
        private Light _moon;
        private Light _fill; // cool fill from the opposite azimuth: models the facets
        private Camera _cam;

        // -- procedural sky ----------------------------------------------------
        private GameObject _skyRoot;
        private Material[] _bandMats;
        private const int BandCount = 8;
        private const float SkyRadius = 1200f;

        private GameObject _sunCoreGO, _sunHaloGO, _moonCoreGO, _moonHaloGO;
        private Material _sunCoreMat, _sunHaloMat, _moonCoreMat, _moonHaloMat;

        // -- stars --------------------------------------------------------------
        private GameObject _starRoot;
        private Material _starMat, _milkyMat;

        // -- meteors (easter egg: rare night shower) --------------------------------
        private Mesh _meteorMesh;
        private Material _meteorMat;
        private const int MeteorCount = 14;
        private struct Meteor
        {
            public Vector3 Pos;
            public Vector3 Vel;
            public float Life;   // remaining seconds
            public float MaxLife;
            public float Len;
        }
        private Meteor[] _meteors;
        private float _meteorTimer;

        // -- clouds --------------------------------------------------------------
        private Mesh[] _cloudMeshes;
        private Material _cloudMat;
        private Matrix4x4[][] _cloudMats; // per variant scratch arrays
        private int[] _cloudCounts;
        private struct Cloud
        {
            public Vector3 Base;
            public float Scale;
            public float Speed;
            public float Yaw;
            public int Variant;
        }
        private Cloud[] _clouds;
        private const int CloudCount = 40;
        private float _cloudTime;
        private static readonly Vector3 WindDir = new Vector3(0.83f, 0f, 0.55f).normalized;
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int AmbColorId = Shader.PropertyToID("_AmbColor");

        // -- lightning ------------------------------------------------------------
        // Static event so AmbientAudio can schedule delayed thunder after each strike.
        public static event System.Action OnLightningStrike;
        private float _flash;
        private float _nextBolt = 5f;

        private ParticleSystem _fireflies;
        private ParticleSystem _rain;
        private GameObject _rainGO;

        // Sky palettes: (zenith, horizon) per mood.
        private static readonly Color DayZen = new Color(0.19f, 0.40f, 0.74f);
        private static readonly Color DayHor = new Color(0.68f, 0.79f, 0.88f);
        private static readonly Color DuskZen = new Color(0.13f, 0.15f, 0.34f);
        private static readonly Color DuskHor = new Color(1.00f, 0.44f, 0.18f);
        private static readonly Color NightZen = new Color(0.006f, 0.010f, 0.030f);
        private static readonly Color NightHor = new Color(0.045f, 0.070f, 0.140f);
        private static readonly Color StormZen = new Color(0.16f, 0.18f, 0.23f);
        private static readonly Color StormHor = new Color(0.38f, 0.41f, 0.47f);

        private static readonly Color FogDay = new Color(0.60f, 0.67f, 0.74f);
        private static readonly Color FogNight = new Color(0.030f, 0.045f, 0.095f);
        private static readonly Color FogDusk = new Color(0.95f, 0.62f, 0.38f); // golden-hour glow on the mist
        private static readonly Color AmbDay = new Color(0.46f, 0.51f, 0.57f);
        private static readonly Color AmbNight = new Color(0.065f, 0.095f, 0.155f);

        public void Build(WorldData world)
        {
            GameBootstrap.Instance.RegisterView(this);
            _cam = GameBootstrap.Instance.Camera.GetComponent<Camera>();

            var sunGO = new GameObject("Sun");
            sunGO.transform.SetParent(transform, false);
            _sun = sunGO.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.None;

            var moonGO = new GameObject("Moon");
            moonGO.transform.SetParent(transform, false);
            _moon = moonGO.AddComponent<Light>();
            _moon.type = LightType.Directional;
            _moon.color = new Color(0.55f, 0.65f, 0.90f);
            _moon.shadows = LightShadows.None;

            // Cool fill: no shadows, low intensity — its only job is to keep
            // shadow-side facets readable and give the poly-art depth.
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(transform, false);
            _fill = fillGO.AddComponent<Light>();
            _fill.type = LightType.Directional;
            _fill.color = new Color(0.50f, 0.60f, 0.78f);
            _fill.shadows = LightShadows.None;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            BuildSky(world);
            BuildStars(world);
            BuildClouds(world);
            BuildMeteors();
            BuildFireflies(world);
            BuildRain();
        }

        public void SyncFromState(GameState state)
        {
            float t = state.TimeOfDay; // 0..24
            bool night = state.IsNight;
            bool storm = state.Weather == Weather.Storm;
            float dt = Time.deltaTime;

            // Sun path: rises 6h, sets 18h.
            float sunT = (t - 6f) / 12f; // 0..1 across the day
            float elev = Mathf.Sin(Mathf.Clamp01(sunT) * Mathf.PI);
            float sunI = night ? 0f : Mathf.Clamp01(elev) * 1.15f;
            _sun.intensity = sunI;
            float azim = Mathf.Lerp(-90f, 270f, Mathf.Clamp01(sunT));
            _sun.transform.rotation = Quaternion.Euler(90f - Mathf.Clamp01(elev) * 75f, azim, 0f);
            // Warm at the edges of the day — golden hour lingers (elev*1.15
            // keeps warmth alive well past sunrise/sunset).
            float warmth = 1f - Mathf.Clamp01(elev * 1.15f);
            _sun.color = Color.Lerp(new Color(1f, 0.94f, 0.86f), new Color(1f, 0.50f, 0.26f), night ? 0f : warmth);

            _moon.intensity = night ? 0.38f : 0f;
            _moon.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

            // Fill opposes the sun so facets get a cool rim from behind.
            _fill.intensity = night ? 0.05f : 0.22f;
            _fill.transform.rotation = Quaternion.Euler(40f, azim + 180f, 0f);

            // Dusk factor: peaks near sunrise/sunset, lingers with golden hour.
            float edge = night ? 0f : Mathf.Clamp01(1f - elev * 1.8f);

            // -- sky palette -------------------------------------------------------
            Color zen, hor;
            if (night) { zen = NightZen; hor = NightHor; }
            else { zen = Color.Lerp(DayZen, DuskZen, edge); hor = Color.Lerp(DayHor, DuskHor, edge); }
            if (storm) { zen = Color.Lerp(zen, StormZen, 0.75f); hor = Color.Lerp(hor, StormHor, 0.75f); }

            // Lightning: randomize strike timing, decay the flash envelope.
            if (storm)
            {
                _nextBolt -= dt;
                if (_nextBolt <= 0f)
                {
                    _flash = 1f;
                    _nextBolt = UnityEngine.Random.Range(3f, 11f);
                    var strike = OnLightningStrike;
                    if (strike != null) strike();
                }
            }
            else { _flash = 0f; _nextBolt = 5f; }
            _flash = Mathf.Max(0f, _flash - dt * 2.2f);
            float flicker = _flash * (0.7f + 0.3f * Mathf.Abs(Mathf.Sin(Time.time * 43f)));

            // -- sky dome bands ------------------------------------------------------
            for (int b = 0; b < BandCount; b++)
            {
                float bandT = (b + 0.5f) / BandCount; // 0 bottom -> 1 zenith
                Color c = Color.Lerp(hor, zen, Mathf.Pow(bandT, 0.8f));
                if (b == 0) c = hor * 0.35f; // below-horizon ground haze
                if (flicker > 0f) c += new Color(0.55f, 0.60f, 0.75f) * flicker * 0.55f;
                if (_bandMats != null) _bandMats[b].color = c;
            }
            _cam.backgroundColor = hor; // fallback behind the dome
            _cam.clearFlags = CameraClearFlags.SolidColor;

            // -- sun & moon discs ------------------------------------------------------
            Vector3 cp = _cam.transform.position;
            _skyRoot.transform.position = cp;
            _starRoot.transform.position = cp;

            float sa = Mathf.PI * Mathf.Clamp01(sunT); // 0=east horizon .. pi=west horizon
            Vector3 sunDir = new Vector3(Mathf.Cos(sa), Mathf.Max(0.02f, Mathf.Sin(sa) * 1.15f), 0.28f).normalized;
            Vector3 moonDir = new Vector3(-0.45f, 0.62f, -0.64f).normalized;

            _sunCoreGO.SetActive(!night);
            _sunHaloGO.SetActive(!night);
            _moonCoreGO.SetActive(night);
            _moonHaloGO.SetActive(night);
            if (!night)
            {
                if (_sunCoreGO != null) _sunCoreGO.transform.localPosition = sunDir * (SkyRadius - 40f);
                if (_sunHaloGO != null) _sunHaloGO.transform.localPosition = sunDir * (SkyRadius - 30f);
                Color core = Color.Lerp(new Color(1f, 0.97f, 0.90f), new Color(1f, 0.42f, 0.14f), warmth);
                if (_sunCoreMat != null) _sunCoreMat.color = core;
                if (_sunHaloMat != null) _sunHaloMat.color = Color.Lerp(hor, core, 0.38f);
            }
            if (night)
            {
                if (_moonCoreGO != null) _moonCoreGO.transform.localPosition = moonDir * (SkyRadius - 40f);
                if (_moonHaloGO != null) _moonHaloGO.transform.localPosition = moonDir * (SkyRadius - 30f);
                if (_moonCoreMat != null)
                {
                    if (flicker > 0f)
                        _moonCoreMat.color = new Color(0.92f, 0.95f, 1f) + new Color(0.5f, 0.55f, 0.7f) * flicker;
                    else _moonCoreMat.color = new Color(0.92f, 0.95f, 1f);
                }
            }

            // -- stars ------------------------------------------------------------------
            if (_starRoot != null) _starRoot.gameObject.SetActive(night);

            // -- fog / ambient ------------------------------------------------------------
            // Golden-hour glow: the mist itself goes warm at dusk.
            Color fogC = night ? FogNight : Color.Lerp(FogDay, FogDusk, edge * 0.85f);
            if (storm) fogC = Color.Lerp(fogC, new Color(0.35f, 0.38f, 0.45f), 0.7f);
            if (flicker > 0f) fogC = Color.Lerp(fogC, new Color(0.72f, 0.78f, 0.95f), flicker * 0.55f);
            RenderSettings.fogColor = fogC;
            // Fog is the cheapest depth cue we have — lean into it.
            float density = night ? 0.0042f : 0.0026f;
            density += edge * 0.0016f; // dawn/dusk mist
            if (state.Weather == Weather.Rain) density += 0.0022f;
            if (storm) density += 0.0045f;
            if (state.Weather == Weather.Cloudy) density += 0.0008f;
            RenderSettings.fogDensity = density;

            Color amb = night ? AmbNight : Color.Lerp(AmbDay, new Color(0.52f, 0.40f, 0.32f), edge * 0.65f);
            if (storm) amb = Color.Lerp(amb, new Color(0.20f, 0.22f, 0.28f), 0.6f);
            if (flicker > 0f) amb += new Color(0.65f, 0.70f, 0.88f) * flicker * 1.1f;
            RenderSettings.ambientLight = amb;

            // -- clouds ---------------------------------------------------------------------
            UpdateClouds(state, cp, dt, edge, night, storm, flicker, sunDir, moonDir);

            // -- meteors (easter egg: rare night shower) ------------------------------------
            if (night) UpdateMeteors(state, cp, dt);

            // -- particles ---------------------------------------------------------------------
            SetEmission(_fireflies, night ? 26f : (edge > 0.55f ? 10f : 0f)); // fireflies wake at dusk
            float rainRate = storm ? 900f : state.Weather == Weather.Rain ? 380f : 0f;
            SetEmission(_rain, rainRate);
            if (rainRate > 0f)
                _rainGO.transform.position = new Vector3(cp.x, cp.y + 12f, cp.z);
        }

        // -- sky dome ---------------------------------------------------------------

        private void BuildSky(WorldData world)
        {
            _skyRoot = new GameObject("SkyRoot");
            _skyRoot.transform.SetParent(transform, false);

            var skyShader = Shader.Find("Solace/SkyUnlit");
            if (skyShader == null)
            {
                Debug.LogWarning("[Solace] Solace/SkyUnlit shader not found — procedural sky disabled. " +
                                 "Ensure Assets/Shaders/ is in the project.");
                return;
            }
            // 8 bands from just below the horizon up to the zenith.
            float[] lats = { -10f, 0f, 10f, 22f, 36f, 52f, 68f, 84f, 90f };
            const int segs = 24;
            var mesh = new Mesh();
            var verts = new List<Vector3>();
            var subTris = new List<int>[BandCount];
            for (int b = 0; b < BandCount; b++) subTris[b] = new List<int>();

            for (int r = 0; r < lats.Length; r++)
            {
                float lat = lats[r] * Mathf.Deg2Rad;
                float rr = SkyRadius * Mathf.Cos(lat);
                float y = SkyRadius * Mathf.Sin(lat);
                for (int s = 0; s <= segs; s++)
                {
                    float lon = (s / (float)segs) * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(lon) * rr, y, Mathf.Sin(lon) * rr));
                }
            }
            for (int b = 0; b < BandCount; b++)
            {
                int r0 = b * (segs + 1), r1 = (b + 1) * (segs + 1);
                var tris = subTris[b];
                for (int s = 0; s < segs; s++)
                {
                    // Cull is off in the shader; winding only needs consistency.
                    tris.Add(r0 + s); tris.Add(r1 + s); tris.Add(r1 + s + 1);
                    tris.Add(r0 + s); tris.Add(r1 + s + 1); tris.Add(r0 + s + 1);
                }
            }
            mesh.SetVertices(verts);
            mesh.subMeshCount = BandCount;
            for (int b = 0; b < BandCount; b++) mesh.SetTriangles(subTris[b], b);
            mesh.RecalculateBounds();

            var mf = _skyRoot.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = _skyRoot.AddComponent<MeshRenderer>();
            _bandMats = new Material[BandCount];
            for (int b = 0; b < BandCount; b++)
            {
                var m = new Material(skyShader);
                m.enableInstancing = true;
                _bandMats[b] = m;
            }
            mr.sharedMaterials = _bandMats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Sun / moon: spheres read as discs at distance; no billboarding needed.
            // (Children of _skyRoot, so positions are LOCAL: _skyRoot sits on the camera.)
            _sunCoreMat = new Material(skyShader);
            _sunHaloMat = new Material(skyShader);
            _moonCoreMat = new Material(skyShader);
            _moonHaloMat = new Material(skyShader);
            _moonHaloMat.color = new Color(0.30f, 0.38f, 0.60f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            _sunCoreGO = AddCelestial("SunDisc", sphere, _sunCoreMat, 26f);
            _sunHaloGO = AddCelestial("SunHalo", sphere, _sunHaloMat, 95f);
            _moonCoreGO = AddCelestial("MoonDisc", sphere, _moonCoreMat, 20f);
            _moonHaloGO = AddCelestial("MoonHalo", sphere, _moonHaloMat, 62f);
        }

        private GameObject AddCelestial(string name, Mesh mesh, Material mat, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_skyRoot.transform, false);
            go.transform.localScale = Vector3.one * radius;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        // -- stars --------------------------------------------------------------------

        private void BuildStars(WorldData world)
        {
            var skyShader = Shader.Find("Solace/SkyUnlit");
            if (skyShader == null)
            {
                Debug.LogWarning("[Solace] Solace/SkyUnlit shader not found — stars disabled. " +
                                 "Ensure Assets/Shaders/ is in the project.");
                return;
            }
            _starRoot = new GameObject("StarRoot");
            _starRoot.transform.SetParent(transform, false);

            var rng = SeededRandom.Derive(world.Seed, "unity-stars-v2");
            var rock = MeshFactory.FacetedRock(); // tiny crystal stars

            // Bright field stars.
            var meshes = new List<Mesh>();
            var xforms = new List<Matrix4x4>();
            for (int i = 0; i < 240; i++)
            {
                Vector3 dir = RandomDomeDir(rng, 0.09f, 1.35f);
                float s = rng.NextFloat(1.1f, 2.8f);
                meshes.Add(rock);
                xforms.Add(Matrix4x4.TRS(dir * 1100f, Quaternion.identity, Vector3.one * s));
            }
            Mesh field = MeshFactory.Merge(meshes, xforms);
            _starMat = new Material(skyShader);
            _starMat.color = new Color(0.95f, 0.96f, 1f);
            AddStarMesh("Stars", field, _starMat);

            // Milky way: dimmer, bluer, clustered along a tilted great circle.
            meshes.Clear(); xforms.Clear();
            Vector3 bandN = new Vector3(0.35f, 1f, 0.2f).normalized;
            int placed = 0, guard = 0;
            while (placed < 200 && guard++ < 4000)
            {
                Vector3 dir = RandomDomeDir(rng, 0.12f, 1.25f);
                float w = 1f - Mathf.Abs(Vector3.Dot(dir, bandN));
                if (rng.NextFloat() < w * w)
                {
                    float s = rng.NextFloat(0.7f, 1.5f);
                    meshes.Add(rock);
                    xforms.Add(Matrix4x4.TRS(dir * 1080f, Quaternion.identity, Vector3.one * s));
                    placed++;
                }
            }
            Mesh milky = MeshFactory.Merge(meshes, xforms);
            _milkyMat = new Material(skyShader);
            _milkyMat.color = new Color(0.62f, 0.70f, 0.95f);
            AddStarMesh("MilkyWay", milky, _milkyMat);
        }

        // -- meteors (easter egg) ------------------------------------------------------

        private void BuildMeteors()
        {
            _meteorMesh = MeshFactory.Streak();
            // Bright white-gold emissive: meteors burn.
            _meteorMat = MaterialFactory.LitEmissive(
                new Color(1f, 0.98f, 0.92f), new Color(1f, 0.9f, 0.7f) * 2f, 0.2f);
            _meteors = new Meteor[MeteorCount];
            for (int i = 0; i < MeteorCount; i++) _meteors[i].Life = 0f;
            _meteorTimer = 0f;
        }

        private void UpdateMeteors(GameState state, Vector3 camPos, float dt)
        {
            bool active = state.Eggs.MeteorUntil > state.ElapsedSeconds;
            if (!active)
            {
                // Let existing streaks finish, but spawn nothing new.
                _meteorTimer = 0f;
            }
            else
            {
                _meteorTimer -= dt;
                if (_meteorTimer <= 0f)
                {
                    _meteorTimer = UnityEngine.Random.Range(0.15f, 0.7f);
                    // Spawn a meteor: high in the sky dome near the camera, streaking down.
                    for (int i = 0; i < MeteorCount; i++)
                    {
                        if (_meteors[i].Life > 0f) continue;
                        float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                        float e = UnityEngine.Random.Range(0.35f, 1.1f);
                        Vector3 dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e),
                                                  Mathf.Sin(a) * Mathf.Cos(e));
                        Vector3 start = camPos + dir * 900f;
                        Vector3 vel = new Vector3(
                            UnityEngine.Random.Range(-1f, 1f),
                            UnityEngine.Random.Range(-1.6f, -0.9f),
                            UnityEngine.Random.Range(-1f, 1f)).normalized
                            * UnityEngine.Random.Range(280f, 420f);
                        _meteors[i].Pos = start;
                        _meteors[i].Vel = vel;
                        _meteors[i].MaxLife = UnityEngine.Random.Range(0.7f, 1.4f);
                        _meteors[i].Life = _meteors[i].MaxLife;
                        _meteors[i].Len = UnityEngine.Random.Range(18f, 34f);
                        break;
                    }
                }
            }

            // Advance + draw live meteors (fog-free: they're in the sky).
            for (int i = 0; i < MeteorCount; i++)
            {
                if (_meteors[i].Life <= 0f) continue;
                _meteors[i].Life -= dt;
                if (_meteors[i].Life <= 0f) continue;
                _meteors[i].Pos += _meteors[i].Vel * dt;
                float fade = Mathf.Clamp01(_meteors[i].Life / _meteors[i].MaxLife);
                Vector3 dir = _meteors[i].Vel.normalized;
                Quaternion rot = Quaternion.LookRotation(dir);
                Vector3 scale = new Vector3(1.6f * fade + 0.4f, 1.6f * fade + 0.4f,
                                            _meteors[i].Len * fade);
                var m = Matrix4x4.TRS(_meteors[i].Pos, rot, scale);
                Graphics.DrawMesh(_meteorMesh, m, _meteorMat, 0, null, 0, null,
                                  ShadowCastingMode.Off, false, null, false);
            }
        }

        private void AddStarMesh(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_starRoot.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Vector3 RandomDomeDir(SeededRandom rng, float minElev, float maxElev)
        {
            float a = rng.NextFloat(0f, Mathf.PI * 2f);
            float e = rng.NextFloat(minElev, maxElev);
            return new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e)).normalized;
        }

        // -- clouds ---------------------------------------------------------------------

        private void BuildClouds(WorldData world)
        {
            var lambert = Shader.Find("Solace/SkyLambert");
            if (lambert == null)
            {
                Debug.LogWarning("[Solace] Solace/SkyLambert shader not found — clouds disabled. " +
                                 "Ensure Assets/Shaders/ is in the project.");
                return;
            }
            _cloudMat = new Material(lambert);
            _cloudMat.enableInstancing = true;

            var rng = SeededRandom.Derive(world.Seed, "unity-clouds");
            _cloudMeshes = new Mesh[3];
            for (int v = 0; v < 3; v++) _cloudMeshes[v] = BuildCloudPuff(rng);

            _clouds = new Cloud[CloudCount];
            for (int i = 0; i < CloudCount; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(420f, 880f);
                _clouds[i] = new Cloud
                {
                    Base = new Vector3(Mathf.Cos(a) * r, rng.NextFloat(170f, 310f), Mathf.Sin(a) * r),
                    Scale = rng.NextFloat(0.7f, 1.6f),
                    Speed = rng.NextFloat(2f, 5.5f),
                    Yaw = rng.NextFloat(0f, 360f),
                    Variant = i % 3,
                };
            }
            _cloudMats = new Matrix4x4[3][];
            _cloudCounts = new int[3];
            for (int v = 0; v < 3; v++) _cloudMats[v] = new Matrix4x4[CloudCount];
        }

        private static Mesh BuildCloudPuff(SeededRandom rng)
        {
            var rock = MeshFactory.FacetedRock();
            var meshes = new List<Mesh>();
            var xforms = new List<Matrix4x4>();
            int blobs = 6;
            for (int i = 0; i < blobs; i++)
            {
                var pos = new Vector3(rng.NextFloat(-15f, 15f), rng.NextFloat(-1f, 2.5f), rng.NextFloat(-6f, 6f));
                var scl = new Vector3(rng.NextFloat(5f, 11f), rng.NextFloat(2.4f, 4.4f), rng.NextFloat(4f, 7f));
                meshes.Add(rock);
                xforms.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), scl));
            }
            return MeshFactory.Merge(meshes, xforms);
        }

        private void UpdateClouds(GameState state, Vector3 camPos, float dt, float edge,
                                  bool night, bool storm, float flicker, Vector3 sunDir, Vector3 moonDir)
        {
            if (_clouds == null || _cloudMat == null) return;
            _cloudTime += dt;

            // Coverage by weather.
            int drawCount = CloudCount;
            if (state.Weather == Weather.Clear) drawCount = 22;
            else if (state.Weather == Weather.Cloudy) drawCount = 34;
            else if (state.Weather == Weather.Rain) drawCount = 38;

            for (int v = 0; v < 3; v++) _cloudCounts[v] = 0;
            for (int i = 0; i < drawCount; i++)
            {
                Cloud c = _clouds[i];
                Vector3 p = c.Base + WindDir * (_cloudTime * c.Speed);
                // Wrap around the camera so the field never empties.
                Vector3 rel = p - camPos;
                rel.x = WrapAxis(rel.x, 950f);
                rel.z = WrapAxis(rel.z, 950f);
                int v = c.Variant;
                _cloudMats[v][_cloudCounts[v]++] = Matrix4x4.TRS(
                    camPos + rel,
                    Quaternion.Euler(0f, c.Yaw + _cloudTime * 0.6f, 0f),
                    Vector3.one * c.Scale);
            }
            for (int v = 0; v < 3; v++)
            {
                if (_cloudCounts[v] == 0) continue;
                MaterialFactory.DrawInstanced(_cloudMeshes[v], _cloudMat, _cloudMats[v], _cloudCounts[v],
                    ShadowCastingMode.Off, false);
            }

            // Cloud mood lighting: single animated sun/moon in the lambert shader.
            Color albedo, sunCol, ambCol;
            Vector3 lightDir;
            if (night)
            {
                albedo = new Color(0.09f, 0.12f, 0.20f);
                lightDir = moonDir;
                sunCol = new Color(0.30f, 0.38f, 0.60f);
                ambCol = new Color(0.06f, 0.08f, 0.14f);
            }
            else
            {
                albedo = Color.Lerp(new Color(0.94f, 0.95f, 0.98f), new Color(1.0f, 0.55f, 0.36f), edge);
                lightDir = sunDir;
                sunCol = Color.Lerp(new Color(1f, 0.96f, 0.88f), new Color(1f, 0.45f, 0.20f), edge);
                ambCol = Color.Lerp(new Color(0.38f, 0.41f, 0.48f), new Color(0.32f, 0.24f, 0.30f), edge);
            }
            if (storm)
            {
                albedo = Color.Lerp(albedo, new Color(0.20f, 0.22f, 0.28f), 0.75f);
                ambCol = Color.Lerp(ambCol, new Color(0.16f, 0.17f, 0.22f), 0.6f);
            }
            if (flicker > 0f) ambCol += new Color(0.65f, 0.70f, 0.88f) * flicker;
            _cloudMat.color = albedo;
            _cloudMat.SetVector(SunDirId, lightDir);
            _cloudMat.SetColor(SunColorId, sunCol);
            _cloudMat.SetColor(AmbColorId, ambCol);
        }

        private static float WrapAxis(float v, float half)
        {
            float span = half * 2f;
            while (v > half) v -= span;
            while (v < -half) v += span;
            return v;
        }

        // -- particles ------------------------------------------------------------

        private void BuildFireflies(WorldData world)
        {
            var go = new GameObject("Fireflies");
            go.transform.SetParent(transform, false);
            PointOfInterest den = null;
            foreach (var p in world.Pois)
                if (p.Type == PoiType.Den) den = p;
            float dx = den != null ? den.X : 0f, dz = den != null ? den.Z : 0f;
            go.transform.position = new Vector3(dx, world.SampleHeight(dx, dz) + 1.5f, dz);

            _fireflies = go.AddComponent<ParticleSystem>();
            var main = _fireflies.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f));
            main.gravityModifier = 0f;
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _fireflies.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            var sh = _fireflies.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 14f;
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            psr.material = MaterialFactory.LitEmissive(new Color(0.7f, 0.55f, 0.2f),
                                                       new Color(1f, 0.8f, 0.35f), 0.4f);
        }

        private void BuildRain()
        {
            _rainGO = new GameObject("Rain");
            _rainGO.transform.SetParent(transform, false);
            _rain = _rainGO.AddComponent<ParticleSystem>();
            var main = _rain.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(22f, 30f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.65f, 0.75f, 0.85f));
            main.gravityModifier = 0f;
            main.maxParticles = 1200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _rain.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            var sh = _rain.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(40f, 1f, 40f);
            var psr = _rainGO.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            // Thin vertical streaks (opaque): read as rain at speed.
            psr.mesh = MeshFactory.Streak();
            psr.material = MaterialFactory.Lit(new Color(0.60f, 0.70f, 0.80f), 0.4f);
        }

        private static void SetEmission(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var em = ps.emission;
            var curve = em.rateOverTime;
            if (Mathf.Abs(curve.constant - rate) < 0.01f) return;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }
    }
}
