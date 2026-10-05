// Solace.Unity — terrain and water, generated from WorldData.
//
// The terrain is one mesh with 12 submeshes (6 biomes × 2 shade variants),
// flat-shaded via duplicated vertices. One opaque material per submesh — no
// vertex colors, no custom shaders. Built at half the sim resolution (8m
// cells): chunkier facets suit the "remembered wilderness" look and keep the
// vertex count at ~16k.
//
// Water: an opaque ribbon along RiverPath + a disc at LakeCenter, drawn at a
// fixed level. At night (when WorldData.NightRiverGlow) the river glows teal.
using System.Collections.Generic;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class TerrainBuilder : MonoBehaviour, ISimView
    {
        private const int GridRes = 64; // 8m cells over the 512m vale

        private Material _waterMat;
        private static readonly Color WaterDay = new Color(0.10f, 0.23f, 0.30f);
        private static readonly Color WaterGlow = new Color(0.15f, 0.75f, 0.70f);

        // Biome base colors (§12: regional memory).
        private static readonly Color[] BiomeColors = new Color[]
        {
            new Color(0.45f, 0.52f, 0.50f), // Mistmoor: misty violet-green
            new Color(0.10f, 0.28f, 0.24f), // Foxpine: deep blue-green
            new Color(0.42f, 0.42f, 0.45f), // FellCrag: grey
            new Color(0.88f, 0.90f, 0.94f), // SnowPeak: white
            new Color(0.72f, 0.64f, 0.47f), // Riverbank: sandy
            new Color(0.38f, 0.48f, 0.26f), // DenGrounds: warm moss
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

            var verts = new List<Vector3>[12];
            var tris = new List<int>[12];
            for (int i = 0; i < 12; i++) { verts[i] = new List<Vector3>(); tris[i] = new List<int>(); }

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

                    Biome biome = world.GetBiome(xc, zc);
                    int shade = ((cx * 73 + cz * 149) & 1);
                    int bucket = ((int)biome) * 2 + shade;

                    int b = verts[bucket].Count;
                    verts[bucket].Add(new Vector3(x0, world.SampleHeight(x0, z0), z0));
                    verts[bucket].Add(new Vector3(x1, world.SampleHeight(x1, z0), z0));
                    verts[bucket].Add(new Vector3(x1, world.SampleHeight(x1, z1), z1));
                    verts[bucket].Add(new Vector3(x0, world.SampleHeight(x0, z1), z1));
                    tris[bucket].Add(b); tris[bucket].Add(b + 2); tris[bucket].Add(b + 1);
                    tris[bucket].Add(b); tris[bucket].Add(b + 3); tris[bucket].Add(b + 2);
                }
            }

            var mesh = new Mesh();
            mesh.subMeshCount = 12;
            var allVerts = new List<Vector3>();
            var allTris = new List<int>();
            var materials = new Material[12];
            for (int i = 0; i < 12; i++)
            {
                int base_ = allVerts.Count;
                allVerts.AddRange(verts[i]);
                for (int t = 0; t < tris[i].Count; t++) allTris.Add(base_ + tris[i][t]);
                Color c = BiomeColors[i / 2] * (i % 2 == 0 ? 1f : 0.9f);
                materials[i] = MaterialFactory.Lit(c, 0.9f);
            }
            mesh.SetVertices(allVerts);
            // Per-submesh triangle ranges over the concatenated index list.
            int cursor = 0;
            for (int i = 0; i < 12; i++)
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
        }

        private void BuildWater(WorldData world)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float y = WorldData.WaterLevel + 0.12f;

            // River ribbon.
            var path = world.RiverPath;
            const float halfWidth = 3.5f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                V2 p0 = path[i], p1 = path[i + 1];
                float dx = p1.X - p0.X, dz = p1.Z - p0.Z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f) continue;
                float nx = -dz / len * halfWidth, nz = dx / len * halfWidth;
                int b = verts.Count;
                verts.Add(new Vector3(p0.X + nx, y, p0.Z + nz)); // L0
                verts.Add(new Vector3(p0.X - nx, y, p0.Z - nz)); // R0
                verts.Add(new Vector3(p1.X + nx, y, p1.Z + nz)); // L1
                verts.Add(new Vector3(p1.X - nx, y, p1.Z - nz)); // R1
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

            _waterMat = MaterialFactory.NewLitEmissiveInstance(WaterDay, Color.black, 0.85f);

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
            MaterialFactory.SetEmission(_waterMat, glow ? WaterGlow * 0.9f : Color.black);
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }
    }
}
