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
    }
}
