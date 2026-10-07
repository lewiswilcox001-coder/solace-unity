// Solace.Unity — terrain and water, generated from WorldData.
//
// The terrain is one mesh with 36 submeshes (6 biomes × 3 height bands ×
// 2 shade variants), flat-shaded via duplicated vertices. Each biome owns a
// hand-picked 3-stop color ramp (lowland → rolling → alpine) so mountains
// read as deliberate gradient art, not random noise. One opaque matte
// material per submesh — no vertex colors, no custom shaders. Built at half
// the sim resolution (8m cells): chunky facets suit the poly-art look and
// keep the vertex count at ~16k.
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
            const int Buckets = 36; // 6 biomes × 3 height bands × 2 shade variants

            var verts = new List<Vector3>[Buckets];
            var tris = new List<int>[Buckets];
            for (int i = 0; i < Buckets; i++) { verts[i] = new List<Vector3>(); tris[i] = new List<int>(); }

            for (int cx = 0; cx < GridRes; cx++)
            {
                for (int cz = 0; cz < GridRes; cz++)
                {
                    float x0 = -half + cx * step;
                    float x1 = x0 + step;
                    float z0 = -half + cz * step;
                    float z1 = z0 + step;
                    float xc = (x0 + x1) * 0.5f;
                    float zc = (z0 + z1) * 0.5f;

                    float h00 = world.SampleHeight(x0, z0);
                    float h10 = world.SampleHeight(x1, z0);
                    float h11 = world.SampleHeight(x1, z1);
                    float h01 = world.SampleHeight(x0, z1);
                    float hAvg = (h00 + h10 + h11 + h01) * 0.25f;
                    int band = hAvg < BandLowMax ? 0 : (hAvg < BandMidMax ? 1 : 2);

                    Biome biome = world.GetBiome(xc, zc);
                    int shade = ((cx * 73 + cz * 149) & 1);
                    int bucket = ((int)biome * 3 + band) * 2 + shade;

                    int b = verts[bucket].Count;
                    verts[bucket].Add(new Vector3(x0, h00, z0));
                    verts[bucket].Add(new Vector3(x1, h10, z0));
                    verts[bucket].Add(new Vector3(x1, h11, z1));
                    verts[bucket].Add(new Vector3(x0, h01, z1));
                    tris[bucket].Add(b); tris[bucket].Add(b + 2); tris[bucket].Add(b + 1);
                    tris[bucket].Add(b); tris[bucket].Add(b + 3); tris[bucket].Add(b + 2);
                }
            }

            var mesh = new Mesh();
            mesh.subMeshCount = Buckets;
            var allVerts = new List<Vector3>();
            var allTris = new List<int>();
            var materials = new Material[Buckets];
            for (int i = 0; i < Buckets; i++)
            {
                int base_ = allVerts.Count;
                allVerts.AddRange(verts[i]);
                for (int t = 0; t < tris[i].Count; t++) allTris.Add(base_ + tris[i][t]);
                Color c = BiomeRamps[i / 2] * (i % 2 == 0 ? 1f : 0.92f);
                materials[i] = MaterialFactory.Lit(c, 0.12f); // matte earth, never glossy
            }
            mesh.SetVertices(allVerts);
            // Per-submesh triangle ranges over the concatenated index list.
            int cursor = 0;
            for (int i = 0; i < Buckets; i++)
            {
                var sub = new List<int>();
                for (int t = 0; t < tris[i].Count; t++) sub.Add(allTris[cursor + t]);
                cursor += tris[i].Count;
                mesh.SetTriangles(sub, i);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.receiveShadows = true;
            _terrainRenderer = renderer; // seasons re-tint via ApplySeasonTint
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
        /// Re-hues the whole terrain for a season. Swaps the 36 bucket
        /// materials for season-tinted versions (cached in MaterialFactory,
        /// so repeated turns are cheap) and re-tints the owned water material.
        /// Called by SeasonView on season change.
        /// </summary>
        public void ApplySeasonTint(Season season)
        {
            if (_terrainRenderer != null)
            {
                var mats = new Material[36];
                for (int i = 0; i < 36; i++)
                {
                    Color base_ = BiomeRamps[i / 2] * (i % 2 == 0 ? 1f : 0.92f);
                    mats[i] = MaterialFactory.Lit(SeasonPalette.TintTerrain(base_, season), 0.12f);
                }
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
