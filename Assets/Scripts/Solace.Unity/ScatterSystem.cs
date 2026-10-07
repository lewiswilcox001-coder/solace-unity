// Solace.Unity — instanced vegetation, rocks, glow-moss and glowberries.
//
// Placement is deterministic (SeededRandom.Derive(seed, "unity-scatter")) and
// driven by biome density. Instances are grouped into grid cells; each frame
// only cells within the cull distance are drawn via Graphics.DrawMeshInstanced
// in ≤512-instance batches. Zero per-frame allocations: all matrices are
// precomputed once.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Solace.Core;

namespace Solace.Unity
{
    public class ScatterSystem : MonoBehaviour
    {
        private const float CellSize = 32f;
        private const int BatchLimit = 512;
        private const float CullDistance = 260f;

        private struct Variant
        {
            public Mesh Mesh;
            public Material Material;
            public Vector3[] CellCenters;
            public Matrix4x4[][][] CellBatches; // per cell: one array per ≤512 chunk
            public int CellCount;
        }

        private readonly List<Variant> _variants = new List<Variant>();
        private Camera _cam;
        private float _cullDistSq;

        // Night-flower glow: animated by time of day (dim by day, luminous at night).
        private Material _flowerVioletMat;
        private Material _flowerCyanMat;
        private static readonly Color FlowerVioletEmission = new Color(0.38f, 0.16f, 0.80f);
        private static readonly Color FlowerCyanEmission = new Color(0.15f, 0.60f, 0.75f);
        private float _flowerGlow;

