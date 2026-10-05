// Solace.Core — deterministic world generation.
//
// Setting: a misty highland glen. A river winds the length of the valley and
// widens into a still loch; heather moorland climbs to pinewoods, then fell
// crag and snow. A small hamlet keeps peat fires lit. On the high fell stands
// an ancient broch — a stone tower no one living built. Cairns mark the old
// trails, bothy fires shelter travellers, berries ripen by the water.
//
// Everything here is generated from the world-gen RNG stream only, so the
// same seed always builds the same glen on every platform.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    /// <summary>Biome roles. Each has a clear gameplay affordance.</summary>
    public enum Biome
    {
        Moorland,      // open heather — foraging, hares, easy travel
        Pinewood,      // forest — shelter, deer, berries, wolves
        FellCrag,      // rocky high ground — views, danger, the broch
        SnowPeak,      // extreme high — harsh, rarely worth the climb
        Riverbank,     // shore and shallow water — drinking, fishing stories
        HamletGrounds  // settlement — people, hearths, safety
    }

    public enum PoiType
    {
        Hamlet,      // the social hearth: crofts, peat fires, villagers
        BrochRuin,   // ancient stone tower on the fell — the great mystery
        Cairn,       // waystone cairn at a trail fork
        Campfire,    // traveller camp / bothy fire — rest and shelter
        BerryBush,   // harvestable food (Stock = berries remaining)
        RuinSite,    // lesser ruins: standing stones, old shieling
        Overlook     // high viewpoint over the glen
    }

    /// <summary>A named place in the world. Discovery state is per-life.</summary>
    public class PointOfInterest
    {
        public int Id;
        public PoiType Type;
        public string Name;        // true name; hidden until LearnedName
        public float X;
        public float Z;
        public float Radius;       // arrival / interaction radius, meters
        public bool Discovered;    // Solace has been here / seen it clearly
        public bool LearnedName;   // Solace knows its true name
        public int Stock;          // berry bushes: berries remaining
        public bool Looted;        // ruin sites / broch: already searched

        public string DisplayName
        {
            get
            {
                if (LearnedName) return Name;
                switch (Type)
                {
                    case PoiType.Hamlet: return "the hamlet";
                    case PoiType.BrochRuin: return "the old tower on the fell";
                    case PoiType.Cairn: return "a piled cairn";
                    case PoiType.Campfire: return "a campfire site";
                    case PoiType.BerryBush: return "a berry bush";
                    case PoiType.RuinSite: return "some old stones";
                    case PoiType.Overlook: return "a high viewpoint";
                    default: return "an unfamiliar place";
                }
            }
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("type", Type.ToString());
            o.Add("name", Name);
            o.Add("x", X);
            o.Add("z", Z);
            o.Add("radius", Radius);
            o.Add("discovered", Discovered);
            o.Add("learnedName", LearnedName);
            o.Add("stock", Stock);
            o.Add("looted", Looted);
            return o;
        }

        public static PointOfInterest FromJson(JsonObject o)
        {
            var p = new PointOfInterest();
            p.Id = JsonHelpers.GetInt(o, "id", 0);
            p.Type = (PoiType)Enum.Parse(typeof(PoiType), JsonHelpers.GetString(o, "type", "Cairn"));
            p.Name = JsonHelpers.GetString(o, "name", "?");
            p.X = JsonHelpers.GetFloat(o, "x", 0f);
            p.Z = JsonHelpers.GetFloat(o, "z", 0f);
            p.Radius = JsonHelpers.GetFloat(o, "radius", 5f);
            p.Discovered = JsonHelpers.GetBool(o, "discovered", false);
            p.LearnedName = JsonHelpers.GetBool(o, "learnedName", false);
            p.Stock = JsonHelpers.GetInt(o, "stock", 0);
            p.Looted = JsonHelpers.GetBool(o, "looted", false);
            return p;
        }
    }

    public class WorldConfig
    {
        public int Seed;
        public float SizeMeters = 512f;
        public float CellSize = 4f;
    }

    /// <summary>
    /// The generated glen: heightmap, moisture, biomes, river, trails, POIs.
    /// Fully serializable; the save stores it verbatim.
    /// </summary>
    public class WorldData
    {
        public const float WaterLevel = 0.4f;

        public int Seed;
        public float SizeMeters;
        public float CellSize;
        public int Resolution;
        public float[] Heights;
        public float[] Moisture;
        public Biome[] Biomes;
        public List<V2> RiverPath = new List<V2>();
        public List<V2> TrailPath = new List<V2>();
        public List<PointOfInterest> Pois = new List<PointOfInterest>();
        public V2 SpawnPoint;
        public V2 LakeCenter;
        public float LakeRadius;
        public string ValleyName = "";
        public string HamletName = "";
        public string BrochName = "";

        public float HalfSize { get { return SizeMeters * 0.5f; } }

        private int ClampIx(float x)
        {
            int ix = (int)Math.Floor((x + HalfSize) / CellSize);
            return MathX.Clamp(ix, 0, Resolution - 1);
        }

        private float CellX(int ix) { return (ix + 0.5f) * CellSize - HalfSize; }

        /// <summary>Bilinear height sample in meters.</summary>
        public float SampleHeight(float x, float z)
        {
            float fx = (x + HalfSize) / CellSize - 0.5f;
            float fz = (z + HalfSize) / CellSize - 0.5f;
            int x0 = MathX.Clamp((int)Math.Floor(fx), 0, Resolution - 2);
            int z0 = MathX.Clamp((int)Math.Floor(fz), 0, Resolution - 2);
            float tx = MathX.Clamp01(fx - x0);
            float tz = MathX.Clamp01(fz - z0);
            float h00 = Heights[x0 + z0 * Resolution];
            float h10 = Heights[(x0 + 1) + z0 * Resolution];
            float h01 = Heights[x0 + (z0 + 1) * Resolution];
            float h11 = Heights[(x0 + 1) + (z0 + 1) * Resolution];
            return MathX.Lerp(MathX.Lerp(h00, h10, tx), MathX.Lerp(h01, h11, tx), tz);
        }

        public float SampleMoisture(float x, float z)
        {
            int ix = ClampIx(x), iz = ClampIx(z);
            return Moisture[ix + iz * Resolution];
        }

        public Biome GetBiome(float x, float z)
        {
            int ix = ClampIx(x), iz = ClampIx(z);
            return Biomes[ix + iz * Resolution];
        }

        public bool IsWater(float x, float z) { return SampleHeight(x, z) < WaterLevel; }

        /// <summary>Ground slope (rise over run) via finite differences.</summary>
        public float SlopeAt(float x, float z)
        {
            float e = CellSize;
            float dx = (SampleHeight(x + e, z) - SampleHeight(x - e, z)) / (2f * e);
            float dz = (SampleHeight(x, z + e) - SampleHeight(x, z - e)) / (2f * e);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Nearest point on the river or loch shore — for drinking.</summary>
        public V2 NearestWaterPoint(float x, float z)
        {
            V2 best = LakeCenter;
            float bestD = V2.DistanceSq(new V2(x, z), LakeCenter);
            for (int i = 0; i < RiverPath.Count; i++)
            {
                float d = V2.DistanceSq(new V2(x, z), RiverPath[i]);
                if (d < bestD) { bestD = d; best = RiverPath[i]; }
            }
            return best;
        }

        public PointOfInterest GetPoi(int id)
        {
            for (int i = 0; i < Pois.Count; i++)
                if (Pois[i].Id == id) return Pois[i];
            return null;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("seed", Seed);
            o.Add("sizeMeters", SizeMeters);
            o.Add("cellSize", CellSize);
            o.Add("resolution", Resolution);
            var ha = new JsonArray();
            for (int i = 0; i < Heights.Length; i++) ha.Add(Heights[i]);
            o.Add("heights", ha);
            var ma = new JsonArray();
            for (int i = 0; i < Moisture.Length; i++) ma.Add(Moisture[i]);
            o.Add("moisture", ma);
            var ba = new JsonArray();
            for (int i = 0; i < Biomes.Length; i++) ba.Add((int)Biomes[i]);
            o.Add("biomes", ba);
            o.Add("river", V2ListToJson(RiverPath));
            o.Add("trail", V2ListToJson(TrailPath));
            var pa = new JsonArray();
            for (int i = 0; i < Pois.Count; i++) pa.Add(Pois[i].ToJson());
            o.Add("pois", pa);
            o.Add("spawn", V2ToJson(SpawnPoint));
            o.Add("lake", V2ToJson(LakeCenter));
            o.Add("lakeRadius", LakeRadius);
            o.Add("valleyName", ValleyName);
            o.Add("hamletName", HamletName);
            o.Add("brochName", BrochName);
            return o;
        }

        public static JsonObject V2ToJson(V2 v)
        {
            var o = new JsonObject();
            o.Add("x", v.X);
            o.Add("z", v.Z);
            return o;
        }

        public static V2 V2FromJson(JsonObject o)
        {
            return new V2(JsonHelpers.GetFloat(o, "x", 0f), JsonHelpers.GetFloat(o, "z", 0f));
        }

        public static JsonArray V2ListToJson(List<V2> list)
        {
            var a = new JsonArray();
            for (int i = 0; i < list.Count; i++) a.Add(V2ToJson(list[i]));
            return a;
        }

        public static List<V2> V2ListFromJson(JsonArray a)
        {
            var list = new List<V2>(a.Count);
            for (int i = 0; i < a.Count; i++) list.Add(V2FromJson(a[i].AsObject()));
            return list;
        }

        public static WorldData FromJson(JsonObject o)
        {
            var w = new WorldData();
            w.Seed = JsonHelpers.GetInt(o, "seed", 0);
            w.SizeMeters = JsonHelpers.GetFloat(o, "sizeMeters", 512f);
            w.CellSize = JsonHelpers.GetFloat(o, "cellSize", 4f);
            w.Resolution = JsonHelpers.GetInt(o, "resolution", 128);
            var ha = o["heights"].AsArray();
            w.Heights = new float[ha.Count];
            for (int i = 0; i < ha.Count; i++) w.Heights[i] = ((JsonNumber)ha[i]).AsFloat();
            var ma = o["moisture"].AsArray();
            w.Moisture = new float[ma.Count];
            for (int i = 0; i < ma.Count; i++) w.Moisture[i] = ((JsonNumber)ma[i]).AsFloat();
            var ba = o["biomes"].AsArray();
            w.Biomes = new Biome[ba.Count];
            for (int i = 0; i < ba.Count; i++) w.Biomes[i] = (Biome)((JsonNumber)ba[i]).AsInt();
            w.RiverPath = V2ListFromJson(o["river"].AsArray());
            w.TrailPath = V2ListFromJson(o["trail"].AsArray());
            var pa = o["pois"].AsArray();
            w.Pois = new List<PointOfInterest>(pa.Count);
            for (int i = 0; i < pa.Count; i++) w.Pois.Add(PointOfInterest.FromJson(pa[i].AsObject()));
            w.SpawnPoint = V2FromJson(o["spawn"].AsObject());
            w.LakeCenter = V2FromJson(o["lake"].AsObject());
            w.LakeRadius = JsonHelpers.GetFloat(o, "lakeRadius", 20f);
            w.ValleyName = JsonHelpers.GetString(o, "valleyName", "");
            w.HamletName = JsonHelpers.GetString(o, "hamletName", "");
            w.BrochName = JsonHelpers.GetString(o, "brochName", "");
            return w;
        }
    }

    /// <summary>
    /// Builds the glen. Uses ONLY the world-gen RNG stream (SeededRandom.Derive
    /// with domain "world") plus deterministic noise — never any other stream.
    /// </summary>
    public static class WorldGenerator
    {
        // Authored name pools — place identity is picked deterministically.
        private static readonly string[] ValleyNames =
            { "Glen Vane", "Glen Morven", "Strath Aline", "Glen Dubh", "Glen Halladale", "Srath Mor" };
        private static readonly string[] HamletNames =
            { "Dunvane", "Kilmory", "Auchterlin", "Balnamoon", "Craigduff", "Tomnavin" };
        private static readonly string[] BrochNames =
            { "the Broch of Cairston", "Dun Athad", "the Grey Tower", "Caisteal Dubh", "Dun Mor" };
        private static readonly string[] RuinNames =
            { "the Clachan Stones", "the old shieling", "the Shepherd's Cairn", "the drowned croft" };

        public static WorldData Generate(WorldConfig config)
        {
            _nextPoiId = 0; // ids are per-world; reset so the same seed → same ids
            var rng = SeededRandom.Derive(config.Seed, "world");
            uint noiseSeed = (uint)config.Seed * 0x9E3779B1u + 0x85EBCA6Bu;

            var w = new WorldData();
            w.Seed = config.Seed;
            w.SizeMeters = config.SizeMeters;
            w.CellSize = config.CellSize;
            w.Resolution = Math.Max(8, (int)(config.SizeMeters / config.CellSize));
            int res = w.Resolution;
            float half = w.HalfSize;

            w.ValleyName = rng.Pick(ValleyNames);
            w.HamletName = rng.Pick(HamletNames);
            w.BrochName = rng.Pick(BrochNames);

            w.Heights = new float[res * res];
            w.Moisture = new float[res * res];
            w.Biomes = new Biome[res * res];

            // 1. Base terrain: glen floor between rising fells, enclosed at the ends.
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float x = (ix + 0.5f) * w.CellSize - half;
                    float z = (iz + 0.5f) * w.CellSize - half;
                    float nx = x / half;
                    float nz = z / half;

                    float across = nx * nx;                       // fells rise east/west
                    float along = nz * nz * nz * nz * 0.35f;      // gentler enclosure north/south
                    float rim = MathX.Clamp01(across + along);
                    float rimH = rim * rim * 56f;

                    float hills = (Noise.Fbm(x * 0.008f + 31.7f, z * 0.008f - 11.2f, 4, 2.03f, 0.5f, noiseSeed) - 0.5f) * 20f;
                    float detail = (Noise.Value(x * 0.05f, z * 0.05f, noiseSeed ^ 0x1234ABCDu) - 0.5f) * 2.5f;
                    float crag = Noise.Ridged(x * 0.012f - 5.1f, z * 0.012f + 8.8f, 3, 2.1f, 0.55f, noiseSeed ^ 0x77AA55CCu)
                                 * rim * 14f;

                    w.Heights[ix + iz * res] = 8f + rimH + hills * (0.35f + 0.65f * rim) + detail + crag;
                    w.Moisture[ix + iz * res] = Noise.Fbm(x * 0.006f + 100.3f, z * 0.006f - 40.7f, 3, 2.0f, 0.5f, noiseSeed ^ 0x5F3759DFu);
                }
            }

            // 2. River spline: meanders north → south, widening into a loch.
            int ctrlCount = 7;
            var ctrl = new List<V2>(ctrlCount);
            for (int i = 0; i < ctrlCount; i++)
            {
                float z = -half + i * (w.SizeMeters / (ctrlCount - 1)); // spans [-half, half]
                float x = rng.NextFloat(-0.26f, 0.26f) * half;
                ctrl.Add(new V2(x, z));
            }
            w.RiverPath = SampleCatmullRom(ctrl, 64);

            // Loch: widen the river at ~55% of its length.
            int lakeIdx = (int)(w.RiverPath.Count * 0.55f);
            w.LakeCenter = w.RiverPath[lakeIdx];
            w.LakeRadius = rng.NextFloat(20f, 30f);

            CarveRiver(w);

            // 3. Hamlet: flat ground near the river, upper third of the glen.
            PointOfInterest hamlet = PlaceHamlet(w, rng);
            w.Pois.Add(hamlet);

            // 4. Broch: high fell, far from the hamlet.
            PointOfInterest broch = PlaceBroch(w, rng, hamlet);
            w.Pois.Add(broch);

            // 5. Trails: spawn → hamlet → (cairn fork) → broch.
            V2 fork = BuildTrails(w, hamlet, broch);
            CarveTrail(w);

            // 6. Cairn at the trail fork.
            PointOfInterest cairn = PlaceCairn(w, fork);
            w.Pois.Add(cairn);

            // 7. Campfires: hamlet edge, loch shore, bothy site on the fell trail.
            foreach (var cf in PlaceCampfires(w, rng, hamlet))
                w.Pois.Add(cf);

            // 8. Berry bushes.
            foreach (var b in PlaceBerryBushes(w, rng, hamlet))
                w.Pois.Add(b);

            // 9. Ruin sites: standing stones + old shieling.
            foreach (var r in PlaceRuinSites(w, rng, hamlet, broch))
                w.Pois.Add(r);

            // 10. Overlook.
            w.Pois.Add(PlaceOverlook(w, rng, hamlet));

            // 11. Spawn: at the hamlet edge, on dry land.
            w.SpawnPoint = FindLandNear(w, hamlet.X + 14f, hamlet.Z + 8f);

            // 12. Biomes.
            AssignBiomes(w, hamlet);

            // 13. Ensure spawn + POI ground is sane (not water, not cliff).
            foreach (var p in w.Pois)
            {
                V2 fixed_ = FindLandNear(w, p.X, p.Z);
                p.X = fixed_.X; p.Z = fixed_.Z;
            }
            w.SpawnPoint = FindLandNear(w, w.SpawnPoint.X, w.SpawnPoint.Z);

            return w;
        }

        // ---- terrain carving -------------------------------------------------

        private static void CarveRiver(WorldData w)
        {
            int res = w.Resolution;
            float half = w.HalfSize;
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float x = (ix + 0.5f) * w.CellSize - half;
                    float z = (iz + 0.5f) * w.CellSize - half;
                    float dRiver = DistToPolyline(x, z, w.RiverPath);
                    float dLake = MathF.Sqrt((x - w.LakeCenter.X) * (x - w.LakeCenter.X)
                                           + (z - w.LakeCenter.Z) * (z - w.LakeCenter.Z)) - w.LakeRadius;

                    float h = w.Heights[ix + iz * res];
                    if (dRiver < 9f)
                    {
                        float bed = -2.2f;
                        float t = MathX.SmoothStep((dRiver - 3.5f) / 5.5f);
                        h = MathX.Lerp(bed, h, t);
                    }
                    if (dLake < 9f)
                    {
                        float t = MathX.SmoothStep(dLake / 9f);
                        h = MathX.Lerp(-3.2f, h, t);
                    }
                    w.Heights[ix + iz * res] = h;
                }
            }
        }

        private static float DistToPolyline(float x, float z, List<V2> path)
        {
            float best = float.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                V2 a = path[i], b = path[i + 1];
                float abx = b.X - a.X, abz = b.Z - a.Z;
                float lenSq = abx * abx + abz * abz;
                float t = lenSq > 1e-8f ? ((x - a.X) * abx + (z - a.Z) * abz) / lenSq : 0f;
                t = MathX.Clamp01(t);
                float px = a.X + abx * t - x, pz = a.Z + abz * t - z;
                float d = px * px + pz * pz;
                if (d < best) best = d;
            }
            return MathF.Sqrt(best);
        }

        private static List<V2> SampleCatmullRom(List<V2> ctrl, int samples)
        {
            var pts = new List<V2>(samples + 1);
            int n = ctrl.Count;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples * (n - 1);
                int i = Math.Min((int)Math.Floor(t), n - 2);
                float f = t - i;
                V2 p0 = ctrl[Math.Max(0, i - 1)];
                V2 p1 = ctrl[i];
                V2 p2 = ctrl[i + 1];
                V2 p3 = ctrl[Math.Min(n - 1, i + 2)];
                float f2 = f * f, f3 = f2 * f;
                float x = 0.5f * (2f * p1.X + (-p0.X + p2.X) * f + (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * f2
                                 + (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * f3);
                float z = 0.5f * (2f * p1.Z + (-p0.Z + p2.Z) * f + (2f * p0.Z - 5f * p1.Z + 4f * p2.Z - p3.Z) * f2
                                 + (-p0.Z + 3f * p1.Z - 3f * p2.Z + p3.Z) * f3);
                pts.Add(new V2(x, z));
            }
            return pts;
        }

        // ---- POI placement ----------------------------------------------------

        private static int _nextPoiId = 0; // reset per Generate call

        private static PointOfInterest NewPoi(PoiType type, string name, float x, float z, float radius)
        {
            return new PointOfInterest
            {
                Id = _nextPoiId++,
                Type = type,
                Name = name,
                X = x,
                Z = z,
                Radius = radius,
                Discovered = false,
                LearnedName = false,
                Stock = 0,
                Looted = false
            };
        }

        private static PointOfInterest PlaceHamlet(WorldData w, SeededRandom rng)
        {
            // Near the river, upper-middle glen. Score candidates by flatness.
            float bestScore = float.MinValue;
            float bx = 0f, bz = 0f;
            for (int t = 0; t < 28; t++)
            {
                int ri = rng.NextInt((int)(w.RiverPath.Count * 0.25f), (int)(w.RiverPath.Count * 0.48f));
                V2 rp = w.RiverPath[ri];
                float ang = rng.NextFloat(0f, MathF.PI * 2f);
                float dist = rng.NextFloat(16f, 42f);
                float x = rp.X + MathF.Cos(ang) * dist;
                float z = rp.Z + MathF.Sin(ang) * dist;
                if (Math.Abs(x) > w.HalfSize - 30f || Math.Abs(z) > w.HalfSize - 30f) continue;
                float h = w.SampleHeight(x, z);
                if (h < WorldData.WaterLevel + 1.5f) continue;
                float slope = w.SlopeAt(x, z);
                float score = -slope * 10f - Math.Abs(h - 10f) * 0.05f + rng.NextFloat(0f, 0.5f);
                if (score > bestScore) { bestScore = score; bx = x; bz = z; }
            }
            var p = NewPoi(PoiType.Hamlet, w.HamletName, bx, bz, 26f);
            p.Discovered = true;   // home is known
            p.LearnedName = true;
            return p;
        }

        private static PointOfInterest PlaceBroch(WorldData w, SeededRandom rng, PointOfInterest hamlet)
        {
            float bestH = float.MinValue;
            float bx = 0f, bz = 0f;
            for (int t = 0; t < 60; t++)
            {
                float x = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float z = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float dx = x - hamlet.X, dz = z - hamlet.Z;
                if (dx * dx + dz * dz < 200f * 200f) continue;
                float h = w.SampleHeight(x, z);
                if (h < 30f) continue;
                if (h > bestH) { bestH = h; bx = x; bz = z; }
            }
            var p = NewPoi(PoiType.BrochRuin, w.BrochName, bx, bz, 16f);
            return p;
        }

        private static V2 BuildTrails(WorldData w, PointOfInterest hamlet, PointOfInterest broch)
        {
            // Trail: spawn-side → hamlet → fork → broch. Gentle switchback feel
            // via midpoint offsets away from water and steep ground.
            var a = new V2(hamlet.X + 14f, hamlet.Z + 8f);
            var b = new V2(hamlet.X, hamlet.Z);
            var fork = V2.Lerp(b, new V2(broch.X, broch.Z), 0.35f);
            var c = new V2(broch.X, broch.Z);

            var ctrl = new List<V2>();
            ctrl.Add(a);
            ctrl.Add(V2.Lerp(a, b, 0.5f));
            ctrl.Add(b);
            ctrl.Add(V2.Lerp(b, fork, 0.5f));
            ctrl.Add(fork);
            ctrl.Add(V2.Lerp(fork, c, 0.5f));
            ctrl.Add(c);

            // Nudge midpoints off water / cliffs deterministically.
            for (int i = 1; i < ctrl.Count - 1; i++)
            {
                V2 p = ctrl[i];
                if (w.IsWater(p.X, p.Z) || w.SlopeAt(p.X, p.Z) > 0.8f)
                {
                    V2 best = p;
                    float bestCost = float.MaxValue;
                    for (int k = 0; k < 8; k++)
                    {
                        float ang = k / 8f * MathF.PI * 2f;
                        V2 q = new V2(p.X + MathF.Cos(ang) * 10f, p.Z + MathF.Sin(ang) * 10f);
                        float cost = (w.IsWater(q.X, q.Z) ? 100f : 0f) + w.SlopeAt(q.X, q.Z);
                        if (cost < bestCost) { bestCost = cost; best = q; }
                    }
                    ctrl[i] = best;
                }
            }

            w.TrailPath = SampleCatmullRom(ctrl, 72);
            return fork;
        }

        private static void CarveTrail(WorldData w)
        {
            int res = w.Resolution;
            float half = w.HalfSize;
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float x = (ix + 0.5f) * w.CellSize - half;
                    float z = (iz + 0.5f) * w.CellSize - half;
                    // Cheap check: only near-trail cells matter; sample every 4th path point.
                    float best = float.MaxValue;
                    for (int i = 0; i < w.TrailPath.Count; i += 3)
                    {
                        float dx = x - w.TrailPath[i].X, dz = z - w.TrailPath[i].Z;
                        float d = dx * dx + dz * dz;
                        if (d < best) best = d;
                    }
                    best = MathF.Sqrt(best);
                    if (best < 2.2f && w.Heights[ix + iz * res] > WorldData.WaterLevel + 0.5f)
                        w.Heights[ix + iz * res] -= 0.35f * (1f - best / 2.2f);
                }
            }
        }

        private static PointOfInterest PlaceCairn(WorldData w, V2 fork)
        {
            V2 p = FindLandNear(w, fork.X + 6f, fork.Z + 4f);
            return NewPoi(PoiType.Cairn, "the fork cairn", p.X, p.Z, 6f);
        }

        private static List<PointOfInterest> PlaceCampfires(WorldData w, SeededRandom rng, PointOfInterest hamlet)
        {
            var list = new List<PointOfInterest>();
            // Traveller camp at the hamlet edge.
            V2 p1 = FindLandNear(w, hamlet.X - 30f, hamlet.Z + 12f);
            list.Add(NewPoi(PoiType.Campfire, "the traveller's fire", p1.X, p1.Z, 7f));
            // Loch shore bothy.
            float ang = rng.NextFloat(0f, MathF.PI * 2f);
            V2 p2 = FindLandNear(w, w.LakeCenter.X + MathF.Cos(ang) * (w.LakeRadius + 10f),
                                    w.LakeCenter.Z + MathF.Sin(ang) * (w.LakeRadius + 10f));
            list.Add(NewPoi(PoiType.Campfire, "the lochside bothy", p2.X, p2.Z, 7f));
            // High bothy on the fell trail.
            int mid = w.TrailPath.Count * 3 / 4;
            V2 tp = w.TrailPath[mid];
            V2 p3 = FindLandNear(w, tp.X + 8f, tp.Z - 6f);
            list.Add(NewPoi(PoiType.Campfire, "the high bothy", p3.X, p3.Z, 7f));
            return list;
        }

        private static List<PointOfInterest> PlaceBerryBushes(WorldData w, SeededRandom rng, PointOfInterest hamlet)
        {
            var list = new List<PointOfInterest>();
            int target = rng.NextInt(9, 13);
            int guard = 0;
            while (list.Count < target && guard++ < 400)
            {
                float x = rng.NextFloat(-w.HalfSize + 25f, w.HalfSize - 25f);
                float z = rng.NextFloat(-w.HalfSize + 25f, w.HalfSize - 25f);
                float h = w.SampleHeight(x, z);
                if (h < WorldData.WaterLevel + 0.8f || h > 30f) continue;
                if (w.SlopeAt(x, z) > 0.45f) continue;
                float m = w.SampleMoisture(x, z);
                if (m < 0.5f) continue;
                float dx = x - hamlet.X, dz = z - hamlet.Z;
                if (dx * dx + dz * dz > 230f * 230f) continue;
                bool tooClose = false;
                for (int i = 0; i < list.Count; i++)
                {
                    float ddx = x - list[i].X, ddz = z - list[i].Z;
                    if (ddx * ddx + ddz * ddz < 25f * 25f) { tooClose = true; break; }
                }
                if (tooClose) continue;
                var b = NewPoi(PoiType.BerryBush, "berry bush", x, z, 4f);
                b.Stock = rng.NextInt(3, 9);
                list.Add(b);
            }
            return list;
        }

        private static List<PointOfInterest> PlaceRuinSites(WorldData w, SeededRandom rng, PointOfInterest hamlet, PointOfInterest broch)
        {
            var list = new List<PointOfInterest>();
            // Standing stones on open moor.
            float best = float.MinValue;
            float sx = 0f, sz = 0f;
            for (int t = 0; t < 60; t++)
            {
                float x = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float z = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float h = w.SampleHeight(x, z);
                if (h < 9f || h > 26f) continue;
                if (w.SlopeAt(x, z) > 0.35f) continue;
                float dx = x - hamlet.X, dz = z - hamlet.Z;
                float dh = dx * dx + dz * dz;
                if (dh < 90f * 90f) continue;
                float score = -Math.Abs(h - 16f) + rng.NextFloat(0f, 4f);
                if (score > best) { best = score; sx = x; sz = z; }
            }
            string n1 = rng.Pick(RuinNames);
            list.Add(NewPoi(PoiType.RuinSite, n1, sx, sz, 10f));

            // Old shieling hut, closer to the hamlet trail.
            float bx = 0f, bz = 0f; best = float.MinValue;
            for (int t = 0; t < 60; t++)
            {
                float x = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float z = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float h = w.SampleHeight(x, z);
                if (h < WorldData.WaterLevel + 1.2f || h > 24f) continue;
                if (w.SlopeAt(x, z) > 0.4f) continue;
                float dx = x - hamlet.X, dz = z - hamlet.Z;
                float dh = dx * dx + dz * dz;
                if (dh < 60f * 60f || dh > 200f * 200f) continue;
                float score = -w.SlopeAt(x, z) * 8f + rng.NextFloat(0f, 2f);
                if (score > best) { best = score; bx = x; bz = z; }
            }
            string n2 = rng.Pick(RuinNames);
            if (n2 == n1) n2 = "the old shieling";
            list.Add(NewPoi(PoiType.RuinSite, n2, bx, bz, 9f));
            return list;
        }

        private static PointOfInterest PlaceOverlook(WorldData w, SeededRandom rng, PointOfInterest hamlet)
        {
            float best = float.MinValue;
            float ox = 0f, oz = 0f;
            for (int t = 0; t < 60; t++)
            {
                float x = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float z = rng.NextFloat(-w.HalfSize + 40f, w.HalfSize - 40f);
                float h = w.SampleHeight(x, z);
                if (h < 26f) continue;
                float dx = x - hamlet.X, dz = z - hamlet.Z;
                if (dx * dx + dz * dz < 110f * 110f) continue;
                float score = h + rng.NextFloat(0f, 6f);
                if (score > best) { best = score; ox = x; oz = z; }
            }
            return NewPoi(PoiType.Overlook, "the high view", ox, oz, 8f);
        }

        /// <summary>Snaps a point to nearby dry, walkable land (spiral search).</summary>
        private static V2 FindLandNear(WorldData w, float x, float z)
        {
            if (!w.IsWater(x, z) && w.SlopeAt(x, z) < 0.9f) return new V2(x, z);
            for (float r = 4f; r < 60f; r += 4f)
            {
                for (int k = 0; k < 12; k++)
                {
                    float ang = k / 12f * MathF.PI * 2f;
                    float qx = x + MathF.Cos(ang) * r;
                    float qz = z + MathF.Sin(ang) * r;
                    if (Math.Abs(qx) > w.HalfSize - 8f || Math.Abs(qz) > w.HalfSize - 8f) continue;
                    if (!w.IsWater(qx, qz) && w.SlopeAt(qx, qz) < 0.9f)
                        return new V2(qx, qz);
                }
            }
            return new V2(x, z);
        }

        private static void AssignBiomes(WorldData w, PointOfInterest hamlet)
        {
            int res = w.Resolution;
            float half = w.HalfSize;
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float x = (ix + 0.5f) * w.CellSize - half;
                    float z = (iz + 0.5f) * w.CellSize - half;
                    float h = w.Heights[ix + iz * res];
                    float m = w.Moisture[ix + iz * res];

                    float dhx = x - hamlet.X, dhz = z - hamlet.Z;
                    Biome b;
                    if (dhx * dhx + dhz * dhz < 32f * 32f)
                        b = Biome.HamletGrounds;
                    else if (h < WorldData.WaterLevel + 1.1f)
                        b = Biome.Riverbank;
                    else if (h > 46f)
                        b = Biome.SnowPeak;
                    else if (h > 28f)
                        b = Biome.FellCrag;
                    else if (m > 0.56f && h < 30f)
                        b = Biome.Pinewood;
                    else
                        b = Biome.Moorland;

                    // Steep low ground reads as crag.
                    if (b != Biome.HamletGrounds && b != Biome.Riverbank && b != Biome.SnowPeak)
                    {
                        float e = w.CellSize;
                        float sl = Math.Abs(w.SampleHeight(x + e, z) - w.SampleHeight(x - e, z)) / (2f * e)
                                 + Math.Abs(w.SampleHeight(x, z + e) - w.SampleHeight(x, z - e)) / (2f * e);
                        if (sl > 0.85f && h > 14f) b = Biome.FellCrag;
                    }
                    w.Biomes[ix + iz * res] = b;
                }
            }
        }
    }
}
