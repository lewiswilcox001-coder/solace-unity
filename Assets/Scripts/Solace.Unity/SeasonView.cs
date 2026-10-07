// Solace.Unity — seasonal visuals: palette shifts, vegetation tints, snow/petals.
//
// SeasonView watches SeasonSystem.Current(state) and, on change:
//   1. Asks TerrainBuilder to swap its 36 bucket materials for season-tinted
//      versions (curated poly-art ramps, re-hued per season).
//   2. Re-tints the shared scatter vegetation materials (pines, grasses,
//      bushes) via the MaterialFactory cache — same instances the scatter
//      system draws with, so the whole vale shifts at once. Keys are unique
//      to vegetation; nothing else is affected.
//   3. Reconfigures one "season motes" particle system: cherry-blossom
//      petals in spring, amber leaves in autumn, snow in winter.
//
// Pines are evergreen, so winter reads as snow-dusted rather than bare;
// bushes go bare-brown. True per-instance leaf loss would need scatter
// ownership and is out of scope.
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    /// <summary>Curated per-season color grading for the vale. All tints are
    /// hand-picked for the geometric-art look — deliberate, not procedural noise.</summary>
    public static class SeasonPalette
    {
        public static Color TintTerrain(Color baseColor, Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    return baseColor * new Color(0.95f, 1.10f, 0.90f); // fresh green push
                case Season.Summer:
                    return baseColor * new Color(1.10f, 1.02f, 0.85f); // golden warmth
                case Season.Autumn:
                    return baseColor * new Color(1.18f, 0.82f, 0.62f); // amber/rust
                default:
                {
                    // Winter: snow hush — dim the earth, lerp toward snow blue-white.
                    Color dim = baseColor * 0.85f;
                    return Color.Lerp(dim, new Color(0.82f, 0.86f, 0.92f), 0.72f);
                }
            }
        }

        public static Color TintWater(Season season)
        {
            switch (season)
            {
                case Season.Spring: return new Color(0.10f, 0.32f, 0.34f); // fresh teal
                case Season.Summer: return new Color(0.08f, 0.30f, 0.38f); // deep summer water
                case Season.Autumn: return new Color(0.10f, 0.22f, 0.28f); // dark mirror
                default: return new Color(0.55f, 0.65f, 0.72f);            // winter ice
            }
        }

        // Vegetation: (factory key color, smoothness, seasonal targets).
        // Keys match ScatterSystem's AddVariant calls exactly.
        private struct VegTint
        {
            public Color Key; public float Smooth;
            public Color Spring; public Color Summer; public Color Autumn; public Color Winter;
        }

        private static readonly VegTint[] VegTints = new VegTint[]
        {
            new VegTint { // pine crowns — evergreen, snow-dusted in winter
                Key = new Color(0.10f, 0.24f, 0.17f), Smooth = 0.20f,
                Spring = new Color(0.13f, 0.33f, 0.19f), Summer = new Color(0.10f, 0.24f, 0.17f),
                Autumn = new Color(0.38f, 0.24f, 0.11f), Winter = new Color(0.42f, 0.48f, 0.52f) },
            new VegTint { // grass tufts
                Key = new Color(0.30f, 0.42f, 0.21f), Smooth = 0.25f,
                Spring = new Color(0.32f, 0.50f, 0.22f), Summer = new Color(0.38f, 0.46f, 0.20f),
                Autumn = new Color(0.48f, 0.34f, 0.14f), Winter = new Color(0.58f, 0.60f, 0.62f) },
            new VegTint { // tall grass
                Key = new Color(0.33f, 0.44f, 0.19f), Smooth = 0.25f,
                Spring = new Color(0.35f, 0.52f, 0.20f), Summer = new Color(0.40f, 0.48f, 0.18f),
                Autumn = new Color(0.50f, 0.36f, 0.13f), Winter = new Color(0.60f, 0.62f, 0.64f) },
            new VegTint { // bushes — bare brown in winter
                Key = new Color(0.10f, 0.25f, 0.12f), Smooth = 0.25f,
                Spring = new Color(0.12f, 0.30f, 0.14f), Summer = new Color(0.10f, 0.25f, 0.12f),
                Autumn = new Color(0.40f, 0.22f, 0.08f), Winter = new Color(0.28f, 0.22f, 0.18f) },
            new VegTint { // reed stalks
                Key = new Color(0.30f, 0.38f, 0.20f), Smooth = 0.30f,
                Spring = new Color(0.32f, 0.46f, 0.20f), Summer = new Color(0.30f, 0.38f, 0.20f),
                Autumn = new Color(0.46f, 0.32f, 0.14f), Winter = new Color(0.52f, 0.54f, 0.56f) },
        };

        public static void ApplyVegetation(Season season)
        {
            foreach (var v in VegTints)
            {
                Material m = MaterialFactory.Lit(v.Key, v.Smooth);
                if (m == null) continue;
                switch (season)
                {
                    case Season.Spring: m.color = v.Spring; break;
                    case Season.Summer: m.color = v.Summer; break;
                    case Season.Autumn: m.color = v.Autumn; break;
                    default: m.color = v.Winter; break;
                }
            }
        }
    }

    public class SeasonView : MonoBehaviour, ISimView
    {
        private TerrainBuilder _terrain;
        private Season _lastSeason = (Season)(-1);
        private ParticleSystem _motes;
        private GameObject _motesGO;
        private Material _motesMat; // dedicated (unique Unlit-white cache key)
        private Camera _cam;

        public void Build(WorldData world, TerrainBuilder terrain)
        {
            GameBootstrap.Instance.RegisterView(this);
            _terrain = terrain;
            _cam = GameBootstrap.Instance.Camera.GetComponent<Camera>();
            BuildMotes();
            // Apply immediately so a loaded winter life doesn't flash summer.
            _lastSeason = (Season)(-1);
            SyncFromState(GameBootstrap.Instance.Sim.State);
        }

        public void SyncFromState(GameState state)
        {
            Season now = SeasonSystem.Current(state);
            if (now == _lastSeason) return;
            _lastSeason = now;
            if (_terrain != null) _terrain.ApplySeasonTint(now);
            SeasonPalette.ApplyVegetation(now);
            ConfigureMotes(now);
        }

        // -- season motes: petals / leaves / snow ---------------------------------

        private void BuildMotes()
        {
            _motesGO = new GameObject("SeasonMotes");
            _motesGO.transform.SetParent(transform, false);
            _motes = _motesGO.AddComponent<ParticleSystem>();
            var main = _motes.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = 0.15f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _motes.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            var sh = _motes.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(60f, 1f, 60f);
            // Gentle sideways drift so petals/leaves wander instead of falling straight.
            var vel = _motes.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var psr = _motesGO.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            psr.material = MaterialFactory.Unlit(Color.white);
            // Motes material is dedicated (unique Unlit-white cache key) — safe to recolor per season.
            _motesMat = psr.material;
        }

        private void ConfigureMotes(Season season)
        {
            if (_motes == null) return;
            var em = _motes.emission;
            var main = _motes.main;
            switch (season)
            {
                case Season.Spring: // cherry-blossom petals
                    em.rateOverTime = new ParticleSystem.MinMaxCurve(45f);
                    main.gravityModifier = 0.08f;
                    if (_motesMat != null) _motesMat.color = new Color(1f, 0.78f, 0.84f);
                    break;
                case Season.Autumn: // falling amber leaves
                    em.rateOverTime = new ParticleSystem.MinMaxCurve(35f);
                    main.gravityModifier = 0.25f;
                    if (_motesMat != null) _motesMat.color = new Color(0.85f, 0.55f, 0.20f);
                    break;
                case Season.Winter: // snow
                    em.rateOverTime = new ParticleSystem.MinMaxCurve(220f);
                    main.gravityModifier = 0.55f;
                    if (_motesMat != null) _motesMat.color = new Color(0.93f, 0.95f, 1f);
                    break;
                default: // summer: the air is clear (fireflies own the night)
                    em.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
                    break;
            }
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            SyncFromState(boot.Sim.State);
            // Motes follow the camera like the rain does.
            if (_motesGO != null && _cam != null)
            {
                Vector3 cp = _cam.transform.position;
                _motesGO.transform.position = new Vector3(cp.x, cp.y + 6f, cp.z);
            }
        }
    }
}