        public void Build(WorldData world)
        {
            _cam = GameBootstrap.Instance.Camera.GetComponent<Camera>();
            _cullDistSq = CullDistance * CullDistance;

            var rng = SeededRandom.Derive(world.Seed, "unity-scatter");
            int grid = Mathf.CeilToInt(world.SizeMeters / CellSize);

            // Per-variant, per-cell instance lists.
            var trunk = NewCellLists(grid);
            var crown = NewCellLists(grid);
            var rock = NewCellLists(grid);
            var grass = NewCellLists(grid);
            var grass2 = NewCellLists(grid);
            var moss = NewCellLists(grid);
            var bush = NewCellLists(grid);
            var berry = NewCellLists(grid);
            var reedStalk = NewCellLists(grid);
            var reedHead = NewCellLists(grid);
            var flowerStem = NewCellLists(grid);
            var flowerViolet = NewCellLists(grid);
            var flowerCyan = NewCellLists(grid);

            PointOfInterest den = null;
            foreach (var p in world.Pois)
                if (p.Type == PoiType.Den) den = p;

            int attempts = grid * grid * 46;
            for (int a = 0; a < attempts; a++)
            {
                float x = (rng.NextFloat() * 2f - 1f) * world.HalfSize;
                float z = (rng.NextFloat() * 2f - 1f) * world.HalfSize;
                float h = world.SampleHeight(x, z);
                if (h < WorldData.WaterLevel + 0.5f) continue;
                if (world.SlopeAt(x, z) > 1.1f) continue;
                if (den != null)
                {
                    float dx = x - den.X, dz = z - den.Z;
                    if (dx * dx + dz * dz < (den.Radius + 6f) * (den.Radius + 6f)) continue;
                }
                Biome biome = world.GetBiome(x, z);
                int cell = CellIndex(x, z, world, grid);

                float roll = rng.NextFloat();
                bool nearWater = h < WorldData.WaterLevel + 1.8f;
                switch (biome)
                {
                    case Biome.Foxpine:
                        if (roll < 0.30f) AddTree(trunk, crown, cell, x, h, z, rng);
                        else if (roll < 0.39f) AddInstance(moss, cell, x, h, z, rng, 0.8f, 1.6f);
                        else if (roll < 0.46f) AddInstance(grass, cell, x, h, z, rng, 0.7f, 1.3f);
                        else if (roll < 0.52f) AddInstance(grass2, cell, x, h, z, rng, 0.7f, 1.3f);
                        else if (roll < 0.56f) AddBerryBush(bush, berry, cell, x, h, z, rng);
                        else if (roll < 0.585f) AddNightFlower(flowerStem, flowerViolet, flowerCyan, cell, x, h, z, rng);
                        else if (nearWater && roll < 0.62f) AddReeds(reedStalk, reedHead, cell, x, h, z, rng);
                        break;
                    case Biome.Mistmoor:
                        if (roll < 0.26f) AddInstance(grass, cell, x, h, z, rng, 0.8f, 1.6f);
                        else if (roll < 0.32f) AddInstance(grass2, cell, x, h, z, rng, 0.8f, 1.5f);
                        else if (roll < 0.39f) AddInstance(rock, cell, x, h, z, rng, 0.5f, 1.4f);
                        else if (roll < 0.43f) AddInstance(moss, cell, x, h, z, rng, 0.6f, 1.2f);
                        else if (roll < 0.46f) AddBerryBush(bush, berry, cell, x, h, z, rng);
                        else if (roll < 0.485f) AddNightFlower(flowerStem, flowerViolet, flowerCyan, cell, x, h, z, rng);
                        else if (nearWater && roll < 0.52f) AddReeds(reedStalk, reedHead, cell, x, h, z, rng);
                        break;
                    case Biome.DenGrounds:
                        if (roll < 0.17f) AddInstance(grass, cell, x, h, z, rng, 0.7f, 1.2f);
                        else if (roll < 0.22f) AddInstance(grass2, cell, x, h, z, rng, 0.7f, 1.2f);
                        else if (roll < 0.27f) AddInstance(moss, cell, x, h, z, rng, 0.8f, 1.5f);
                        else if (roll < 0.29f) AddNightFlower(flowerStem, flowerViolet, flowerCyan, cell, x, h, z, rng);
                        break;
                    case Biome.FellCrag:
                        if (roll < 0.30f) AddInstance(rock, cell, x, h, z, rng, 0.7f, 2.2f);
                        break;
                    case Biome.Riverbank:
                        if (roll < 0.22f) AddInstance(grass, cell, x, h, z, rng, 0.7f, 1.2f);
                        else if (roll < 0.29f) AddInstance(moss, cell, x, h, z, rng, 0.7f, 1.4f);
                        else if (nearWater && roll < 0.44f) AddReeds(reedStalk, reedHead, cell, x, h, z, rng);
                        else if (roll < 0.47f) AddNightFlower(flowerStem, flowerViolet, flowerCyan, cell, x, h, z, rng);
                        break;
                    case Biome.SnowPeak:
                        if (roll < 0.12f) AddInstance(rock, cell, x, h, z, rng, 0.8f, 1.8f);
                        break;
                }
            }

            AddVariant(MeshFactory.GetPrimitive(PrimitiveType.Cylinder),
                       MaterialFactory.Lit(new Color(0.23f, 0.15f, 0.10f), 0.25f), trunk, world, grid);
            AddVariant(MeshFactory.PineCrown(),
                       MaterialFactory.Lit(new Color(0.10f, 0.24f, 0.17f), 0.20f), crown, world, grid);
            AddVariant(MeshFactory.FacetedRock(),
                       MaterialFactory.Lit(new Color(0.38f, 0.38f, 0.41f), 0.35f), rock, world, grid);
            AddVariant(MeshFactory.GrassTuft(),
                       MaterialFactory.Lit(new Color(0.30f, 0.42f, 0.21f), 0.25f), grass, world, grid);
            AddVariant(BuildTallGrass(),
                       MaterialFactory.Lit(new Color(0.33f, 0.44f, 0.19f), 0.25f), grass2, world, grid);
            AddVariant(BuildReedStalks(),
                       MaterialFactory.Lit(new Color(0.30f, 0.38f, 0.20f), 0.30f), reedStalk, world, grid);
            AddVariant(BuildReedHeads(),
                       MaterialFactory.Lit(new Color(0.32f, 0.22f, 0.13f), 0.40f), reedHead, world, grid);
            AddVariant(MeshFactory.Cone(0.05f, 0.7f, 4),
                       MaterialFactory.Lit(new Color(0.07f, 0.14f, 0.10f), 0.30f), flowerStem, world, grid);
            // Night flowers: unique color keys so these cached materials are
            // effectively dedicated — Update() animates their emission.
            _flowerVioletMat = MaterialFactory.LitEmissive(new Color(0.10f, 0.04f, 0.16f),
                                                           FlowerVioletEmission, 0.4f);
            AddVariant(MeshFactory.FacetedRock(), _flowerVioletMat, flowerViolet, world, grid);
            _flowerCyanMat = MaterialFactory.LitEmissive(new Color(0.04f, 0.10f, 0.14f),
                                                         FlowerCyanEmission, 0.4f);
            AddVariant(MeshFactory.FacetedRock(), _flowerCyanMat, flowerCyan, world, grid);
            AddVariant(MeshFactory.Disc(1.0f, 9),
                       MaterialFactory.LitEmissive(new Color(0.10f, 0.35f, 0.30f),
                                                   new Color(0.10f, 0.55f, 0.48f), 0.6f), moss, world, grid);
            AddVariant(MeshFactory.GetPrimitive(PrimitiveType.Sphere),
                       MaterialFactory.Lit(new Color(0.10f, 0.25f, 0.12f), 0.25f), bush, world, grid);
            AddVariant(MeshFactory.GetPrimitive(PrimitiveType.Sphere),
                       MaterialFactory.LitEmissive(new Color(0.45f, 0.08f, 0.08f),
                                                   new Color(0.85f, 0.15f, 0.10f), 0.4f), berry, world, grid);
        }

