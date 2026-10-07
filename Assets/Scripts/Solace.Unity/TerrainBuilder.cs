// Solace.Unity — terrain and water, generated from WorldData.
//
// The terrain is ONE mesh with SHARED vertices and SMOOTH normals — soft,
// "A Short Hike"-style low-poly shading instead of crystalline facets.
// Color comes from 36 curated materials (6 biomes × 6 height bands): the
// finer band quantization (vs the old 3) makes altitude gradients read as
// soft blends, and the harsh shade-variant checkerboard is gone. One opaque
// matte material per bucket — no vertex colors (URP Lit ignores them), no
// custom shaders. Seasons swap the 36 materials for re-tinted versions.
// Built at half the sim resolution (8m cells): 65×65 shared verts (~4k,
// down from ~16k duplicated).
//
// Water: a faceted ribbon along RiverPath + a disc at LakeCenter, drawn at a
// fixed level with a slight per-facet height jitter so it sparkles. At night
// (when WorldData.NightRiverGlow) the river glows teal and breathes.
using System.Collections.Generic;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class TerrainBuilder : MonoBehaviour, ISimView
    {
        private const int GridRes = 64; // 8m cells over the 512m vale

        // Height-band edges: lowland (damp) → rolling → alpine.
        private const float BandLowMax = 7f;
        private const float BandMidMax = 24f;

        private Material _waterMat;
        private MeshRenderer _terrainRenderer; // stored so seasons can re-tint the palette
        private Color[] _bucketColors;         // 36 curated colors, re-tinted per season
        private static readonly Color WaterDay = new Color(0.10f, 0.28f, 0.34f);
        private static readonly Color WaterGlow = new Color(0.14f, 0.72f, 0.66f);

        // Per-biome 3-stop ramps: [biome * 3 + band]. Curated by hand —
        // analogous greens, cool rock, warm sand; low bands run darker/cooler
        // (shadowed damp), high bands lighter (sunlit alpine).
        private static readonly Color[] BiomeRamps = new Color[]
        {
            // Mistmoor: muted sage moor
            new Color(0.30f, 0.36f, 0.32f), // low: damp sage
            new Color(0.38f, 0.44f, 0.38f), // mid: sage
            new Color(0.52f, 0.54f, 0.50f), // high: pale lichen
            // Foxpine: deep forest
            new Color(0.08f, 0.17f, 0.13f), // low: dark forest floor
            new Color(0.11f, 0.24f, 0.17f), // mid: moss pine
            new Color(0.24f, 0.28f, 0.24f), // high: grey-green rock transition
            // FellCrag: grey crags
            new Color(0.28f, 0.28f, 0.31f), // low: shadowed slate
            new Color(0.36f, 0.36f, 0.38f), // mid: grey
            new Color(0.50f, 0.50f, 0.53f), // high: sunlit rock
            // SnowPeak: snow
            new Color(0.55f, 0.60f, 0.66f), // low: blue-grey rock below snowline
            new Color(0.72f, 0.76f, 0.81f), // mid: pale snow-blue
            new Color(0.86f, 0.89f, 0.92f), // high: bright snow
            // Riverbank: sand
            new Color(0.45f, 0.38f, 0.26f), // low: wet dark sand
            new Color(0.60f, 0.52f, 0.37f), // mid: sand
            new Color(0.68f, 0.60f, 0.44f), // high: dry pale sand
            // DenGrounds: moss home
            new Color(0.24f, 0.31f, 0.17f), // low: deep moss
            new Color(0.32f, 0.39f, 0.22f), // mid: moss
            new Color(0.44f, 0.42f, 0.30f), // high: warm earth
        };

        public void Build(WorldData world)
        {
            GameBootstrap.Instance.RegisterView(this);
            BuildTerrainMesh(world);
            BuildWater(world);
        }

        private void BuildTerrainMesh(WorldData world)
        {
            float half = world.HalfSize;
            float step = world.SizeMeters / GridRes;
            int n = GridRes + 1; // shared grid verts per side
            const int Bands = 6;
            const int Buckets = 6 * Bands; // 6 biomes × 6 height bands

            _bucketColors = BuildBucketColors();

            var verts = new Vector3[n * n];
            for (int gz = 0; gz < n; gz++)
                for (int gx = 0; gx < n; gx++)
                    verts[gz * n + gx] = new Vector3(-half + gx * step, world.SampleHeight(-half + gx * step, -half + gz * step), -half + gz * step);

            // Per-bucket triangle lists over the SHARED vertex buffer, so
            // RecalculateNormals averages across bucket boundaries → smooth.
            // Winding matches the old per-quad build (upward-facing).
            var tris = new List<int>[Buckets];
            for (int i = 0; i < Buckets; i++) tris[i] = new List<int>();
            for (int cz = 0; cz < GridRes; cz++)
            {
                for (int cx = 0; cx < GridRes; cx++)
                {
                    int b = cz * n + cx;
                    float xc = -half + (cx + 0.5f) * step;
                    float zc = -half + (cz + 0.5f) * step;
                    float hAvg = (verts[b].y + verts[b + 1].y + verts[b + n].y + verts[b + n + 1].y) * 0.25f;
                    int bucket = (int)world.GetBiome(xc, zc) * Bands + HeightBand(hAvg);
                    tris[bucket].Add(b); tris[bucket].Add(b + n + 1); tris[bucket].Add(b + 1);
                    tris[bucket].Add(b); tris[bucket].Add(b + n); tris[bucket].Add(b + n + 1);
                }
            }

            var mesh = new Mesh();
            mesh.subMeshCount = Buckets;
            mesh.SetVertices(verts);
            for (int i = 0; i < Buckets; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateNormals(); // shared verts → smooth soft shading
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[Buckets];
            for (int i = 0; i < Buckets; i++)
                materials[i] = MaterialFactory.Lit(_bucketColors[i], 0.12f); // matte earth, never glossy
            renderer.sharedMaterials = materials;
            renderer.receiveShadows = true;
            _terrainRenderer = renderer; // seasons re-tint via ApplySeasonTint
        }

        // Six altitude bands (meters). Finer than the old 3-stop bands so
        // mountainsides read as soft gradients, not hard stripes.
        private static int HeightBand(float h)
        {
            if (h < 8f) return 0;
            if (h < 16f) return 1;
            if (h < 26f) return 2;
            if (h < 38f) return 3;
            if (h < 52f) return 4;
            return 5;
        }

        /// <summary>
        /// The 36 bucket colors: each biome's hand-curated 3-stop ramp sampled
        /// as a continuous piecewise-linear function at the six band centers,
        /// so adjacent bands are close in color — gradual blends, no stripes.
        /// </summary>
        private Color[] BuildBucketColors()
        {
            float[] centers = { 4f, 12f, 21f, 32f, 45f, 60f };
            var colors = new Color[36];
            for (int bi = 0; bi < 6; bi++)
                for (int b = 0; b < 6; b++)
                    colors[bi * 6 + b] = SampleRamp(bi, centers[b]);
            return colors;
        }

        /// <summary>Continuous color along a biome's 3-stop ramp at height h.</summary>
        private Color SampleRamp(int biome, float h)
        {
            Color c0 = BiomeRamps[biome * 3];
            Color c1 = BiomeRamps[biome * 3 + 1];
            Color c2 = BiomeRamps[biome * 3 + 2];
            if (h <= BandLowMax) return c0;
            if (h >= BandMidMax) return Color.Lerp(c1, c2, Mathf.Clamp01((h - BandMidMax) / 26f));
            return Color.Lerp(c0, c1, (h - BandLowMax) / (BandMidMax - BandLowMax));
        }

        private void BuildWater(WorldData world)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float y = WorldData.WaterLevel + 0.12f;

            // River ribbon. Each quad gets a tiny deterministic height jitter
            // so the facets catch the sun individually — crystalline sparkle.
            var path = world.RiverPath;
            const float halfWidth = 3.5f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                V2 p0 = path[i], p1 = path[i + 1];
                float dx = p1.X - p0.X, dz = p1.Z - p0.Z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f) continue;
                float j0 = (Fract(Mathf.Sin(i * 12.9898f) * 43758.5453f) - 0.5f) * 0.05f;
                float j1 = (Fract(Mathf.Sin((i + 1) * 12.9898f) * 43758.5453f) - 0.5f) * 0.05f;
                float nx = -dz / len * halfWidth, nz = dx / len * halfWidth;
                int b = verts.Count;
                verts.Add(new Vector3(p0.X + nx, y + j0, p0.Z + nz)); // L0
                verts.Add(new Vector3(p0.X - nx, y + j0, p0.Z - nz)); // R0
                verts.Add(new Vector3(p1.X + nx, y + j1, p1.Z + nz)); // L1
                verts.Add(new Vector3(p1.X - nx, y + j1, p1.Z - nz)); // R1
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            // Loch disc.
            {
                int segments = 28;
                int b = verts.Count;
                verts.Add(new Vector3(world.LakeCenter.X, y, world.LakeCenter.Z));
                for (int s = 0; s <= segments; s++)
                {
                    float a = (s / (float)segments) * Mathf.PI * 2f;
                    verts.Add(new Vector3(
                        world.LakeCenter.X + Mathf.Cos(a) * world.LakeRadius, y,
                        world.LakeCenter.Z + Mathf.Sin(a) * world.LakeRadius));
                }
                for (int s = 0; s < segments; s++)
                    { tris.Add(b); tris.Add(b + 1 + s + 1); tris.Add(b + 1 + s); }
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _waterMat = MaterialFactory.NewLitEmissiveInstance(WaterDay, Color.black, 0.6f);

            var go = new GameObject("Water");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _waterMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SyncFromState(GameState state)
        {
            if (_waterMat == null) return;
            bool glow = state.World.NightRiverGlow && state.IsNight;
            if (glow)
            {
                // The river breathes: a slow luminous pulse, never a flat neon.
                float breathe = 0.72f + 0.28f * Mathf.Sin(Time.time * 0.6f);
                MaterialFactory.SetEmission(_waterMat, WaterGlow * breathe);
            }
            else
            {
                MaterialFactory.SetEmission(_waterMat, Color.black);
            }
        }

        /// <summary>
        /// <summary>
        /// Re-hues the whole terrain for a season: swaps the 36 bucket
        /// materials for season-tinted versions (cached in MaterialFactory,
        /// so repeated turns are cheap) and re-tints the owned water material.
        /// Called by SeasonView on season change.
        /// </summary>
        public void ApplySeasonTint(Season season)
        {
            if (_terrainRenderer != null && _bucketColors != null)
            {
                var mats = new Material[36];
                for (int i = 0; i < 36; i++)
                    mats[i] = MaterialFactory.Lit(SeasonPalette.TintTerrain(_bucketColors[i], season), 0.12f);
                _terrainRenderer.sharedMaterials = mats;
            }
            if (_waterMat != null)
                _waterMat.color = SeasonPalette.TintWater(season);
        }

        private static float Fract(float x) { return x - Mathf.Floor(x); }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }
    }
}
