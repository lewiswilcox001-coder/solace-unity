// Solace.Unity — structures at points of interest, built from POI data.
//
// Den: earth dome + dark entrance + glow-moss. EmberHollow: stone ring + warm
// point light + rising ember particles. InsectileRuin: chitin arches — curved,
// tapered, alien; explicitly NOT human architecture. Cairn: piled stones +
// a tall scent-stone. RuinSite: husk circle. Overlook: stone seat.
// GlowberryBush: a hero bush with emissive berries.
//
// Light budget: exactly one point light here (the den's ember-hollow); the
// fox's chest-core owns the only other point light. Sun + moon are the two
// directionals. Total pixel lights ≤ 4 by construction.
using System.Collections.Generic;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class DenBuilder : MonoBehaviour
    {
        private static readonly Color Earth = new Color(0.30f, 0.22f, 0.14f);
        private static readonly Color Dark = new Color(0.03f, 0.025f, 0.03f);
        private static readonly Color Stone = new Color(0.45f, 0.44f, 0.46f);
        private static readonly Color Chitin = new Color(0.10f, 0.06f, 0.14f);
        private static readonly Color MossGlow = new Color(0.12f, 0.60f, 0.52f);

        private bool _emberLightPlaced;

        public void Build(WorldData world)
        {
            foreach (var poi in world.Pois)
            {
                float y = world.SampleHeight(poi.X, poi.Z);
                var root = new GameObject("POI_" + poi.Type + "_" + poi.Id);
                root.transform.SetParent(transform, false);
                root.transform.position = new Vector3(poi.X, y, poi.Z);
                switch (poi.Type)
                {
                    case PoiType.Den: BuildDen(root, poi); break;
                    case PoiType.EmberHollow: BuildEmberHollow(root, poi); break;
                    case PoiType.InsectileRuin: BuildHive(root, poi); break;
                    case PoiType.Cairn: BuildCairn(root, poi); break;
                    case PoiType.RuinSite: BuildRuinSite(root, poi); break;
                    case PoiType.Overlook: BuildOverlook(root, poi); break;
                    case PoiType.GlowberryBush: BuildHeroBush(root, poi); break;
                    case PoiType.CrystalCave: BuildCrystalCave(root, poi); break;
                    case PoiType.HotSpring: BuildHotSpring(root, poi); break;
                    case PoiType.HollowLog: BuildHollowLog(root, poi); break;
                    case PoiType.RainbowGrove: BuildRainbowGrove(root, poi); break;
                }
            }
        }

        // -- den -----------------------------------------------------------------

        private void BuildDen(GameObject root, PointOfInterest poi)
        {
            var earth = MaterialFactory.Lit(Earth, 0.2f);
            var moss = MaterialFactory.LitEmissive(new Color(0.10f, 0.30f, 0.22f), MossGlow * 0.7f, 0.7f);
            var dark = MaterialFactory.Lit(Dark, 0.1f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);

            // Earth dome.
            MeshFactory.AddMesh(root, "Dome", sphere, earth,
                new Vector3(0f, -1.2f, 0f), new Vector3(9f, 4.5f, 8f), Quaternion.identity);
            // Dark entrance: a half-buried black dome facing outward.
            MeshFactory.AddMesh(root, "Entrance", sphere, dark,
                new Vector3(0f, 0.1f, 4.6f), new Vector3(3.2f, 2.4f, 2.0f), Quaternion.identity);
            // Glow-moss patches around the mouth.
            var rng = SeededRandom.Derive(poi.Id * 7919 + 13, "unity-den");
            for (int i = 0; i < 7; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(4f, 8f);
                MeshFactory.AddMesh(root, "Moss" + i, MeshFactory.Disc(1f, 8), moss,
                    new Vector3(Mathf.Cos(a) * r, 0.15f, Mathf.Sin(a) * r),
                    Vector3.one * rng.NextFloat(0.7f, 1.6f), Quaternion.identity);
            }
        }

        // -- ember hollow ----------------------------------------------------------

        private void BuildEmberHollow(GameObject root, PointOfInterest poi)
        {
            var stone = MaterialFactory.Lit(Stone, 0.35f);
            var ember = MaterialFactory.LitEmissive(new Color(0.55f, 0.20f, 0.05f),
                                                     new Color(1.0f, 0.42f, 0.10f), 0.5f);
            var moss = MaterialFactory.LitEmissive(new Color(0.10f, 0.30f, 0.22f), MossGlow * 0.7f, 0.7f);
            var rockMesh = MeshFactory.FacetedRock();
            var rng = SeededRandom.Derive(poi.Id * 7919 + 71, "unity-ember");

            // Ring of stones.
            int n = 9;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                float r = 2.6f;
                MeshFactory.AddMesh(root, "Stone" + i, rockMesh, stone,
                    new Vector3(Mathf.Cos(a) * r, 0.35f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.7f, 1.1f), rng.NextFloat(0.5f, 0.8f), rng.NextFloat(0.7f, 1.1f)),
                    Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
            }
            // Glowing ember bed.
            MeshFactory.AddMesh(root, "EmberBed", MeshFactory.Disc(2.0f, 12), ember,
                new Vector3(0f, 0.12f, 0f), Vector3.one, Quaternion.identity);
            // Glow-moss fringe.
            for (int i = 0; i < 5; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(3.2f, 5f);
                MeshFactory.AddMesh(root, "Moss" + i, MeshFactory.Disc(1f, 8), moss,
                    new Vector3(Mathf.Cos(a) * r, 0.12f, Mathf.Sin(a) * r),
                    Vector3.one * rng.NextFloat(0.6f, 1.2f), Quaternion.identity);
            }

            BuildEmberParticles(root);

            if (!_emberLightPlaced)
            {
                _emberLightPlaced = true;
                var lightGO = new GameObject("EmberLight");
                lightGO.transform.SetParent(root.transform, false);
                lightGO.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.55f, 0.25f);
                light.intensity = 6f;
                light.range = 20f;
                light.shadows = LightShadows.None;
            }
        }

        private void BuildEmberParticles(GameObject root)
        {
            var go = new GameObject("Embers");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f));
            main.gravityModifier = -0.15f;
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(14f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 1.6f;
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            psr.material = MaterialFactory.LitEmissive(new Color(0.6f, 0.2f, 0.05f),
                                                       new Color(1f, 0.45f, 0.1f), 0.4f);
        }

        // -- the hollow hive (insectile ruin) ---------------------------------------

        private void BuildHive(GameObject root, PointOfInterest poi)
        {
            var chitin = MaterialFactory.Lit(Chitin, 0.35f);
            var moss = MaterialFactory.LitEmissive(new Color(0.10f, 0.30f, 0.22f), MossGlow * 0.5f, 0.7f);
            var rng = SeededRandom.Derive(poi.Id * 7919 + 131, "unity-hive");

            // Chitin arches: curved tapered tusk-segments along arcs, leaning
            // at alien angles. Nothing here is straight, square, or human.
            int arches = 4;
            for (int a = 0; a < arches; a++)
            {
                float baseA = rng.NextFloat(0f, Mathf.PI * 2f);
                float radius = rng.NextFloat(7f, 13f);
                float lean = rng.NextFloat(-0.35f, 0.35f);
                var arch = new GameObject("Arch" + a);
                arch.transform.SetParent(root.transform, false);
                arch.transform.localPosition = new Vector3(Mathf.Cos(baseA) * radius * 0.4f, 0f,
                                                           Mathf.Sin(baseA) * radius * 0.4f);
                arch.transform.localRotation = Quaternion.Euler(0f, rng.NextFloat(0f, 360f), lean * 57f);
                int segs = 7;
                for (int s = 0; s < segs; s++)
                {
                    float t = s / (float)(segs - 1);
                    float ang = Mathf.PI * (0.08f + 0.84f * t);
                    float rr = radius * (0.55f + 0.45f * Mathf.Sin(t * Mathf.PI));
                    float segR = 1.6f * (1f - t * 0.75f);
                    float segH = 4.2f * (1f - t * 0.55f);
                    var pos = new Vector3(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr * 0.9f + 1f,
                                          (rng.NextFloat() - 0.5f) * 2f);
                    // Orient along the arc tangent.
                    var tangent = new Vector3(-Mathf.Sin(ang), Mathf.Cos(ang) * 0.9f, 0f);
                    var q = Quaternion.LookRotation(tangent, Vector3.up);
                    MeshFactory.AddMesh(arch, "Seg" + s, MeshFactory.Cone(segR, segH, 6), chitin,
                        pos - tangent * segH * 0.5f, Vector3.one, q * Quaternion.Euler(90f, 0f, 0f));
                }
            }
            // Fallen husk shards.
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            for (int i = 0; i < 10; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(4f, 18f);
                MeshFactory.AddMesh(root, "Shard" + i, sphere, chitin,
                    new Vector3(Mathf.Cos(a) * r, 0.2f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.5f, 1.4f), rng.NextFloat(0.3f, 0.7f), rng.NextFloat(0.5f, 1.2f)),
                    Quaternion.Euler(rng.NextFloat(0f, 30f), rng.NextFloat(0f, 360f), 0f));
            }
            // A little glow-moss reclaiming the edges.
            for (int i = 0; i < 6; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(14f, 22f);
                MeshFactory.AddMesh(root, "Moss" + i, MeshFactory.Disc(1f, 8), moss,
                    new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r),
                    Vector3.one * rng.NextFloat(0.6f, 1.4f), Quaternion.identity);
            }
        }

        // -- cairn / ruin site / overlook / glowberry ---------------------------------

        private void BuildCairn(GameObject root, PointOfInterest poi)
        {
            var stone = MaterialFactory.Lit(Stone, 0.35f);
            var rockMesh = MeshFactory.FacetedRock();
            var rng = SeededRandom.Derive(poi.Id * 7919 + 197, "unity-cairn");
            // Piled stones.
            float y = 0f;
            for (int i = 0; i < 6; i++)
            {
                float s = 1.3f - i * 0.16f;
                MeshFactory.AddMesh(root, "Pile" + i, rockMesh, stone,
                    new Vector3((rng.NextFloat() - 0.5f) * 0.3f, y + s * 0.45f, (rng.NextFloat() - 0.5f) * 0.3f),
                    new Vector3(s, s * 0.7f, s), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
                y += s * 0.55f;
            }
            // Tall scent-stone beside it.
            MeshFactory.AddMesh(root, "ScentStone", MeshFactory.Cone(0.8f, 3.4f, 6), stone,
                new Vector3(2.2f, 0f, 0.6f), Vector3.one, Quaternion.Euler(0f, 0f, 6f));
        }

        private void BuildRuinSite(GameObject root, PointOfInterest poi)
        {
            var chitin = MaterialFactory.Lit(Chitin, 0.5f);
            var rng = SeededRandom.Derive(poi.Id * 7919 + 263, "unity-ruin");
            // A circle of old husks.
            int n = 8;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + rng.NextFloat(-0.1f, 0.1f);
                float r = 4.5f;
                MeshFactory.AddMesh(root, "Husk" + i, MeshFactory.Cone(rng.NextFloat(0.5f, 0.9f),
                    rng.NextFloat(1.2f, 2.4f), 5), chitin,
                    new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    Vector3.one, Quaternion.Euler(rng.NextFloat(-12f, 12f), rng.NextFloat(0f, 360f), 0f));
            }
        }

        private void BuildOverlook(GameObject root, PointOfInterest poi)
        {
            var stone = MaterialFactory.Lit(Stone, 0.35f);
            // A simple stone seat facing the vale.
            MeshFactory.AddMesh(root, "Seat", MeshFactory.GetPrimitive(PrimitiveType.Cube), stone,
                new Vector3(0f, 0.5f, 0f), new Vector3(2.2f, 1.0f, 1.2f), Quaternion.identity);
            MeshFactory.AddMesh(root, "SeatBack", MeshFactory.GetPrimitive(PrimitiveType.Cube), stone,
                new Vector3(0f, 1.4f, -0.7f), new Vector3(2.2f, 1.4f, 0.5f), Quaternion.Euler(-8f, 0f, 0f));
        }

        private void BuildHeroBush(GameObject root, PointOfInterest poi)
        {
            var leaf = MaterialFactory.Lit(new Color(0.10f, 0.28f, 0.13f), 0.25f);
            var berryMat = MaterialFactory.LitEmissive(new Color(0.45f, 0.08f, 0.08f),
                                                        new Color(0.9f, 0.16f, 0.10f), 0.4f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            var rng = SeededRandom.Derive(poi.Id * 7919 + 331, "unity-bush");
            MeshFactory.AddMesh(root, "Bush", sphere, leaf,
                new Vector3(0f, 1.0f, 0f), new Vector3(2.4f, 2.0f, 2.4f), Quaternion.identity);
            for (int i = 0; i < 14; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float e = rng.NextFloat(-0.5f, 1f);
                float r = 1.15f;
                MeshFactory.AddMesh(root, "Berry" + i, sphere, berryMat,
                    new Vector3(Mathf.Cos(a) * r, 1.0f + e, Mathf.Sin(a) * r),
                    Vector3.one * 0.28f, Quaternion.identity);
            }
        }

        // -- crystal cave ------------------------------------------------------------

        private void BuildCrystalCave(GameObject root, PointOfInterest poi)
        {
            // Living glass: faceted shards rising from a stone mouth, glowing
            // faint blue-violet. The emissive reads as night-glow when the sun dies.
            var stone = MaterialFactory.Lit(Stone, 0.4f);
            var crystalA = MaterialFactory.LitEmissive(new Color(0.35f, 0.55f, 0.75f),
                                                        new Color(0.25f, 0.55f, 0.95f) * 0.9f, 0.15f);
            var crystalB = MaterialFactory.LitEmissive(new Color(0.55f, 0.40f, 0.70f),
                                                        new Color(0.55f, 0.35f, 0.90f) * 0.8f, 0.15f);
            var rockMesh = MeshFactory.FacetedRock();
            var rng = SeededRandom.Derive(poi.Id * 7919 + 397, "unity-crystal");

            // Low stone lip around the mouth.
            int n = 10;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                float r = 4.2f;
                // Leave a gap at the front for entry.
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 28f) continue;
                MeshFactory.AddMesh(root, "Lip" + i, rockMesh, stone,
                    new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.8f, 1.4f), rng.NextFloat(0.5f, 0.9f), rng.NextFloat(0.8f, 1.4f)),
                    Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
            }
            // Crystal shards: stretched octahedrons, clustered, leaning outward.
            int shards = 14;
            for (int i = 0; i < shards; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(0.5f, 3.6f);
                float hgt = rng.NextFloat(1.2f, 4.2f);
                var mat = rng.NextFloat() < 0.6f ? crystalA : crystalB;
                float lean = rng.NextFloat(-14f, 14f);
                MeshFactory.AddMesh(root, "Shard" + i, rockMesh, mat,
                    new Vector3(Mathf.Cos(a) * r, hgt * 0.32f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.35f, 0.7f), hgt * 0.55f, rng.NextFloat(0.35f, 0.7f)),
                    Quaternion.Euler(lean, rng.NextFloat(0f, 360f), rng.NextFloat(-10f, 10f)));
            }
            // A few small ground crystals scattered around the mouth.
            for (int i = 0; i < 8; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(4.5f, 8f);
                var mat = rng.NextFloat() < 0.5f ? crystalA : crystalB;
                MeshFactory.AddMesh(root, "Peb" + i, MeshFactory.Cone(0.28f, rng.NextFloat(0.5f, 1.1f), 6), mat,
                    new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    Vector3.one, Quaternion.Euler(rng.NextFloat(-8f, 8f), rng.NextFloat(0f, 360f), 0f));
            }
        }

        // -- hot spring --------------------------------------------------------------

        private void BuildHotSpring(GameObject root, PointOfInterest poi)
        {
            var stone = MaterialFactory.Lit(Stone, 0.35f);
            var water = MaterialFactory.LitEmissive(new Color(0.15f, 0.45f, 0.50f),
                                                     new Color(0.10f, 0.35f, 0.38f) * 0.6f, 0.25f);
            var rockMesh = MeshFactory.FacetedRock();
            var rng = SeededRandom.Derive(poi.Id * 7919 + 463, "unity-spring");

            // Ring of smooth stones.
            int n = 11;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                float r = 3.1f;
                MeshFactory.AddMesh(root, "Rim" + i, rockMesh, stone,
                    new Vector3(Mathf.Cos(a) * r, 0.25f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.7f, 1.2f), rng.NextFloat(0.4f, 0.7f), rng.NextFloat(0.7f, 1.2f)),
                    Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
            }
            // The pool itself: warm, faintly glowing water.
            MeshFactory.AddMesh(root, "Pool", MeshFactory.Disc(2.9f, 16), water,
                new Vector3(0f, 0.18f, 0f), Vector3.one, Quaternion.identity);
            // Steam: soft white motes rising.
            var go = new GameObject("Steam");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.95f, 0.96f));
            main.gravityModifier = -0.1f;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(8f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 2.0f;
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            psr.material = MaterialFactory.Lit(new Color(0.90f, 0.93f, 0.94f), 0.9f);
        }

        // -- hollow log ---------------------------------------------------------------

        private void BuildHollowLog(GameObject root, PointOfInterest poi)
        {
            var bark = MaterialFactory.Lit(new Color(0.28f, 0.18f, 0.10f), 0.5f);
            var dark = MaterialFactory.Lit(new Color(0.02f, 0.015f, 0.015f), 0.1f);
            var moss = MaterialFactory.LitEmissive(new Color(0.10f, 0.30f, 0.22f), MossGlow * 0.5f, 0.7f);
            var rng = SeededRandom.Derive(poi.Id * 7919 + 529, "unity-log");

            // The fallen trunk: a long cylinder lying on its side.
            float yaw = rng.NextFloat(0f, 360f);
            var logGO = new GameObject("Trunk");
            logGO.transform.SetParent(root.transform, false);
            logGO.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            MeshFactory.AddMesh(logGO, "Trunk", MeshFactory.GetPrimitive(PrimitiveType.Cylinder), bark,
                new Vector3(0f, 0.9f, 0f), new Vector3(1.8f, 7f, 1.8f),
                Quaternion.Euler(0f, 0f, 90f));
            // Dark hollow mouth at one end.
            var mouthDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            MeshFactory.AddMesh(root, "Mouth", MeshFactory.GetPrimitive(PrimitiveType.Sphere), dark,
                mouthDir * 3.4f + new Vector3(0f, 0.85f, 0f),
                new Vector3(1.1f, 1.1f, 0.6f), Quaternion.identity);
            // Moss patches on top.
            for (int i = 0; i < 5; i++)
            {
                float t = rng.NextFloat(-2.6f, 2.6f);
                var off = Quaternion.Euler(0f, yaw, 0f) * new Vector3(t, 1.75f, rng.NextFloat(-0.5f, 0.5f));
                MeshFactory.AddMesh(root, "Moss" + i, MeshFactory.Disc(0.7f, 7), moss,
                    off, Vector3.one * rng.NextFloat(0.7f, 1.3f), Quaternion.identity);
            }
            // Broken branch stubs.
            for (int i = 0; i < 3; i++)
            {
                float t = rng.NextFloat(-2f, 2f);
                var off = Quaternion.Euler(0f, yaw, 0f) * new Vector3(t, 1.4f, 0f);
                MeshFactory.AddMesh(root, "Stub" + i, MeshFactory.Cone(0.22f, rng.NextFloat(0.8f, 1.6f), 5), bark,
                    off, Vector3.one,
                    Quaternion.Euler(rng.NextFloat(20f, 50f), rng.NextFloat(0f, 360f), 0f));
            }
        }

        private void BuildRainbowGrove(GameObject root, PointOfInterest poi)
        {
            // SECRET: the hidden rainbow grove. A circle of tall crystals, each
            // a different color of the rainbow, humming with light. This should
            // stop Lewis in his tracks when he finds it.
            var rng = SeededRandom.Derive(poi.Id * 7919 + 977, "unity-rainbow");
            var rockMesh = MeshFactory.FacetedRock();
            // Mossy stone ring at the base.
            var mossStone = MaterialFactory.Lit(new Color(0.25f, 0.35f, 0.22f), 0.5f);
            for (int i = 0; i < 12; i++)
            {
                float a = (i / 12f) * Mathf.PI * 2f;
                float r = 7.5f;
                MeshFactory.AddMesh(root, "Ring" + i, rockMesh, mossStone,
                    new Vector3(Mathf.Cos(a) * r, 0.25f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.9f, 1.5f), rng.NextFloat(0.4f, 0.7f), rng.NextFloat(0.9f, 1.5f)),
                    Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f));
            }
            // The rainbow: seven tall crystals, one per color, in spectral order.
            Color[] spectral = new Color[]
            {
                new Color(0.95f, 0.25f, 0.25f), // red
                new Color(0.95f, 0.55f, 0.20f), // orange
                new Color(0.95f, 0.85f, 0.25f), // yellow
                new Color(0.35f, 0.85f, 0.35f), // green
                new Color(0.30f, 0.60f, 0.95f), // blue
                new Color(0.45f, 0.35f, 0.85f), // indigo
                new Color(0.75f, 0.40f, 0.90f), // violet
            };
            for (int i = 0; i < 7; i++)
            {
                float a = (i / 7f) * Mathf.PI * 2f + 0.22f;
                float r = 4.2f;
                Color c = spectral[i];
                var mat = MaterialFactory.LitEmissive(c * 0.75f, c * 1.4f, 0.12f);
                float hgt = rng.NextFloat(3.5f, 6.5f);
                float lean = rng.NextFloat(-10f, 10f);
                MeshFactory.AddMesh(root, "Prism" + i, rockMesh, mat,
                    new Vector3(Mathf.Cos(a) * r, hgt * 0.38f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.55f, 0.85f), hgt * 0.62f, rng.NextFloat(0.55f, 0.85f)),
                    Quaternion.Euler(lean, rng.NextFloat(0f, 360f), rng.NextFloat(-8f, 8f)));
            }
            // Center crystal: the heart — white-gold, tallest, the source.
            var heartMat = MaterialFactory.LitEmissive(
                new Color(1f, 0.97f, 0.88f), new Color(1f, 0.92f, 0.75f) * 1.8f, 0.1f);
            MeshFactory.AddMesh(root, "Heart", rockMesh, heartMat,
                new Vector3(0f, 3.2f, 0f),
                new Vector3(1.1f, 8.5f, 1.1f),
                Quaternion.Euler(4f, 30f, -3f));
            // Scattered small rainbow shards on the moss.
            for (int i = 0; i < 16; i++)
            {
                float a = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(1.5f, 9f);
                Color c = spectral[rng.NextInt(0, 7)];
                var mat = MaterialFactory.LitEmissive(c * 0.7f, c * 1.1f, 0.15f);
                float hgt = rng.NextFloat(0.4f, 1.4f);
                MeshFactory.AddMesh(root, "Shard" + i, rockMesh, mat,
                    new Vector3(Mathf.Cos(a) * r, hgt * 0.3f, Mathf.Sin(a) * r),
                    new Vector3(rng.NextFloat(0.2f, 0.4f), hgt * 0.6f, rng.NextFloat(0.2f, 0.4f)),
                    Quaternion.Euler(rng.NextFloat(-15f, 15f), rng.NextFloat(0f, 360f), 0f));
            }
        }
    }
}