        private void Update()
        {
            if (_cam == null || _variants.Count == 0) return;

            // Night flowers breathe: dim violet/cyan buds by day, luminous at night.
            var boot = GameBootstrap.Instance;
            float target = 0.12f;
            if (boot != null && boot.Sim != null && boot.Sim.State.IsNight) target = 1f;
            _flowerGlow = Mathf.Lerp(_flowerGlow, target, 1f - Mathf.Exp(-1.5f * Time.deltaTime));
            float glowI = 0.25f + 1.6f * _flowerGlow;
            MaterialFactory.SetEmission(_flowerVioletMat, FlowerVioletEmission * glowI);
            MaterialFactory.SetEmission(_flowerCyanMat, FlowerCyanEmission * glowI);

            Vector3 camPos = _cam.transform.position;
            for (int v = 0; v < _variants.Count; v++)
            {
                Variant vd = _variants[v];
                for (int c = 0; c < vd.CellCount; c++)
                {
                    if ((vd.CellCenters[c] - camPos).sqrMagnitude > _cullDistSq) continue;
                    Matrix4x4[][] batches = vd.CellBatches[c];
                    if (batches == null) continue;
                    for (int b = 0; b < batches.Length; b++)
                    {
                        Matrix4x4[] mats = batches[b];
                        MaterialFactory.DrawInstanced(vd.Mesh, vd.Material, mats, -1,
                            ShadowCastingMode.Off, false);
                    }
                }
            }
        }

        // -- build helpers ------------------------------------------------------

        private static List<Matrix4x4>[] NewCellLists(int grid)
        {
            var lists = new List<Matrix4x4>[grid * grid];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<Matrix4x4>();
            return lists;
        }

        private static int CellIndex(float x, float z, WorldData world, int grid)
        {
            int cx = Mathf.Clamp((int)((x + world.HalfSize) / CellSize), 0, grid - 1);
            int cz = Mathf.Clamp((int)((z + world.HalfSize) / CellSize), 0, grid - 1);
            return cx + cz * grid;
        }

        private static void AddInstance(List<Matrix4x4>[] lists, int cell,
                                        float x, float y, float z, SeededRandom rng,
                                        float minS, float maxS)
        {
            float s = rng.NextFloat(minS, maxS);
            float rot = rng.NextFloat(0f, Mathf.PI * 2f);
            lists[cell].Add(Matrix4x4.TRS(new Vector3(x, y - 0.05f, z),
                Quaternion.Euler(0f, rot * Mathf.Rad2Deg, 0f),
                new Vector3(s, s * rng.NextFloat(0.8f, 1.2f), s)));
        }

