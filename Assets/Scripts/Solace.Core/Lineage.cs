// Solace.Core — the lineage: generations, aging, sickness, kits, bonding,
// tales, and succession.
//
// The locked pivot: the game is a lineage, not a life. The protagonist is a
// lantern-fox-like creature (slender, long-limbed, luminous core in the chest)
// in a world where no humans ever existed. Light is life: the Energy need IS
// light, and sickness/dimming reads in the glow. Generations turn through
// bonding, kits, aging, and death; death closes a chapter, never the game.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    /// <summary>Life stage derived from Age (game-years) and species lifespan.</summary>
    public enum LifeStage
    {
        Kit,       // < 1 game-year
        Juvenile,  // < 3 game-years
        Adult,     // prime
        Elder      // past ~65% of lifespan
    }

    /// <summary>Sicknesses of the light. None = healthy.</summary>
    public enum SicknessKind
    {
        None,
        DimCough,   // caught in bad weather while weak, or near sick kindred
        GutTwist,   // from eating while starving
        LightFever  // storm-brought, the most dangerous
    }

    /// <summary>A pair-bond with another of his kind.</summary>
    public class Bond
    {
        public int PartnerId = -1;   // kindred entity id
        public float Strength;       // 0..1
        public float SinceStrongAt;  // game time when Strength first passed 0.7
        public bool LitterBorn;      // this bond has already produced kits

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("partnerId", PartnerId);
            o.Add("strength", Strength);
            o.Add("sinceStrongAt", SinceStrongAt);
            o.Add("litterBorn", LitterBorn);
            return o;
        }

        public static Bond FromJson(JsonObject o)
        {
            return new Bond
            {
                PartnerId = JsonHelpers.GetInt(o, "partnerId", -1),
                Strength = JsonHelpers.GetFloat(o, "strength", 0f),
                SinceStrongAt = JsonHelpers.GetFloat(o, "sinceStrongAt", 0f),
                LitterBorn = JsonHelpers.GetBool(o, "litterBorn", false)
            };
        }
    }

    /// <summary>
    /// A distilled memory: something worth telling the kits. On succession the
    /// heir learns the parent's tales, and each tale's lesson becomes instinct
    /// (a small personality nudge).
    /// </summary>
    public class Tale
    {
        public int Id;
        public string Title = "";
        public string LessonTrait = "Caution"; // Personality.Nudge key
        public float LessonAmount;             // small, e.g. 0.05
        public int OriginGeneration;
        public string OriginEvent = "";        // e.g. "first saw the hollow hive"

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("title", Title);
            o.Add("lessonTrait", LessonTrait);
            o.Add("lessonAmount", LessonAmount);
            o.Add("originGeneration", OriginGeneration);
            o.Add("originEvent", OriginEvent);
            return o;
        }

        public static Tale FromJson(JsonObject o)
        {
            return new Tale
            {
                Id = JsonHelpers.GetInt(o, "id", 0),
                Title = JsonHelpers.GetString(o, "title", ""),
                LessonTrait = JsonHelpers.GetString(o, "lessonTrait", "Caution"),
                LessonAmount = JsonHelpers.GetFloat(o, "lessonAmount", 0f),
                OriginGeneration = JsonHelpers.GetInt(o, "originGeneration", 1),
                OriginEvent = JsonHelpers.GetString(o, "originEvent", "")
            };
        }
    }

    /// <summary>One closed chapter of the chronicle.</summary>
    public class ChapterRecord
    {
        public int Generation;
        public float StartTime;  // game seconds
        public float EndTime;
        public string Cause = ""; // short summary, e.g. "old age", "gloom-maw"

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("generation", Generation);
            o.Add("startTime", StartTime);
            o.Add("endTime", EndTime);
            o.Add("cause", Cause);
            return o;
        }

        public static ChapterRecord FromJson(JsonObject o)
        {
            return new ChapterRecord
            {
                Generation = JsonHelpers.GetInt(o, "generation", 1),
                StartTime = JsonHelpers.GetFloat(o, "startTime", 0f),
                EndTime = JsonHelpers.GetFloat(o, "endTime", 0f),
                Cause = JsonHelpers.GetString(o, "cause", "")
            };
        }
    }

    /// <summary>
    /// The lineage itself: which generation is living, the closed chapters, all
    /// tales ever distilled, and the ids of living kin among the kindred.
    /// </summary>
    public class LineageState
    {
        public int Generation = 1;
        public int ProtagonistId = 1;
        public int NextAgentId = 2;
        public int NextTaleId = 1;
        public float ChapterStartTime;
        public List<ChapterRecord> Chapters = new List<ChapterRecord>();
        public List<Tale> Tales = new List<Tale>();
        public List<int> KinEntityIds = new List<int>(); // grown kits / bonded kin among entities

        public Tale GetTale(int id)
        {
            for (int i = 0; i < Tales.Count; i++)
                if (Tales[i].Id == id) return Tales[i];
            return null;
        }

        public int TalesThisGeneration(int generation)
        {
            int n = 0;
            for (int i = 0; i < Tales.Count; i++)
                if (Tales[i].OriginGeneration == generation) n++;
            return n;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("generation", Generation);
            o.Add("protagonistId", ProtagonistId);
            o.Add("nextAgentId", NextAgentId);
            o.Add("nextTaleId", NextTaleId);
            o.Add("chapterStartTime", ChapterStartTime);
            var ca = new JsonArray();
            for (int i = 0; i < Chapters.Count; i++) ca.Add(Chapters[i].ToJson());
            o.Add("chapters", ca);
            var ta = new JsonArray();
            for (int i = 0; i < Tales.Count; i++) ta.Add(Tales[i].ToJson());
            o.Add("tales", ta);
            var ka = new JsonArray();
            var sorted = new List<int>(KinEntityIds);
            sorted.Sort();
            foreach (var id in sorted) ka.Add(id);
            o.Add("kinEntityIds", ka);
            return o;
        }

        public static LineageState FromJson(JsonObject o)
        {
            var l = new LineageState();
            l.Generation = JsonHelpers.GetInt(o, "generation", 1);
            l.ProtagonistId = JsonHelpers.GetInt(o, "protagonistId", 1);
            l.NextAgentId = JsonHelpers.GetInt(o, "nextAgentId", 2);
            l.NextTaleId = JsonHelpers.GetInt(o, "nextTaleId", 1);
            l.ChapterStartTime = JsonHelpers.GetFloat(o, "chapterStartTime", 0f);
            JsonValue cv;
            if (o.TryGet("chapters", out cv) && !cv.IsNull)
            {
                var ca = cv.AsArray();
                for (int i = 0; i < ca.Count; i++) l.Chapters.Add(ChapterRecord.FromJson(ca[i].AsObject()));
            }
            JsonValue tv;
            if (o.TryGet("tales", out tv) && !tv.IsNull)
            {
                var ta = tv.AsArray();
                for (int i = 0; i < ta.Count; i++) l.Tales.Add(Tale.FromJson(ta[i].AsObject()));
            }
            JsonValue kv;
            if (o.TryGet("kinEntityIds", out kv) && !kv.IsNull)
            {
                var ka = kv.AsArray();
                for (int i = 0; i < ka.Count; i++) l.KinEntityIds.Add(((JsonNumber)ka[i]).AsInt());
            }
            return l;
        }
    }

    /// <summary>
    /// A kit: simpler than an agent. Needs are energy (light), hunger, health,
    /// mood — no curiosity. Kits learn by following the parent and watching.
    /// </summary>
    public class KitState
    {
        public int Id;
        public string Name = "";
        public float X, Z;
        public float Facing;
        public float Age;          // game-years
        public float Energy = 80f; // light
        public float Hunger = 25f;
        public float Health = 100f;
        public float Mood = 60f;
        public int ParentId = -1;  // protagonist agent id (lineage record)
        public int MotherId = -1;  // kindred entity id, if known
        public int FatherId = -1;  // protagonist agent id
        public float LightShade = 0.5f; // inherited hue 0..1
        public float Forage;       // learned skills 0..1
        public float Notice;
        public float Hide;
        public bool FollowingParent = true;
        public bool IsAlive = true;
        public string State = "Follow"; // Follow | Play | Eat | Hide | Sleep

        public V2 Pos { get { return new V2(X, Z); } }

        public LifeStage Stage
        {
            get { return Age < 1f ? LifeStage.Kit : Age < 3f ? LifeStage.Juvenile : LifeStage.Adult; }
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("name", Name);
            o.Add("x", X); o.Add("z", Z);
            o.Add("facing", Facing);
            o.Add("age", Age);
            o.Add("energy", Energy); o.Add("hunger", Hunger);
            o.Add("health", Health); o.Add("mood", Mood);
            o.Add("parentId", ParentId);
            o.Add("motherId", MotherId);
            o.Add("fatherId", FatherId);
            o.Add("lightShade", LightShade);
            o.Add("forage", Forage); o.Add("notice", Notice); o.Add("hide", Hide);
            o.Add("followingParent", FollowingParent);
            o.Add("isAlive", IsAlive);
            o.Add("state", State);
            return o;
        }

        public static KitState FromJson(JsonObject o)
        {
            var k = new KitState();
            k.Id = JsonHelpers.GetInt(o, "id", 0);
            k.Name = JsonHelpers.GetString(o, "name", "kit");
            k.X = JsonHelpers.GetFloat(o, "x", 0f); k.Z = JsonHelpers.GetFloat(o, "z", 0f);
            k.Facing = JsonHelpers.GetFloat(o, "facing", 0f);
            k.Age = JsonHelpers.GetFloat(o, "age", 0f);
            k.Energy = JsonHelpers.GetFloat(o, "energy", 80f);
            k.Hunger = JsonHelpers.GetFloat(o, "hunger", 25f);
            k.Health = JsonHelpers.GetFloat(o, "health", 100f);
            k.Mood = JsonHelpers.GetFloat(o, "mood", 60f);
            k.ParentId = JsonHelpers.GetInt(o, "parentId", -1);
            k.MotherId = JsonHelpers.GetInt(o, "motherId", -1);
            k.FatherId = JsonHelpers.GetInt(o, "fatherId", -1);
            k.LightShade = JsonHelpers.GetFloat(o, "lightShade", 0.5f);
            k.Forage = JsonHelpers.GetFloat(o, "forage", 0f);
            k.Notice = JsonHelpers.GetFloat(o, "notice", 0f);
            k.Hide = JsonHelpers.GetFloat(o, "hide", 0f);
            k.FollowingParent = JsonHelpers.GetBool(o, "followingParent", true);
            k.IsAlive = JsonHelpers.GetBool(o, "isAlive", true);
            k.State = JsonHelpers.GetString(o, "state", "Follow");
            return k;
        }
    }

    /// <summary>
    /// The kit mind: Follow / Play / Eat / Hide / Sleep. Small, readable, and
    /// cheap — a handful of kits at most. Kits learn skills by watching the
    /// parent; the Simulation journals notable moments from the kit's view.
    /// </summary>
    public static class KitBrain
    {
        private static readonly string[] KitNames =
            { "Ash", "Wick", "Tallow", "Moth", "Ember", "Soot", "Lumen", "Pip", "Cinder", "Glow" };

        public static string PickKitName(SeededRandom rng, List<KitState> existing)
        {
            for (int t = 0; t < 20; t++)
            {
                string n = rng.Pick(KitNames);
                bool used = false;
                foreach (var k in existing)
                    if (k.Name == n) { used = true; break; }
                if (!used) return n;
            }
            return rng.Pick(KitNames);
        }

        public static void Tick(Simulation sim, KitState kit, float dt)
        {
            if (!kit.IsAlive) return;
            var s = sim.State;
            var parent = s.Agent;

            kit.Age += dt / 86400f / LineageSystem.DaysPerYear;
            kit.Hunger = MathX.Clamp(kit.Hunger + dt * 0.05f, 0f, 100f);
            if (kit.Hunger > 90f)
            {
                kit.Health -= dt * 0.2f; // starving weakens
                kit.Mood = MathX.Clamp(kit.Mood - dt * 0.5f, 0f, 100f);
            }
            if (kit.Health <= 0f)
            {
                kit.IsAlive = false;
                sim.Journal(sim.Now,
                    "Little " + kit.Name + " dimmed and did not wake. I buried her light under stones by the den.",
                    JournalCategory.Survival, 0.95f);
                return;
            }

            float pd = parent.IsAlive ? V2.Distance(kit.Pos, parent.Pos) : float.MaxValue;

            // Danger first: hide at the den when a gloom-maw is near.
            if (NearestPredatorDist(sim, kit.Pos, 22f) < 22f)
            {
                kit.State = "Hide";
                var den = FindDen(s);
                if (den != null)
                {
                    MoveToward(sim, kit, den.X, den.Z, 3.2f, dt, 3f);
                    kit.Hide = MathX.Clamp01(kit.Hide + dt * 0.002f);
                }
                return;
            }

            // Eat when the parent eats nearby: the parent shares.
            if (parent.IsAlive && parent.CurrentGoal == "Eat" && pd < 15f)
            {
                kit.State = "Eat";
                MoveToward(sim, kit, parent.X, parent.Z, 3.4f, dt, 2f);
                kit.Hunger = MathX.Clamp(kit.Hunger - dt * 2.5f, 0f, 100f);
                kit.Forage = MathX.Clamp01(kit.Forage + dt * 0.004f);
                return;
            }

            // Kindred share food with hungry kits.
            if (kit.Hunger > 60f && NearestKindredDist(sim, kit.Pos, 12f) < 12f)
            {
                kit.Hunger = MathX.Clamp(kit.Hunger - dt * 0.8f, 0f, 100f);
                if (kit.State != "Eat") kit.State = "Eat";
                return;
            }

            // Sleep at night or when the light runs low.
            if (s.IsNight || kit.Energy < 22f)
            {
                kit.State = "Sleep";
                kit.Energy = MathX.Clamp(kit.Energy + dt * 0.5f, 0f, 100f);
                kit.Hunger = MathX.Clamp(kit.Hunger + dt * 0.02f, 0f, 100f);
                return;
            }

            // Follow the parent; play when close and the light is high.
            if (parent.IsAlive && kit.FollowingParent)
            {
                float followRadius = kit.Stage == LifeStage.Kit ? 5f : 12f;
                if (pd > followRadius)
                {
                    kit.State = "Follow";
                    MoveToward(sim, kit, parent.X, parent.Z, 3.0f, dt, followRadius * 0.6f);
                    return;
                }
            }

            kit.State = "Play";
            kit.Energy = MathX.Clamp(kit.Energy - dt * 0.06f, 0f, 100f);
            kit.Mood = MathX.Clamp(kit.Mood + dt * 0.1f, 0f, 100f);
            // Hop about near the parent (or hold near the den if the parent is gone).
            float ax = parent.IsAlive ? parent.X : kit.X;
            float az = parent.IsAlive ? parent.Z : kit.Z;
            if (V2.Distance(kit.Pos, new V2(ax, az)) > 14f || sim.EventRng.NextFloat() < dt * 0.08f)
            {
                float ang = sim.EventRng.NextFloat(0f, MathF.PI * 2f);
                float r = sim.EventRng.NextFloat(3f, 9f);
                MoveToward(sim, kit, ax + (float)Math.Cos(ang) * r, az + (float)Math.Sin(ang) * r, 2.6f, dt, 1f);
            }
        }

        private static float NearestPredatorDist(Simulation sim, V2 pos, float maxDist)
        {
            float best = float.MaxValue;
            foreach (var e in sim.State.Entities)
            {
                if (e.Kind != EntityKind.Predator || !e.IsAlive) continue;
                float d = V2.Distance(pos, e.Pos);
                if (d < maxDist && d < best) best = d;
            }
            return best;
        }

        private static float NearestKindredDist(Simulation sim, V2 pos, float maxDist)
        {
            float best = float.MaxValue;
            foreach (var e in sim.State.Entities)
            {
                if (e.Kind != EntityKind.Kindred || !e.IsAlive) continue;
                float d = V2.Distance(pos, e.Pos);
                if (d < maxDist && d < best) best = d;
            }
            return best;
        }

        private static PointOfInterest FindDen(GameState s)
        {
            foreach (var p in s.World.Pois)
                if (p.Type == PoiType.Den) return p;
            return null;
        }

        private static void MoveToward(Simulation sim, KitState kit, float tx, float tz,
                                       float speed, float dt, float arrive)
        {
            float dx = tx - kit.X, dz = tz - kit.Z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < Math.Max(arrive, 0.5f)) return;
            dx /= d; dz /= d;
            float nx = kit.X + dx * speed * dt;
            float nz = kit.Z + dz * speed * dt;
            if (sim.State.World.IsWater(nx, nz)) return; // hold rather than swim
            float half = sim.State.World.HalfSize - 4f;
            kit.X = MathX.Clamp(nx, -half, half);
            kit.Z = MathX.Clamp(nz, -half, half);
            kit.Facing = MathF.Atan2(dx, dz);
            kit.Energy = MathX.Clamp(kit.Energy - dt * 0.05f, 0f, 100f);
        }
    }

    /// <summary>
    /// Lineage mechanics: aging, old-age death, sickness, tale distillation,
    /// bonding/litters, and succession. All RNG comes from the lineage stream.
    /// </summary>
    public static class LineageSystem
    {
        /// <summary>Game-days per game-year. A ~16-year life is ~128 game-days.</summary>
        public const float DaysPerYear = 8f;

        public const int MaxTalesPerGeneration = 12;

        public static LifeStage StageFor(float ageYears, float lifespanYears)
        {
            if (ageYears < 1f) return LifeStage.Kit;
            if (ageYears < 3f) return LifeStage.Juvenile;
            if (ageYears < lifespanYears * 0.65f) return LifeStage.Adult;
            return LifeStage.Elder;
        }

        public static string SicknessName(SicknessKind k)
        {
            switch (k)
            {
                case SicknessKind.DimCough: return "dim-cough";
                case SicknessKind.GutTwist: return "gut-twist";
                case SicknessKind.LightFever: return "light-fever";
                default: return "sickness";
            }
        }

        // -- aging ----------------------------------------------------------

        /// <summary>Advances age; handles vigor foreshadowing and old-age death.</summary>
        public static void TickAging(Simulation sim, float dt)
        {
            var s = sim.State;
            var a = s.Agent;
            if (!a.IsAlive) return;

            a.Age += dt / 86400f / DaysPerYear;
            var rng = sim.LineageRng;

            // Foreshadowing: the dimming is never sudden.
            if (!a.VigorForeshadowed && a.Age > a.LifespanYears * 0.92f)
            {
                a.VigorForeshadowed = true;
                sim.Journal(sim.Now,
                    "I can feel my light thinning, the way evening thins. Not yet — but it is coming. " +
                    "I should make sure the tales are told.",
                    JournalCategory.Reflection, 0.9f);
            }

            // Past lifespan: an increasing daily chance of a peaceful dimming.
            if (a.Age > a.LifespanYears)
            {
                float yearsPast = a.Age - a.LifespanYears;
                float dailyP = Math.Min(0.25f, 0.015f + 0.035f * yearsPast);
                if (rng.NextFloat() < dailyP * dt / 86400f)
                    sim.KillAgent("I dimmed the way evening dims, old and full of tales.");
            }
        }

        // -- sickness -------------------------------------------------------

        /// <summary>Progresses sickness; rest and food cure, strain worsens.</summary>
        public static void TickSickness(Simulation sim, float dt)
        {
            var s = sim.State;
            var a = s.Agent;
            if (!a.IsAlive) return;
            var rng = sim.LineageRng;

            if (a.Sickness == SicknessKind.None)
            {
                TryContract(sim, rng, dt);
                return;
            }

            bool resting = a.CurrentGoal == "Rest";
            bool wellFed = a.Hunger < 50f;
            if ((resting || wellFed) && a.Hunger < 80f)
                a.SicknessSeverity -= dt * (0.08f / 3600f) * (resting ? 1.6f : 1f); // slow cure
            else
                a.SicknessSeverity += dt * (0.05f / 3600f); // slow worsening

            a.SicknessSeverity = MathX.Clamp01(a.SicknessSeverity);

            if (a.SicknessSeverity <= 0f)
            {
                sim.Journal(sim.Now,
                    "The " + SicknessName(a.Sickness) + " has loosened its grip. My light is my own again.",
                    JournalCategory.Survival, 0.55f);
                a.Sickness = SicknessKind.None;
                return;
            }

            // At full severity, untreated, the light gutters: health drains.
            if (a.SicknessSeverity >= 1f)
            {
                a.Health = MathX.Clamp(a.Health - dt * 0.15f, 0f, 100f);
                if (!a.SicknessWarned || sim.Now - a.SicknessWarnedAt > 86400f)
                {
                    a.SicknessWarned = true;
                    a.SicknessWarnedAt = sim.Now;
                    sim.Journal(sim.Now,
                        "My light is guttering with the " + SicknessName(a.Sickness) +
                        ". If I do not rest and eat, it will go out.",
                        JournalCategory.Survival, 0.9f);
                }
                if (a.Health <= 0f)
                    sim.KillAgent("The " + SicknessName(a.Sickness) + " took my light, though I fought it to the end.");
            }
        }

        private static void TryContract(Simulation sim, SeededRandom rng, float dt)
        {
            var s = sim.State;
            var a = s.Agent;
            float ageFactor = 1f + a.Age / Math.Max(1f, a.LifespanYears); // susceptibility rises with age
            float dayFrac = dt / 86400f;

            // Bad weather while weak.
            bool badWeather = s.Weather == Weather.Rain || s.Weather == Weather.Storm;
            bool weak = a.Health < 45f || a.Energy < 25f;
            if (badWeather && weak && rng.NextFloat() < 0.10f * ageFactor * dayFrac)
            {
                Contract(sim, s.Weather == Weather.Storm ? SicknessKind.LightFever : SicknessKind.DimCough, 0.25f);
                return;
            }

            // Near sick kindred.
            foreach (var e in s.Entities)
            {
                if (e.Kind != EntityKind.Kindred || !e.IsAlive) continue;
                if (e.Sickness == SicknessKind.None) continue;
                if (V2.Distance(a.Pos, e.Pos) < 8f && rng.NextFloat() < 0.18f * ageFactor * dayFrac)
                {
                    Contract(sim, e.Sickness, 0.25f);
                    return;
                }
            }
        }

        private static void Contract(Simulation sim, SicknessKind kind, float severity)
        {
            var a = sim.State.Agent;
            a.Sickness = kind;
            a.SicknessSeverity = severity;
            a.SicknessWarned = false;
            sim.Journal(sim.Now,
                "Something is wrong in my light — a " + SicknessName(kind) +
                " settling in. I need rest, and food, and time.",
                JournalCategory.Survival, 0.7f);
        }

        /// <summary>Gut-twist from eating while starving: strain has a price.</summary>
        public static void MaybeGutTwist(Simulation sim)
        {
            var a = sim.State.Agent;
            if (a.Sickness != SicknessKind.None || a.Hunger < 85f) return;
            if (sim.LineageRng.NextFloat() < 0.05f)
                Contract(sim, SicknessKind.GutTwist, 0.3f);
        }

        // -- tales ----------------------------------------------------------

        /// <summary>
        /// Distills a high-salience episode into a tellable tale (cap 12 per
        /// generation). The agent learns it immediately; telling happens at rest.
        /// </summary>
        public static void MaybeDistillTale(Simulation sim, string title, string lessonTrait,
                                            float lessonAmount, string originEvent)
        {
            var s = sim.State;
            if (s.Lineage.TalesThisGeneration(s.Lineage.Generation) >= MaxTalesPerGeneration)
                return;
            // Don't distill the same title twice in a generation.
            foreach (var t in s.Lineage.Tales)
                if (t.OriginGeneration == s.Lineage.Generation && t.Title == title) return;

            var tale = new Tale
            {
                Id = s.Lineage.NextTaleId++,
                Title = title,
                LessonTrait = lessonTrait,
                LessonAmount = lessonAmount,
                OriginGeneration = s.Lineage.Generation,
                OriginEvent = originEvent
            };
            s.Lineage.Tales.Add(tale);
            if (!s.Agent.TalesKnown.Contains(tale.Id))
                s.Agent.TalesKnown.Add(tale.Id);
        }

        // -- bonding & litters ----------------------------------------------

        /// <summary>
        /// Checks whether a strong bond has ripened into kits. Called per step;
        /// cheap (one bond at most).
        /// </summary>
        public static void TickBonding(Simulation sim)
        {
            var s = sim.State;
            var a = s.Agent;
            if (!a.IsAlive || a.Bond == null) return;
            var bond = a.Bond;
            if (bond.Strength <= 0.7f || bond.LitterBorn) return;
            if (bond.SinceStrongAt <= 0f) bond.SinceStrongAt = sim.Now;
            if (sim.Now - bond.SinceStrongAt < 2f * 86400f) return; // after time

            var partner = FindEntity(s, bond.PartnerId);
            if (partner == null || !partner.IsAlive || partner.Kind != EntityKind.Kindred) return;
            if (LineageSystem.StageFor(a.Age, a.LifespanYears) != LifeStage.Adult &&
                LineageSystem.StageFor(a.Age, a.LifespanYears) != LifeStage.Elder) return;

            bond.LitterBorn = true;
            int n = 1 + sim.LineageRng.NextInt(3);
            BirthLitter(sim, n, partner.Id);

            string countWord = n == 1 ? "One kit" : n == 2 ? "Two kits" : "Three kits";
            sim.Journal(sim.Now,
                countWord + " tumbled out of the den at dawn, all glow and noise. " +
                partner.Name + " and I are parents. The vale feels wider than the sky.",
                JournalCategory.Social, 0.95f);
            MaybeDistillTale(sim, "The Litter at " + DenName(s), "Compassion", 0.06f,
                "the birth of " + (n == 1 ? "a kit" : n + " kits"));
        }

        private static EntityState FindEntity(GameState s, int id)
        {
            for (int i = 0; i < s.Entities.Count; i++)
                if (s.Entities[i].Id == id) return s.Entities[i];
            return null;
        }

        private static string DenName(GameState s)
        {
            foreach (var p in s.World.Pois)
                if (p.Type == PoiType.Den) return p.Name;
            return "the den";
        }

        /// <summary>Births n kits at the den. Journaled by the caller.</summary>
        public static void BirthLitter(Simulation sim, int n, int motherEntityId)
        {
            var s = sim.State;
            var a = s.Agent;
            var den = FindDen(s);
            float dx = den != null ? den.X : a.X;
            float dz = den != null ? den.Z : a.Z;
            for (int i = 0; i < n; i++)
            {
                var kit = new KitState
                {
                    Id = s.Lineage.NextAgentId++,
                    Name = KitBrain.PickKitName(sim.LineageRng, s.Kits),
                    X = dx + sim.LineageRng.NextFloat(-4f, 4f),
                    Z = dz + sim.LineageRng.NextFloat(-4f, 4f),
                    Age = 0f,
                    ParentId = a.Id,
                    MotherId = motherEntityId,
                    FatherId = a.Id,
                    LightShade = MathX.Clamp01(a.LightShade + sim.LineageRng.NextFloat(-0.06f, 0.06f)),
                    FollowingParent = true,
                    IsAlive = true
                };
                s.Kits.Add(kit);
            }
        }

        private static PointOfInterest FindDen(GameState s)
        {
            foreach (var p in s.World.Pois)
                if (p.Type == PoiType.Den) return p;
            return null;
        }

        /// <summary>A grown kit leaves the den to walk her own trails as kindred.</summary>
        public static void KitToKindred(Simulation sim, KitState kit)
        {
            var s = sim.State;
            var den = FindDen(s);
            var e = new EntityState
            {
                Id = sim.NextEntityId(),
                Kind = EntityKind.Kindred,
                Name = kit.Name,
                X = den != null ? den.X + sim.EventRng.NextFloat(-10f, 10f) : kit.X,
                Z = den != null ? den.Z + sim.EventRng.NextFloat(-10f, 10f) : kit.Z,
                Health = 100f,
                Behavior = "Wander",
                HomeX = den != null ? den.X : kit.X,
                HomeZ = den != null ? den.Z : kit.Z,
            };
            e.TargetX = e.X; e.TargetZ = e.Z;
            s.Entities.Add(e);
            s.Social.Meet(e.Id, kit.Name, sim.Now);
            if (!s.Lineage.KinEntityIds.Contains(e.Id))
                s.Lineage.KinEntityIds.Add(e.Id);
            sim.Journal(sim.Now,
                kit.Name + " has grown and gone to walk her own trails. The den feels larger and emptier, and I am prouder than the sky.",
                JournalCategory.Social, 0.8f);
        }

        // -- succession -----------------------------------------------------

        /// <summary>
        /// The chapter closes and the next begins. Death is never game over:
        /// the eldest living kit takes up the tales; with no living kit, a young
        /// distant kin arrives at the den carrying them.
        /// </summary>
        public static void SucceedOnDeath(GameState s, string cause, SeededRandom rng)
        {
            var old = s.Agent;
            old.IsAlive = false;
            old.Health = 0f;
            old.HasMoveTarget = false;
            old.InCombat = false;

            int gen = s.Lineage.Generation;
            string kind = ClassifyCause(cause);

            // 1. Close the chapter.
            string closeLine = kind == "old"
                ? "Chapter " + gen + " ends: " + old.Name + " dimmed by the still loch, old and full of tales."
                : kind == "predator"
                ? "Chapter " + gen + " ends: " + old.Name + " was taken by a gloom-maw on the high fell. The tales do not end here."
                : kind == "sickness"
                ? "Chapter " + gen + " ends: " + old.Name + " dimmed of the " + SicknessName(old.Sickness) + ", though he fought it to the last."
                : "Chapter " + gen + " ends: " + old.Name + " — " + cause + " The tales do not end here.";
            s.Journal.Add(s.ElapsedSeconds, closeLine, JournalCategory.Chapter, 1.0f, 1f,
                null, null, "self", gen);
            s.Lineage.Chapters.Add(new ChapterRecord
            {
                Generation = gen,
                StartTime = s.Lineage.ChapterStartTime,
                EndTime = s.ElapsedSeconds,
                Cause = kind == "old" ? "old age" : kind == "predator" ? "gloom-maw" : kind == "sickness" ? SicknessName(old.Sickness) : cause
            });

            // 2. Choose the heir: eldest living kit, or a young distant kin.
            KitState eldest = null;
            foreach (var k in s.Kits)
            {
                if (!k.IsAlive) continue;
                if (eldest == null || k.Age > eldest.Age) eldest = k;
            }

            AgentState heir;
            string openLine;
            if (eldest != null)
            {
                s.Kits.Remove(eldest);
                heir = HeirFromKit(s, eldest, rng);
                openLine = "Chapter " + (gen + 1) + " begins: " + heir.Name +
                           " takes up the tales, her light the color of " + ShadeWord(heir.LightShade) + ".";
            }
            else
            {
                heir = HeirFromDistantKin(s, old, rng);
                openLine = "With " + old.Name + " gone, a young kin came down from the high fells, " +
                           "saying she had heard the tales. Her name is " + heir.Name + ".";
            }

            // 3. Inheritance: tales become instincts; POI discovery persists.
            foreach (var taleId in old.TalesKnown)
            {
                var tale = s.Lineage.GetTale(taleId);
                if (tale == null) continue;
                heir.Traits.Nudge(tale.LessonTrait, tale.LessonAmount);
                if (!heir.TalesKnown.Contains(taleId))
                    heir.TalesKnown.Add(taleId);
            }
            heir.KnownPoiIds = new HashSet<int>(old.KnownPoiIds); // never reset on succession
            heir.LightShade = MathX.Clamp01(old.LightShade + rng.NextFloat(-0.06f, 0.06f));

            heir.Id = s.Lineage.NextAgentId++;
            heir.Generation = gen + 1;
            heir.IsProtagonist = true;
            s.Agent = heir;
            s.Lineage.Generation = gen + 1;
            s.Lineage.ProtagonistId = heir.Id;
            s.Lineage.ChapterStartTime = s.ElapsedSeconds;

            // 4. Open the chapter.
            s.Journal.Add(s.ElapsedSeconds, openLine, JournalCategory.Chapter, 1.0f, 1f,
                null, null, "self", s.Lineage.Generation);

            // Prune dead kin records.
            for (int i = s.Lineage.KinEntityIds.Count - 1; i >= 0; i--)
            {
                var e = FindEntity(s, s.Lineage.KinEntityIds[i]);
                if (e == null || !e.IsAlive) s.Lineage.KinEntityIds.RemoveAt(i);
            }
        }

        private static string ClassifyCause(string cause)
        {
            string c = (cause ?? "").ToLowerInvariant();
            if (c.Contains("dimmed") || c.Contains("old") || c.Contains("evening dims")) return "old";
            if (c.Contains("gloom-maw") || c.Contains("predator")) return "predator";
            if (c.Contains("dim-cough") || c.Contains("gut-twist") || c.Contains("light-fever") || c.Contains("sickness")) return "sickness";
            return "other";
        }

        private static AgentState HeirFromKit(GameState s, KitState kit, SeededRandom rng)
        {
            var heir = new AgentState();
            heir.Name = kit.Name;
            heir.Age = Math.Max(kit.Age, 1f);
            heir.X = kit.X; heir.Z = kit.Z;
            heir.Facing = kit.Facing;
            heir.Energy = 85f; heir.Hunger = 20f; heir.Thirst = 15f;
            heir.Health = 100f; heir.Mood = 55f; heir.Curiosity = 70f;
            heir.LifespanYears = rng.NextFloat(14f, 20f);
            heir.LightShade = kit.LightShade;
            heir.MotherId = kit.MotherId;
            heir.FatherId = kit.FatherId;
            heir.Traits = new Personality();
            // What the kit learned following the parent shapes temperament.
            heir.Traits.Nudge("Caution", kit.Hide * 0.2f - 0.1f + (float)rng.NextGaussian() * 0.05f);
            heir.Traits.Nudge("Curiosity", kit.Notice * 0.2f - 0.1f + (float)rng.NextGaussian() * 0.05f);
            heir.Traits.Nudge("Compassion", kit.Forage * 0.1f);
            heir.CurrentActivity = "taking up the tales";
            return heir;
        }

        private static readonly string[] DistantKinNames =
            { "Vesper", "Tallow", "Ember", "Moth", "Sable", "Lumen", "Ash", "Wick" };

        private static AgentState HeirFromDistantKin(GameState s, AgentState old, SeededRandom rng)
        {
            var heir = new AgentState();
            heir.Name = rng.Pick(DistantKinNames);
            heir.Age = 1.5f;
            heir.LifespanYears = rng.NextFloat(14f, 20f);
            heir.LightShade = old.LightShade;
            heir.Energy = 85f; heir.Hunger = 20f; heir.Thirst = 15f;
            heir.Health = 100f; heir.Mood = 50f; heir.Curiosity = 75f;
            heir.Traits = Personality.Generate(rng);
            // The young kin arrives at the den.
            foreach (var p in s.World.Pois)
                if (p.Type == PoiType.Den) { heir.X = p.X + rng.NextFloat(-6f, 6f); heir.Z = p.Z + rng.NextFloat(-6f, 6f); break; }
            heir.CurrentActivity = "arriving at the den";
            return heir;
        }

        private static string ShadeWord(float shade)
        {
            // 0..1 hue as words: ember gold through moonlit blue.
            if (shade < 0.2f) return "ember-gold";
            if (shade < 0.4f) return "harvest-amber";
            if (shade < 0.6f) return "pale honey";
            if (shade < 0.8f) return "mist-green";
            return "moonlit blue";
        }
    }
}
