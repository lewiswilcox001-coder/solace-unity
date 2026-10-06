// Solace.Unity — time of day, weather and atmosphere from sim state.
//
// Sun + moon directionals, flat ambient, exponential fog, and an animated
// sky color on the camera (no skybox asset needed). Stars are one instanced
// draw of tiny emissive spheres; fireflies, rain and embers are instanced
// mote fields (opaque — no transparency anywhere). Mote intensity is driven
// by IsNight / Weather.
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class DayNightCycle : MonoBehaviour, ISimView
    {
        private Light _sun;
        private Light _moon;
        private Camera _cam;

        // Stars: one instanced draw, dome follows the camera.
        private Mesh _starMesh;
        private Material _starMat;
        private Matrix4x4[] _starMatrices;
        private Vector3 _starAnchor;
        private const int StarCount = 220;

        private MoteField _fireflies;
        private MoteField _rain;
        private GameObject _rainGO;

        private static readonly Color SkyDay = new Color(0.60f, 0.74f, 0.86f);
        private static readonly Color SkyDusk = new Color(0.98f, 0.55f, 0.32f);
        private static readonly Color SkyNight = new Color(0.015f, 0.025f, 0.06f);
        private static readonly Color FogDay = new Color(0.66f, 0.72f, 0.78f);
        private static readonly Color FogNight = new Color(0.05f, 0.07f, 0.12f);
        private static readonly Color AmbDay = new Color(0.55f, 0.60f, 0.66f);
        private static readonly Color AmbNight = new Color(0.10f, 0.13f, 0.20f);

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

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            BuildStars(world);
            BuildFireflies(world);
            BuildRain(world);
        }

        public void SyncFromState(GameState state)
        {
            float t = state.TimeOfDay; // 0..24
            bool night = state.IsNight;

            // Sun path: rises 6h, sets 18h.
            float sunT = (t - 6f) / 12f; // 0..1 across the day
            float elev = Mathf.Sin(Mathf.Clamp01(sunT) * Mathf.PI);
            float sunI = night ? 0f : Mathf.Clamp01(elev) * 1.35f;
            _sun.intensity = sunI;
            float azim = Mathf.Lerp(-90f, 270f, Mathf.Clamp01(sunT));
            _sun.transform.rotation = Quaternion.Euler(90f - Mathf.Clamp01(elev) * 75f, azim, 0f);
            // Warm at the edges of the day.
            float warmth = 1f - Mathf.Clamp01(elev * 1.6f);
            _sun.color = Color.Lerp(new Color(1f, 0.96f, 0.90f), new Color(1f, 0.55f, 0.30f), night ? 0f : warmth);

            _moon.intensity = night ? 0.30f : 0f;
            _moon.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

            // Sky / fog / ambient.
            // Dusk factor: peaks near sunrise/sunset.
            float edge = night ? 0f : Mathf.Clamp01(1f - elev * 2.2f);
            Color sky = night ? SkyNight : Color.Lerp(SkyDay, SkyDusk, edge * 0.85f);
            _cam.backgroundColor = sky;
            _cam.clearFlags = CameraClearFlags.SolidColor;

            Color fogC = night ? FogNight : Color.Lerp(FogDay, new Color(0.85f, 0.62f, 0.52f), edge * 0.7f);
            RenderSettings.fogColor = fogC;
            float density = night ? 0.0032f : 0.0018f;
            density += edge * 0.0012f; // dawn/dusk mist
            if (state.Weather == Weather.Rain) density += 0.0022f;
            if (state.Weather == Weather.Storm) density += 0.0045f;
            if (state.Weather == Weather.Cloudy) density += 0.0008f;
            RenderSettings.fogDensity = density;

            RenderSettings.ambientLight = night ? AmbNight : Color.Lerp(AmbDay, new Color(0.55f, 0.42f, 0.36f), edge * 0.6f);

            // Stars at night.
            if (night) DrawStars();
            // Fireflies at night; rain by weather.
            _fireflies.SetIntensity(night ? 1f : 0f);
            float rainIntensity = state.Weather == Weather.Storm ? 1f
                : state.Weather == Weather.Rain ? 0.42f : 0f;
            _rain.SetIntensity(rainIntensity);
            if (rainIntensity > 0f && _cam != null)
            {
                Vector3 cp = _cam.transform.position;
                _rainGO.transform.position = new Vector3(cp.x, cp.y + 12f, cp.z);
            }
        }

        // -- stars ---------------------------------------------------------------

        private void BuildStars(WorldData world)
        {
            _starMesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            _starMat = MaterialFactory.LitEmissive(Color.white, Color.white * 1.6f, 0.3f);
            _starMatrices = new Matrix4x4[StarCount];
            var rng = SeededRandom.Derive(world.Seed, "unity-stars");
            for (int i = 0; i < StarCount; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float e = rng.NextFloat(0.08f, 1.4f);
                float r = 1500f;
                var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
                float s = rng.NextFloat(1.5f, 4f);
                _starMatrices[i] = Matrix4x4.TRS(dir * r, Quaternion.identity, Vector3.one * s);
            }
            _starAnchor = new Vector3(float.MaxValue, 0f, 0f);
        }

        private void DrawStars()
        {
            if (_cam == null) return;
            Vector3 cp = _cam.transform.position;
            if ((cp - _starAnchor).sqrMagnitude > 2500f)
            {
                // Re-anchor the dome to the camera (allocation-free).
                Vector3 d = cp - _starAnchor;
                for (int i = 0; i < StarCount; i++)
                {
                    Vector3 p = _starMatrices[i].GetColumn(3);
                    _starMatrices[i].SetColumn(3, p + d);
                }
                _starAnchor = cp;
            }
            Graphics.DrawMeshInstanced(_starMesh, 0, _starMat, _starMatrices, StarCount,
                null, ShadowCastingMode.Off, false);
        }

        // -- motes ----------------------------------------------------------------

        private void BuildFireflies(WorldData world)
        {
            var go = new GameObject("Fireflies");
            go.transform.SetParent(transform, false);
            PointOfInterest den = null;
            foreach (var p in world.Pois)
                if (p.Type == PoiType.Den) den = p;
            float dx = den != null ? den.X : 0f, dz = den != null ? den.Z : 0f;
            go.transform.position = new Vector3(dx, world.SampleHeight(dx, dz) + 1.5f, dz);

            var motes = go.AddComponent<MoteField>();
            motes.Setup(new MoteConfig
            {
                Mesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere),
                Material = MaterialFactory.LitEmissive(new Color(0.7f, 0.55f, 0.2f),
                                                       new Color(1f, 0.8f, 0.35f), 0.4f),
                Count = 120,
                Behavior = MoteBehavior.Drift,
                Spherical = true,
                Volume = new Vector3(14f, 0f, 0f),
                MinScale = 0.05f,
                MaxScale = 0.12f,
                MinSpeed = 0.4f,
                MaxSpeed = 1.4f,
                VerticalRange = 1.5f, // wander amplitude
            }, SeededRandom.Derive(world.Seed, "unity-motes-fireflies"));
            _fireflies = motes;
        }

        private void BuildRain(WorldData world)
        {
            _rainGO = new GameObject("Rain");
            _rainGO.transform.SetParent(transform, false);
            var motes = _rainGO.AddComponent<MoteField>();
            motes.Setup(new MoteConfig
            {
                // Thin vertical streaks (opaque): read as rain at speed.
                Mesh = MeshFactory.Streak(),
                Material = MaterialFactory.Lit(new Color(0.60f, 0.70f, 0.80f), 0.4f),
                Count = 1200,
                Behavior = MoteBehavior.Fall,
                Spherical = false,
                Volume = new Vector3(40f, 0f, 40f),
                MinScale = 0.8f,
                MaxScale = 1.2f,
                MinSpeed = 22f,
                MaxSpeed = 30f,
                VerticalRange = 30f, // fall distance; volume re-centers above the camera
            }, SeededRandom.Derive(world.Seed, "unity-motes-rain"));
            _rain = motes;
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }
    }
}