        private static void AddTree(List<Matrix4x4>[] trunk, List<Matrix4x4>[] crown,
                                    int cell, float x, float y, float z, SeededRandom rng)
        {
            float s = rng.NextFloat(0.8f, 1.5f);
            float rot = rng.NextFloat(0f, Mathf.PI * 2f);
            var q = Quaternion.Euler(0f, rot * Mathf.Rad2Deg, 0f);
            var pos = new Vector3(x, y - 0.1f, z);
            trunk[cell].Add(Matrix4x4.TRS(pos, q, new Vector3(s, s, s)));
            crown[cell].Add(Matrix4x4.TRS(pos, q, new Vector3(s, s, s)));
        }

        private static void AddBerryBush(List<Matrix4x4>[] bush, List<Matrix4x4>[] berry,
                                         int cell, float x, float y, float z, SeededRandom rng)
        {
            float s = rng.NextFloat(0.8f, 1.3f);
            bush[cell].Add(Matrix4x4.TRS(new Vector3(x, y + 0.55f * s, z),
                Quaternion.identity, new Vector3(s, s * 0.85f, s)));
            int dots = rng.NextInt(3, 7);
            for (int i = 0; i < dots; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(0.4f, 0.95f) * s;
                berry[cell].Add(Matrix4x4.TRS(
                    new Vector3(x + Mathf.Cos(a) * r, y + rng.NextFloat(0.3f, 1.0f) * s, z + Mathf.Sin(a) * r),
                    Quaternion.identity, Vector3.one * rng.NextFloat(0.8f, 1.3f)));
            }
        }

        // A cattail reed stand: 3 stalks with matching brown heads. The stalk
        // and head merges share identical offsets so heads sit on stalk tips.
        private static void AddReeds(List<Matrix4x4>[] stalks, List<Matrix4x4>[] heads,
                                     int cell, float x, float y, float z, SeededRandom rng)
        {
            float s = rng.NextFloat(0.8f, 1.3f);
            float rot = rng.NextFloat(0f, Mathf.PI * 2f);
            var q = Quaternion.Euler(0f, rot * Mathf.Rad2Deg, 0f);
            var base_ = new Vector3(x, y - 0.05f, z);
            stalks[cell].Add(Matrix4x4.TRS(base_, q, new Vector3(s, s, s)));
            // Heads ride the stalk tips in the same local frame (baked into the mesh).
            heads[cell].Add(Matrix4x4.TRS(base_, q, new Vector3(s, s, s)));
        }

        // A night flower: dark stem + one glowing crystal blossom (violet/cyan).
        private static void AddNightFlower(List<Matrix4x4>[] stems, List<Matrix4x4>[] violet,
                                           List<Matrix4x4>[] cyan,
                                           int cell, float x, float y, float z, SeededRandom rng)
        {
            float s = rng.NextFloat(0.7f, 1.2f);
            float rot = rng.NextFloat(0f, Mathf.PI * 2f);
            var q = Quaternion.Euler(0f, rot * Mathf.Rad2Deg, 0f);
            stems[cell].Add(Matrix4x4.TRS(new Vector3(x, y - 0.02f, z), q,
                new Vector3(s, s, s)));
            // Blossom: small faceted crystal hovering just above the stem tip.
            var head = Matrix4x4.TRS(new Vector3(x, y + 0.72f * s, z), q,
                Vector3.one * 0.22f * s);
            if (rng.NextFloat() < 0.5f) violet[cell].Add(head);
            else cyan[cell].Add(head);
        }

        // -- local mesh builders (composed from public MeshFactory parts) --------

        private static Mesh _tallGrass;
        private static Mesh _reedStalks;
        private static Mesh _reedHeads;

