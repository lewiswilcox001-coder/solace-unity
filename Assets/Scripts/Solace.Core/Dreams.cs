// Solace.Core — prophetic dreams: the mind shapes the world.
//
// Dreams occur during sleep. A dream is a "recipe": 2-4 elements from the
// dream vocabulary (which is exactly the world-gen's vocabulary — rivers,
// glow-plants, hollow trees, ruins, mist, and the rest) plus a mood, written
// into the journal in the fox's voice, first-person and impressionistic.
//
// The dream then quietly seeds the world-gen: with some probability a NEW,
// unique place matching the recipe is composed from the ingredients and
// placed at a distant, undiscovered coordinate. It is genuinely new —
// assembled from parts, never a pre-built place.
//
// When the fox later discovers a place whose elements rhyme with a past
// dream (feature overlap, not exact identity), the journal records the
// recognition. Crucially, some dreams never come true: neither the fox nor
// the player knows which dreams are meaningful. Unresolved dreams stay in
// the journal as mysteries.
//
// Dreams can also be seeded by strong memories (recent high-salience
// discoveries lend their elements) — the mind-shapes-world loop:
// mind shapes world, world gives experiences, experiences reshape mind.
//
// Determinism: dream content derives from SeededRandom.Derive(seed,
// "dream:<id>") and the world-seeding from Derive(seed, "dreamplace:<id>");
// the per-rest roll uses the partitioned "dream" stream. Same seed +
// same steps → same dreams, on every platform.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    /// <summary>The dream vocabulary: exactly the world-gen's ingredients.</summary>
    public enum DreamElement
    {
        River,
        Loch,
        NightGlow,
        GlowMoss,
        HollowTree,
        Ruin,
        Cairn,
        Mist,
        Snow,
        Overlook,
        EmberHollow,
        Glowberry,
        TreeWalker,
        SeedIsle
    }

    public enum DreamMood
    {
        Quiet,
        Bright,
        Strange,
        Warm,
        Vast,
        Lonely
    }

    public enum DreamStatus
    {
        Unresolved,  // dreamed; may or may not ever come true
        Fulfilled    // a discovered place rhymed with it
    }

    /// <summary>
    /// The dream vocabulary: first-person fragments in the fox's voice,
    /// mood closings, and the mapping between POI types and dream elements
    /// (used both for memory-seeding and for recognition matching).
    /// </summary>
    public static class DreamVocabulary
    {
        public static string[] Fragments(DreamElement e)
        {
            switch (e)
            {
                case DreamElement.River:
                    return new[] { "the river was singing light", "water running bright uphill", "a river humming to itself" };
                case DreamElement.Loch:
                    return new[] { "still water holding the whole sky", "a loch so calm my reflection stayed behind" };
                case DreamElement.NightGlow:
                    return new[] { "the water lit from underneath, like the night had a pulse", "light moving slow under dark water" };
                case DreamElement.GlowMoss:
                    return new[] { "moss burning soft and green in the dark", "the stones warm with green fire" };
                case DreamElement.HollowTree:
                    return new[] { "a tree with a door of shadow in its side", "I slept inside a tree and it breathed around me" };
                case DreamElement.Ruin:
                    return new[] { "arches of chitin, empty as sky", "husk-circles where something enormous once stood" };
                case DreamElement.Cairn:
                    return new[] { "stones piled by hands I could not see", "a cairn that grew one stone each time I blinked" };
                case DreamElement.Mist:
                    return new[] { "mist that breathed when I breathed", "the mist parting like it knew my name" };
                case DreamElement.Snow:
                    return new[] { "snow that did not melt on my tongue", "white silence all the way up" };
                case DreamElement.Overlook:
                    return new[] { "the whole vale laid out below me like a pelt", "I could see the river's entire thought at once" };
                case DreamElement.EmberHollow:
                    return new[] { "a warm dark that held me like a den", "glow-moss keeping the cold out" };
                case DreamElement.Glowberry:
                    return new[] { "berries lit from within, too bright to eat", "fruit that glowed when I touched it" };
                case DreamElement.TreeWalker:
                    return new[] { "a tree walking, slow as weather", "roots like legs crossing the valley" };
                case DreamElement.SeedIsle:
                    return new[] { "an island drifting with no wind", "a green raft sailing on still water" };
                default:
                    return new[] { "something I could not name" };
            }
        }

        public static string MoodClosing(DreamMood mood, SeededRandom rng)
        {
            switch (mood)
            {
                case DreamMood.Quiet:
                    return rng.Pick(new[] { "and I was not afraid.", "and everything was still, and that was enough." });
                case DreamMood.Bright:
                    return rng.Pick(new[] { "and my chest-light burned brighter to see it.", "and I woke glowing." });
                case DreamMood.Strange:
                    return rng.Pick(new[] { "and I woke unsure whether I had dreamed it or remembered it.", "and something in it was looking back at me." });
                case DreamMood.Warm:
                    return rng.Pick(new[] { "and I felt held, the way the den holds.", "and I did not want to wake." });
                case DreamMood.Vast:
                    return rng.Pick(new[] { "and I felt very small, and glad of it.", "and the sky went on longer than I could follow." });
                case DreamMood.Lonely:
                    return rng.Pick(new[] { "and no one else was there, and that was the dream.", "and I called out, and only the mist answered." });
                default:
                    return "and then I woke.";
            }
        }

        /// <summary>
        /// Structural elements can seed a place; atmospheric ones only flavor
        /// the dream. A dream of pure weather seeds nothing — it stays a
        /// mystery, as it should.
        /// </summary>
        public static bool IsStructural(DreamElement e)
        {
            switch (e)
            {
                case DreamElement.Ruin:
                case DreamElement.Cairn:
                case DreamElement.GlowMoss:
                case DreamElement.HollowTree:
                case DreamElement.EmberHollow:
                case DreamElement.Glowberry:
                case DreamElement.Overlook:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The dream elements a POI type carries, for memory-seeding and recognition.</summary>
        public static List<DreamElement> ElementsFor(PoiType type)
        {
            switch (type)
            {
                case PoiType.Den:
                    return new List<DreamElement> { DreamElement.EmberHollow, DreamElement.GlowMoss };
                case PoiType.InsectileRuin:
                    return new List<DreamElement> { DreamElement.Ruin, DreamElement.Cairn };
                case PoiType.Cairn:
                    return new List<DreamElement> { DreamElement.Cairn, DreamElement.Overlook };
                case PoiType.EmberHollow:
                    return new List<DreamElement> { DreamElement.EmberHollow, DreamElement.GlowMoss };
                case PoiType.GlowberryBush:
                    return new List<DreamElement> { DreamElement.Glowberry, DreamElement.NightGlow };
                case PoiType.RuinSite:
                    return new List<DreamElement> { DreamElement.Ruin, DreamElement.Cairn };
                case PoiType.Overlook:
                    return new List<DreamElement> { DreamElement.Overlook, DreamElement.Mist };
                default:
                    return new List<DreamElement>();
            }
        }

        /// <summary>Which POI type a structural dream element grows into.</summary>
        public static PoiType TypeForElement(DreamElement e)
        {
            switch (e)
            {
                case DreamElement.Ruin: return PoiType.RuinSite;
                case DreamElement.Cairn: return PoiType.Cairn;
                case DreamElement.GlowMoss: return PoiType.EmberHollow;
                case DreamElement.HollowTree: return PoiType.EmberHollow;
                case DreamElement.EmberHollow: return PoiType.EmberHollow;
                case DreamElement.Glowberry: return PoiType.GlowberryBush;
                case DreamElement.Overlook: return PoiType.Overlook;
                default: return PoiType.Cairn;
            }
        }

        /// <summary>Dream-flavored names for seeded places.</summary>
        public static string DreamNameFor(PoiType type, SeededRandom rng)
        {
            switch (type)
            {
                case PoiType.RuinSite:
                    return rng.Pick(new[] { "the sleeping arches", "the husk ring", "the quiet chitin", "the dreamed stones" });
                case PoiType.Cairn:
                    return rng.Pick(new[] { "the singing cairn", "the water-mark stones", "the listening stones" });
                case PoiType.EmberHollow:
                    return rng.Pick(new[] { "the door-tree hollow", "the moss-deep", "the warm dark" });
                case PoiType.GlowberryBush:
                    return rng.Pick(new[] { "the bright berries", "the too-bright bush" });
                case PoiType.Overlook:
                    return rng.Pick(new[] { "the far-seeing place", "the sky's edge" });
                default:
                    return "the dreamed place";
            }
        }

        public static float RadiusFor(PoiType type)
        {
            switch (type)
            {
                case PoiType.RuinSite: return 10f;
                case PoiType.Cairn: return 6f;
                case PoiType.EmberHollow: return 7f;
                case PoiType.GlowberryBush: return 4f;
                case PoiType.Overlook: return 8f;
                default: return 6f;
            }
        }

        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }

    /// <summary>
    /// One dream: the recipe (elements + mood), the journal line, and whether
    /// the world ever rhymed with it. Serialized with the save; the journal
    /// (and therefore the dreams) is part of the multi-generational chronicle.
    /// </summary>
    public class DreamState
    {
        public int Id;
        public float Time;                 // game seconds when dreamed
        public int Generation = 1;         // lineage generation that dreamed it
        public List<DreamElement> Elements = new List<DreamElement>();
        public DreamMood Mood = DreamMood.Quiet;
        public string Text = "";
        public DreamStatus Status = DreamStatus.Unresolved;
        public int SeededPlaceId = -1;     // POI grown from this dream (-1 = never seeded)
        public int ResolvedPlaceId = -1;   // POI whose discovery fulfilled it
        public bool MemorySeeded;          // grown from a strong memory, not the dark

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("time", Time);
            o.Add("generation", Generation);
            var ea = new JsonArray();
            foreach (var e in Elements) ea.Add(e.ToString());
            o.Add("elements", ea);
            o.Add("mood", Mood.ToString());
            o.Add("text", Text);
            o.Add("status", Status.ToString());
            o.Add("seededPlaceId", SeededPlaceId);
            o.Add("resolvedPlaceId", ResolvedPlaceId);
            o.Add("memorySeeded", MemorySeeded);
            return o;
        }

        public static DreamState FromJson(JsonObject o)
        {
            var d = new DreamState();
            d.Id = JsonHelpers.GetInt(o, "id", 0);
            d.Time = JsonHelpers.GetFloat(o, "time", 0f);
            d.Generation = JsonHelpers.GetInt(o, "generation", 1);
            JsonValue ev;
            if (o.TryGet("elements", out ev) && !ev.IsNull)
            {
                var ea = ev.AsArray();
                for (int i = 0; i < ea.Count; i++)
                {
                    try { d.Elements.Add((DreamElement)Enum.Parse(typeof(DreamElement), ea[i].AsString())); }
                    catch (ArgumentException) { /* unknown element: skip */ }
                }
            }
            d.Mood = (DreamMood)Enum.Parse(typeof(DreamMood), JsonHelpers.GetString(o, "mood", "Quiet"));
            d.Text = JsonHelpers.GetString(o, "text", "");
            d.Status = (DreamStatus)Enum.Parse(typeof(DreamStatus), JsonHelpers.GetString(o, "status", "Unresolved"));
            d.SeededPlaceId = JsonHelpers.GetInt(o, "seededPlaceId", -1);
            d.ResolvedPlaceId = JsonHelpers.GetInt(o, "resolvedPlaceId", -1);
            d.MemorySeeded = JsonHelpers.GetBool(o, "memorySeeded", false);
            return d;
        }
    }

    /// <summary>Every dream ever dreamed, across generations. Lives on GameState.</summary>
    public class DreamJournal
    {
        public List<DreamState> Dreams = new List<DreamState>();
        public int NextId = 1;

        public int UnresolvedCount
        {
            get
            {
                int n = 0;
                foreach (var d in Dreams)
                    if (d.Status == DreamStatus.Unresolved) n++;
                return n;
            }
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var da = new JsonArray();
            foreach (var d in Dreams) da.Add(d.ToJson());
            o.Add("dreams", da);
            o.Add("nextId", NextId);
            return o;
        }

        public static DreamJournal FromJson(JsonObject o)
        {
            var j = new DreamJournal();
            JsonValue dv;
            if (o.TryGet("dreams", out dv) && !dv.IsNull)
            {
                var da = dv.AsArray();
                for (int i = 0; i < da.Count; i++)
                    j.Dreams.Add(DreamState.FromJson(da[i].AsObject()));
            }
            j.NextId = JsonHelpers.GetInt(o, "nextId", j.Dreams.Count + 1);
            return j;
        }
    }

    /// <summary>
    /// The dream engine: sleep rolls, recipe composition, world-seeding, and
    /// recognition. Stateless static; all state lives on GameState/AgentState.
    /// </summary>
    public static class DreamSystem
    {
        /// <summary>Chance a rest session produces a dream.</summary>
        public const float DreamChancePerRest = 0.45f;
        /// <summary>Of dreams, the share that seed a real place.</summary>
        public const float SeedChance = 0.5f;
        /// <summary>Minimum game-seconds of rest between dream rolls.</summary>
        public const float MinRestBetweenDreams = 21600f; // 6 game-hours

        /// <summary>
        /// Called every fixed step. While the fox is truly asleep (Rest action,
        /// arrived at shelter), rolls at most one dream per rest session.
        /// </summary>
        public static void TickSleep(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            if (sim.Brain.CurrentActionName != "Rest") return;
            if (string.IsNullOrEmpty(a.CurrentActivity) || !a.CurrentActivity.StartsWith("resting")) return;
            // The -9999 sentinel means "never rolled": the first rest may always dream.
            bool neverRolled = a.LastDreamRolledAt < -9000f;
            if (!neverRolled && sim.Now - a.LastDreamRolledAt < MinRestBetweenDreams) return;
            a.LastDreamRolledAt = sim.Now;
            if (sim.DreamRng.NextFloat() < DreamChancePerRest)
                GenerateDream(sim);
        }

        /// <summary>
        /// Composes a dream from the vocabulary (partly memory-seeded), writes
        /// it to the journal, and possibly seeds the world with its place.
        /// </summary>
        public static DreamState GenerateDream(Simulation sim)
        {
            var content = SeededRandom.Derive(sim.State.Seed, "dream:" + sim.State.Dreams.NextId);
            bool memorySeeded;
            var elements = PickElements(sim, content, out memorySeeded);
            var mood = content.Pick((DreamMood[])Enum.GetValues(typeof(DreamMood)));

            var dream = new DreamState
            {
                Id = sim.State.Dreams.NextId++,
                Time = sim.Now,
                Generation = sim.State.Lineage.Generation,
                Elements = elements,
                Mood = mood,
                MemorySeeded = memorySeeded
            };
            dream.Text = ComposeText(content, dream);
            sim.State.Dreams.Dreams.Add(dream);

            // Dreams are uncertain by nature: marked as such in the journal.
            sim.Journal(sim.Now, dream.Text, JournalCategory.Dream, 0.55f, 0.5f);

            if (sim.DreamRng.NextFloat() < SeedChance)
                SeedDreamPlace(sim, dream);

            return dream;
        }

        /// <summary>
        /// Called after a POI is discovered. If an unresolved dream's elements
        /// rhyme with the place (feature overlap, not exact identity), the
        /// dream is fulfilled and the recognition is journaled. Only the
        /// oldest matching dream resolves per discovery.
        /// </summary>
        public static void CheckRecognition(Simulation sim, int poiId)
        {
            var poi = sim.State.World.GetPoi(poiId);
            if (poi == null) return;
            var poiElements = DreamVocabulary.ElementsFor(poi.Type);

            foreach (var dream in sim.State.Dreams.Dreams)
            {
                if (dream.Status != DreamStatus.Unresolved) continue;
                if (dream.Elements.Count == 0) continue;

                int overlap = 0;
                foreach (var e in dream.Elements)
                    if (poiElements.Contains(e)) overlap++;
                // A place grown from the dream itself always rhymes strongly.
                if (poi.Id == dream.SeededPlaceId) overlap += 2;

                if (overlap >= 2)
                {
                    dream.Status = DreamStatus.Fulfilled;
                    dream.ResolvedPlaceId = poi.Id;
                    string first = DreamVocabulary.Fragments(dream.Elements[0])[0];
                    sim.Journal(sim.Now,
                        "This is the place from my dream. " +
                        DreamVocabulary.Capitalize(first) + ", just as I dreamed it. " +
                        "I stood still a long time, and my light burned steady.",
                        JournalCategory.Dream, 0.85f, 0.9f, poi.Id);
                    sim.State.Beliefs.AddOrUpdate("dream." + dream.Id + ".true",
                        "My dream was true — I found the place", "dreamed", 0.9f, sim.Now);
                    break;
                }
            }
        }

        // -- internals -------------------------------------------------------------

        private static List<DreamElement> PickElements(Simulation sim, SeededRandom rng, out bool memorySeeded)
        {
            var pool = new List<DreamElement>();
            memorySeeded = false;

            // Half the time, strong recent memories lend their elements first.
            if (sim.DreamRng.NextFloat() < 0.5f)
            {
                var mem = CollectMemoryElements(sim, rng);
                if (mem.Count > 0)
                {
                    pool.AddRange(mem);
                    memorySeeded = true;
                }
            }

            // Fill the recipe to 2-4 elements from the full vocabulary.
            var all = new List<DreamElement>((DreamElement[])Enum.GetValues(typeof(DreamElement)));
            rng.Shuffle(all);
            foreach (var e in all)
            {
                if (pool.Count >= 4) break;
                if (!pool.Contains(e)) pool.Add(e);
            }
            return pool;
        }

        /// <summary>
        /// Recent high-salience discoveries lend their elements: the mind
        /// replays what moved it. Returns 0-2 elements.
        /// </summary>
        private static List<DreamElement> CollectMemoryElements(Simulation sim, SeededRandom rng)
        {
            var candidates = new List<DreamElement>();
            foreach (var e in sim.State.Journal.Recent(12))
            {
                if (e.Category != JournalCategory.Discovery || !e.PlaceId.HasValue) continue;
                var poi = sim.State.World.GetPoi(e.PlaceId.Value);
                if (poi == null) continue;
                foreach (var el in DreamVocabulary.ElementsFor(poi.Type))
                    if (!candidates.Contains(el)) candidates.Add(el);
            }
            rng.Shuffle(candidates);
            var picked = new List<DreamElement>();
            for (int i = 0; i < Math.Min(2, candidates.Count); i++)
                picked.Add(candidates[i]);
            return picked;
        }

        private static string ComposeText(SeededRandom rng, DreamState dream)
        {
            var parts = new List<string>();
            foreach (var e in dream.Elements)
                parts.Add(rng.Pick(DreamVocabulary.Fragments(e)));
            return "I dreamed " + string.Join(", ", parts.ToArray()) + ", " +
                   DreamVocabulary.MoodClosing(dream.Mood, rng);
        }

        /// <summary>
        /// Grows the dream's place: a genuinely new POI composed from the
        /// recipe's dominant structural element, placed far from home and
        /// from where the fox slept. If no ground can be found, the dream
        /// simply stays unseeded — a mystery.
        /// </summary>
        internal static void SeedDreamPlace(Simulation sim, DreamState dream)
        {
            // The dominant element: first structural one in the recipe.
            DreamElement? dominant = null;
            foreach (var e in dream.Elements)
            {
                if (DreamVocabulary.IsStructural(e)) { dominant = e; break; }
            }
            if (!dominant.HasValue) return; // pure weather: seeds nothing

            var rng = SeededRandom.Derive(sim.State.Seed, "dreamplace:" + dream.Id);
            var world = sim.State.World;
            PoiType type = DreamVocabulary.TypeForElement(dominant.Value);

            PointOfInterest den = null;
            foreach (var p in world.Pois)
                if (p.Type == PoiType.Den) { den = p; break; }
            float hx = den != null ? den.X : 0f;
            float hz = den != null ? den.Z : 0f;
            float ax = sim.State.Agent.X, az = sim.State.Agent.Z;

            float x = 0f, z = 0f;
            bool found = false;
            for (int t = 0; t < 120 && !found; t++)
            {
                x = rng.NextFloat(-world.HalfSize + 25f, world.HalfSize - 25f);
                z = rng.NextFloat(-world.HalfSize + 25f, world.HalfSize - 25f);
                if (world.IsWater(x, z) || world.SlopeAt(x, z) > 0.6f) continue;
                float dhx = x - hx, dhz = z - hz;
                if (dhx * dhx + dhz * dhz < 150f * 150f) continue;  // far from home
                float dax = x - ax, daz = z - az;
                if (dax * dax + daz * daz < 120f * 120f) continue; // far from where I slept
                bool crowded = false;
                foreach (var p in world.Pois)
                {
                    float pdx = x - p.X, pdz = z - p.Z;
                    if (pdx * pdx + pdz * pdz < 35f * 35f) { crowded = true; break; }
                }
                if (crowded) continue;
                found = true;
            }
            if (!found) return;

            int id = 0;
            foreach (var p in world.Pois)
                if (p.Id >= id) id = p.Id + 1;

            var poi = new PointOfInterest
            {
                Id = id,
                Type = type,
                Name = DreamVocabulary.DreamNameFor(type, rng),
                X = x,
                Z = z,
                Radius = DreamVocabulary.RadiusFor(type),
                Discovered = false,
                LearnedName = false,
                Stock = type == PoiType.GlowberryBush ? rng.NextInt(3, 9) : 0,
                Looted = false,
                DreamId = dream.Id
            };
            world.Pois.Add(poi);
            dream.SeededPlaceId = poi.Id;
        }
    }
}
