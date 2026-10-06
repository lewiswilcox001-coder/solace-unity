// Solace.Unity — time of day, weather and atmosphere from sim state.
//
// Sun + moon directionals, flat ambient, exponential fog, and an animated
// sky color on the camera (no skybox asset needed). Stars are one instanced
// draw of tiny emissive spheres; fireflies and rain are mesh-particle
// systems (opaque — no transparency anywhere). All particle emission rates
// are driven by IsNight / Weather.
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

        private ParticleSystem _fireflies;
        private ParticleSystem _rain;
        private GameObject _rainGO;

        private static readonly Color SkyDay = new Color(0.55f, 0.70f, 0.83f);
        private static readonly Color SkyDusk = new Color(0.96f, 0.52f, 0.30f);
        private static readonly Color SkyNight = new Color(0.012f, 0.022f, 0.055f);
        private static readonly Color FogDay = new Color(0.60f, 0.67f, 0.74f);
        private static readonly Color FogNight = new Color(0.030f, 0.045f, 0.095f);
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

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            BuildStars(world);
            BuildFireflies(world);
            BuildRain();
        }

        public void SyncFromState(GameState state)
        {
            float t = state.TimeOfDay; // 0..24
            bool night = state.IsNight;

            // Sun path: rises 6h, sets 18h.
            float sunT = (t - 6f) / 12f; // 0..1 across the day
            float elev = Mathf.Sin(Mathf.Clamp01(sunT) * Mathf.PI);
            float sunI = night ? 0f : Mathf.Clamp01(elev) * 1.15f;
            _sun.intensity = sunI;
            float azim = Mathf.Lerp(-90f, 270f, Mathf.Clamp01(sunT));
            _sun.transform.rotation = Quaternion.Euler(90f - Mathf.Clamp01(elev) * 75f, azim, 0f);
            // Warm at the edges of the day.
            float warmth = 1f - Mathf.Clamp01(elev * 1.6f);
            _sun.color = Color.Lerp(new Color(1f, 0.94f, 0.86f), new Color(1f, 0.52f, 0.28f), night ? 0f : warmth);

            _moon.intensity = night ? 0.38f : 0f;
            _moon.transform.rotation = Quaternion.Euler(35f, 200f, 0f);

            // Sky / fog / ambient.
            // Dusk factor: peaks near sunrise/sunset.
            float edge = night ? 0f : Mathf.Clamp01(1f - elev * 2.2f);
            Color sky = night ? SkyNight : Color.Lerp(SkyDay, SkyDusk, edge * 0.85f);
            _cam.backgroundColor = sky;
            _cam.clearFlags = CameraClearFlags.SolidColor;

            Color fogC = night ? FogNight : Color.Lerp(FogDay, new Color(0.82f, 0.60f, 0.50f), edge * 0.7f);
            RenderSettings.fogColor = fogC;
            // Fog is the cheapest depth cue we have — lean into it.
            float density = night ? 0.0042f : 0.0026f;
            density += edge * 0.0012f; // dawn/dusk mist
            if (state.Weather == Weather.Rain) density += 0.0022f;
            if (state.Weather == Weather.Storm) density += 0.0045f;
            if (state.Weather == Weather.Cloudy) density += 0.0008f;
            RenderSettings.fogDensity = density;

            RenderSettings.ambientLight = night ? AmbNight : Color.Lerp(AmbDay, new Color(0.50f, 0.40f, 0.34f), edge * 0.6f);

            // Stars at night.
            if (night) DrawStars();
            SetEmission(_fireflies, night ? 26f : 0f);
            float rainRate = state.Weather == Weather.Storm ? 900f : state.Weather == Weather.Rain ? 380f : 0f;
            SetEmission(_rain, rainRate);
            if (rainRate > 0f && _cam != null)
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