        /// <summary>Taller, thinner grass: five blades fanning outward.</summary>
        private static Mesh BuildTallGrass()
        {
            if (_tallGrass != null) return _tallGrass;
            var blade = MeshFactory.Cone(0.09f, 1.5f, 4);
            var meshes = new List<Mesh> { blade, blade, blade, blade, blade };
            var xforms = new List<Matrix4x4>
            {
                Matrix4x4.TRS(new Vector3(0f, 0f, 0f), Quaternion.Euler(0f, 0f, 6f), Vector3.one),
                Matrix4x4.TRS(new Vector3(0.18f, 0f, 0.06f), Quaternion.Euler(-8f, 0f, -12f), Vector3.one),
                Matrix4x4.TRS(new Vector3(-0.16f, 0f, 0.10f), Quaternion.Euler(12f, 0f, 6f), new Vector3(1f, 0.85f, 1f)),
                Matrix4x4.TRS(new Vector3(0.05f, 0f, -0.16f), Quaternion.Euler(6f, 40f, -6f), new Vector3(0.9f, 1.1f, 0.9f)),
                Matrix4x4.TRS(new Vector3(-0.06f, 0f, -0.05f), Quaternion.Euler(-4f, 200f, 10f), new Vector3(0.85f, 0.75f, 0.85f)),
            };
            _tallGrass = MeshFactory.Merge(meshes, xforms);
            return _tallGrass;
        }

        /// <summary>
        /// Reed stalks and heads share one layout: three tilted stalks fanning
        /// from a base point, each brown cattail head computed at its stalk's
        /// tilted tip so heads sit exactly on stalk ends.
        /// </summary>
        private static void BuildReeds()
        {
            if (_reedStalks != null) return;
            var stalk = MeshFactory.Cone(0.07f, 2.4f, 4);
            var head = MeshFactory.Cone(0.16f, 0.55f, 5);
            var offs = new Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0.28f, 0f, 0.10f),
                new Vector3(-0.22f, 0f, 0.18f),
            };
            var tilts = new Quaternion[]
            {
                Quaternion.Euler(0f, 0f, 4f),
                Quaternion.Euler(-5f, 0f, -9f),
                Quaternion.Euler(7f, 0f, 6f),
            };
            var stalkMeshes = new List<Mesh>();
            var stalkXf = new List<Matrix4x4>();
            var headMeshes = new List<Mesh>();
            var headXf = new List<Matrix4x4>();
            var flip = Quaternion.Euler(180f, 0f, 0f); // cattail: cone point-down
            for (int i = 0; i < 3; i++)
            {
                stalkMeshes.Add(stalk);
                stalkXf.Add(Matrix4x4.TRS(offs[i], tilts[i], Vector3.one));
                // Head rides the tilted stalk tip, tucked 0.1 into it.
                Vector3 tip = offs[i] + (tilts[i] * new Vector3(0f, 2.3f, 0f));
                headMeshes.Add(head);
                headXf.Add(Matrix4x4.TRS(tip, tilts[i] * flip, Vector3.one));
            }
            _reedStalks = MeshFactory.Merge(stalkMeshes, stalkXf);
            _reedHeads = MeshFactory.Merge(headMeshes, headXf);
        }

        private static Mesh BuildReedStalks() { BuildReeds(); return _reedStalks; }
        private static Mesh BuildReedHeads() { BuildReeds(); return _reedHeads; }

        private void AddVariant(Mesh mesh, Material mat, List<Matrix4x4>[] lists,
                                WorldData world, int grid)
        {
            var centers = new Vector3[grid * grid];
            var batches = new Matrix4x4[grid * grid][][];
            for (int cz = 0; cz < grid; cz++)
                for (int cx = 0; cx < grid; cx++)
                {
                    int i = cx + cz * grid;
                    centers[i] = new Vector3(-world.HalfSize + (cx + 0.5f) * CellSize, 0f,
                                             -world.HalfSize + (cz + 0.5f) * CellSize);
                    List<Matrix4x4> list = lists[i];
                    if (list.Count == 0) continue;
                    int chunks = (list.Count + BatchLimit - 1) / BatchLimit;
                    var arr = new Matrix4x4[chunks][];
                    for (int c = 0; c < chunks; c++)
                    {
                        int from = c * BatchLimit;
                        int n = Mathf.Min(BatchLimit, list.Count - from);
                        arr[c] = new Matrix4x4[n];
                        for (int k = 0; k < n; k++) arr[c][k] = list[from + k];
                    }
                    batches[i] = arr;
                }
            _variants.Add(new Variant
            {
                Mesh = mesh, Material = mat,
                CellCenters = centers, CellBatches = batches, CellCount = grid * grid
            });
        }
    }
}
