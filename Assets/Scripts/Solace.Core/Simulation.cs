// Solace.Core — the simulation: owns the GameState and steps it.
//
// Fixed-step authoritative updates (1/30s game-time steps) driven by a
// real-time accumulator. Deterministic: same seed + same steps + same inputs
// → same consequential state. NewLife(seed) builds a fresh life.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public class Simulation
    {
        public const float FixedDt = 1f / 30f;

        public GameState State;
        public AgentBrain Brain;
        public SeededRandom AiRng;
        public SeededRandom EventRng;
        /// <summary>Partitioned stream for aging, sickness, kits, bonding, succession.</summary>
        public SeededRandom LineageRng;
        /// <summary>Partitioned stream for colossi drift.</summary>
        public SeededRandom ColossusRng;
        /// <summary>Partitioned stream for dream rolls (content derives from the seed).</summary>
        public SeededRandom DreamRng;

        /// <summary>Game seconds advanced per real second.</summary>
        public float TimeScale = 2f;

        /// <summary>Last auto-save checkpoint (transient; not part of the save).</summary>
        public string LastCheckpointJson = "";

        private int _nextEntityId = 1;
        private float _lastCheckpointAt = -9999f;
        private float _prevHealth = 100f;
        // Season memory (per-simulation, not saved — derived from ElapsedSeconds on load).
        private Season _lastSeason = Season.Spring;
        private long _lastSeasonHourMark = 0;
        private readonly HashSet<int> _springBlessed = new HashSet<int>();
        /// <summary>
        /// Storm sense: the fox smelled a storm coming. Game-time until which the
        /// sense lingers (transient; a few game-hours). EatAction reads this.
        /// </summary>
        public float StormSensedUntil = -1f;

        private static readonly string[] KindredNames =
            { "Vesper", "Tallow", "Ember", "Moth", "Sable", "Lumen", "Ash", "Wick" };
        private static readonly string[] KitNames =
            { "Vesper", "Tallow", "Ember", "Moth", "Sable", "Lumen", "Ash", "Wick" };

        public Simulation(GameState state)
        {
            State = state ?? throw new ArgumentNullException("state");
            Brain = new AgentBrain();
            AiRng = new SeededRandom(state.Rng.Ai);
            EventRng = new SeededRandom(state.Rng.Event);
            LineageRng = new SeededRandom(state.Rng.Lineage);
            ColossusRng = new SeededRandom(state.Rng.Colossus);
            DreamRng = new SeededRandom(state.Rng.Dream);
            foreach (var e in state.Entities)
                if (e.Id >= _nextEntityId) _nextEntityId = e.Id + 1;
            _prevHealth = state.Agent.Health;
            // Restore the in-progress action so a loaded life continues its
            // thought instead of re-deciding immediately (keeps determinism).
            if (!string.IsNullOrEmpty(state.Agent.CurrentGoal))
            {
                var act = Brain.GetAction(state.Agent.CurrentGoal);
                if (act != null) Brain.CurrentActionName = act.Name;
            }
            // Season memory: start from the current season so a loaded life
            // doesn't re-journal the season it's already in.
            _lastSeason = SeasonSystem.Current(state);
            _lastSeasonHourMark = (long)Math.Floor(state.ElapsedSeconds / 3600.0);
        }

        public int NextEntityId() { return _nextEntityId++; }

        public float Now { get { return (float)State.ElapsedSeconds; } }

        // -- life factory ----------------------------------------------------------

        /// <summary>Creates a brand-new life: world, agent, ecology, first journal line.</summary>
        public static Simulation NewLife(int seed)
        {
            var world = WorldGenerator.Generate(new WorldConfig { Seed = seed });
            var ai = SeededRandom.Derive(seed, "ai");
            var ev = SeededRandom.Derive(seed, "event");
            var li = SeededRandom.Derive(seed, "lineage");
            var dr = SeededRandom.Derive(seed, "dream");

            var state = new GameState();
            state.Seed = seed;
            state.World = world;
            state.StartHour = 9f;
            state.Weather = Weather.Clear;

            var agent = new AgentState();
            agent.X = world.SpawnPoint.X;
            agent.Z = world.SpawnPoint.Z;
            agent.Facing = 0f;
            agent.Traits = Personality.Generate(ai);
            agent.CurrentGoal = "";
            agent.CurrentActivity = "waking up";
            foreach (var poi in world.Pois)
                if (poi.Type == PoiType.Den || poi.Type == PoiType.EmberHollow)
                    agent.KnownPoiIds.Add(poi.Id);

            // Lineage seed: a fresh first generation, young adult, light-shade
            // from the lineage stream, lifespan 14-20 game-years.
            agent.Name = li.Pick(KindredNames);
            agent.IsProtagonist = true;
            agent.Generation = 1;
            agent.Age = li.NextFloat(2f, 3.5f);
            agent.LightShade = li.NextFloat();
            agent.LifespanYears = li.NextFloat(14f, 20f);
            agent.Energy = 100f;
            state.Agent = agent;

            state.Lineage.Generation = 1;
            state.Lineage.ProtagonistId = 0; // agent id 0 = the protagonist
            state.Lineage.ChapterStartTime = 0f;

            state.Rng.Ai = ai.State;
            state.Rng.Event = ev.State;
            state.Rng.Lineage = li.State;
            state.Rng.Dream = dr.State;

            var sim = new Simulation(state);
            sim.PlaceEcology();

            state.Journal.Add(0f,
                "I woke on the mistmoor above " + world.DenName + " as the mist lifted, " +
                "my chest-light burning steady. A new life in " + world.ValleyName + ", " +
                "waiting to be learned.",
                JournalCategory.Chapter, 0.8f);
            state.Beliefs.AddOrUpdate("home.den", world.DenName + " is home", "saw", 1f, 0f);

            sim.SyncRng();
            return sim;
        }

        private void PlaceEcology()
        {
            var world = State.World;
            var ev = EventRng;
            PointOfInterest den = null;
            foreach (var p in world.Pois)
                if (p.Type == PoiType.Den) den = p;

            // Kindred: a small cast with names, keeping near home.
            var names = new List<string>(KindredNames);
            ev.Shuffle(names);
            int kindred = Math.Min(5, names.Count);
            for (int i = 0; i < kindred; i++)
            {
                V2 spot = RandomLandNear(ev, den.X, den.Z, 8f, 26f, 0f, 20f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Kindred,
                    Name = names[i],
                    X = spot.X, Z = spot.Z,
                    Health = 100f,
                    Behavior = "Wander",
                    HomeX = den.X, HomeZ = den.Z,
                    TargetX = spot.X, TargetZ = spot.Z
                });
                State.Social.Meet(State.Entities[State.Entities.Count - 1].Id, names[i], 0f);
            }

            // Deer on the moor and wood-edge.
            for (int i = 0; i < 6; i++)
            {
                V2 spot = RandomLandNear(ev, 0f, 0f, 40f, 200f, 4f, 28f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Deer, Name = "deer",
                    X = spot.X, Z = spot.Z, Health = 100f, Behavior = "Graze",
                    HomeX = spot.X, HomeZ = spot.Z, TargetX = spot.X, TargetZ = spot.Z
                });
            }

            // Rabbits everywhere low.
            for (int i = 0; i < 8; i++)
            {
                V2 spot = RandomLandNear(ev, den.X, den.Z, 20f, 180f, 2f, 24f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Rabbit, Name = "hare",
                    X = spot.X, Z = spot.Z, Health = 100f, Behavior = "Hop",
                    HomeX = spot.X, HomeZ = spot.Z, TargetX = spot.X, TargetZ = spot.Z
                });
            }

            // Gloom-maws: far from the den, up in the woods and crags.
            for (int i = 0; i < 3; i++)
            {
                V2 spot = RandomLandNear(ev, den.X, den.Z, 160f, 260f, 10f, 45f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Predator, Name = "gloom-maw",
                    X = spot.X, Z = spot.Z, Health = 100f, Behavior = "Roam",
                    HomeX = spot.X, HomeZ = spot.Z, Hunger = 45f,
                    TargetX = spot.X, TargetZ = spot.Z
                });
            }
        }

        private void AddEntity(EntityState e)
        {
            e.Id = NextEntityId();
            e.Facing = EventRng.NextFloat(0f, MathF.PI * 2f);
            State.Entities.Add(e);
        }

        private V2 RandomLandNear(SeededRandom ev, float cx, float cz, float minR, float maxR, float minH, float maxH)
        {
            var world = State.World;
            for (int t = 0; t < 80; t++)
            {
                float ang = ev.NextFloat(0f, MathF.PI * 2f);
                float r = ev.NextFloat(minR, maxR);
                float x = cx + (float)Math.Cos(ang) * r;
                float z = cz + (float)Math.Sin(ang) * r;
                if (Math.Abs(x) > world.HalfSize - 20f || Math.Abs(z) > world.HalfSize - 20f) continue;
                float h = world.SampleHeight(x, z);
                if (h < WorldData.WaterLevel + 0.6f || h < minH || h > maxH) continue;
                if (world.SlopeAt(x, z) > 0.7f) continue;
                return new V2(x, z);
            }
            return new V2(cx + minR, cz); // fallback: deterministic-ish, on-radius
        }

        // -- stepping ------------------------------------------------------------------

        /// <summary>Advances the simulation by dtReal real seconds.</summary>
        public void Step(float dtRealSeconds)
        {
            if (dtRealSeconds <= 0f) return;
            float acc = State.StepRemainder + dtRealSeconds * TimeScale;
            int guard = 0;
            while (acc >= FixedDt && guard++ < 20000)
            {
                FixedStep(FixedDt);
                acc -= FixedDt;
            }
            State.StepRemainder = acc;
            SyncRng();
        }

        private void FixedStep(float h)
        {
            State.ElapsedSeconds += h;

            WeatherDrift();
            SeasonSystem.Tick(this, ref _lastSeason, ref _lastSeasonHourMark, _springBlessed);
            LineageSystem.TickAging(this, h);
            LineageSystem.TickSickness(this, h);
            LineageSystem.TickBonding(this);
            StepKits(h);
            ColossusSystem.Tick(State, ColossusRng, h, Now, (t, text, cat, sal) =>
                Journal(t, text, cat, sal));
            Brain.Tick(this, h);
            DreamSystem.TickSleep(this);
            FoxVoice.Tick(this, h);
            StepEntities(h);
            DiscoveryCheck();
            InjuryHook();
            State.Social.Decay((float)State.ElapsedSeconds, h); // stateless per-step: no chunk timer to persist
            EasterEggSystem.Tick(this, h); // secrets and gifts: detection + journal only
            MilestoneSystem.Tick(this); // detection only: unlocks celebrations, never changes logic
            StatSystem.Tick(this); // tracking only: lifetime statistics, never changes logic

            if (State.ElapsedSeconds - _lastCheckpointAt > 300f)
            {
                _lastCheckpointAt = (float)State.ElapsedSeconds;
                SyncRng();
                LastCheckpointJson = SaveSystem.Save(State);
            }
        }

        private void SyncRng()
        {
            State.Rng.Ai = AiRng.State;
            State.Rng.Event = EventRng.State;
            State.Rng.Lineage = LineageRng.State;
            State.Rng.Colossus = ColossusRng.State;
            State.Rng.Dream = DreamRng.State;
        }

        // -- subsystems -----------------------------------------------------------------

        private void WeatherDrift()
        {
            if (State.ElapsedSeconds - State.WeatherChangedAt < 2700f) return; // ~45 game-min
            State.WeatherChangedAt = (float)State.ElapsedSeconds;
            Weather next = SaveSystem.RollWeather(State.Weather, EventRng);
            if (next == State.Weather) return;
            State.Weather = next;
            switch (next)
            {
                case Weather.Rain:
                    Journal(Now, "Rain begins to fall over the vale.", JournalCategory.Weather, 0.3f);
                    // The sensed storm arrived as rain instead: close enough.
                    StormSensedUntil = -1f;
                    break;
                case Weather.Storm:
                    Journal(Now, "A storm is coming down off the tops. I should think about shelter.", JournalCategory.Weather, 0.5f);
                    StormSensedUntil = -1f;
                    break;
                case Weather.Cloudy:
                    // Storm sense: the fox smells weather on the wind. Sometimes right,
                    // sometimes wrong — instinct, not forecast. (Deterministic via EventRng.)
                    if (EventRng.NextFloat() < 0.35f)
                    {
                        StormSensedUntil = Now + 5400f; // ~1.5 game-hours of foreboding
                        FoxVoice.OnStormSense(this);
                    }
                    break;
                case Weather.Clear:
                    if (StormSensedUntil > 0f)
                    {
                        // False alarm: the wind lied.
                        StormSensedUntil = -1f;
                        FoxVoice.OnFalseAlarm(this);
                    }
                    else if (EventRng.NextFloat() < 0.4f)
                        Journal(Now, "The cloud broke and the vale filled with light.", JournalCategory.Weather, 0.25f);
                    break;
            }
        }

        private void StepEntities(float h)
        {
            var ctx = new EntityContext
            {
                World = State.World,
                Agent = State.Agent,
                Entities = State.Entities,
                Kits = State.Kits,
                Now = Now,
                Rng = EventRng
            };
            foreach (var e in State.Entities)
                EntityBehaviors.Step(e, ctx, h);

            // Predator bites on the agent surface here as journaled injuries.
            foreach (var line in ctx.EventLog)
            {
                if (line.StartsWith("bite:"))
                {
                    float dmg = float.Parse(line.Substring(5),
                        System.Globalization.CultureInfo.InvariantCulture);
                    Journal(Now, "A gloom-maw got its teeth into me (" + dmg.ToString("F0") + "). I need to get away.",
                        JournalCategory.Combat, 0.7f);
                    State.Agent.Traits.Nudge("Caution", 0.01f);
                }
                else if (line.StartsWith("kill:"))
                {
                    Journal(Now, line.Substring(5), JournalCategory.Travel, 0.45f);
                }
                else if (line.StartsWith("kitkill:"))
                {
                    Journal(Now, line.Substring(8), JournalCategory.Social, 0.95f);
                    State.Agent.Mood = MathX.Clamp(State.Agent.Mood - 15f, 0f, 100f);
                }
            }
        }

        private void StepKits(float h)
        {
            var a = State.Agent;
            if (!a.IsAlive) return;
            foreach (var kit in State.Kits)
                KitBrain.Tick(this, kit, h);
            // Kits that reach age 3 join the den as kindred (eldest first).
            for (int i = State.Kits.Count - 1; i >= 0; i--)
            {
                var kit = State.Kits[i];
                if (!kit.IsAlive) continue;
                if (kit.Age >= 3f)
                    LineageSystem.KitToKindred(this, kit);
            }
        }

        private void DiscoveryCheck()
        {
            var a = State.Agent;
            if (!a.IsAlive) return;
            foreach (var poi in State.World.Pois)
            {
                if (poi.Discovered) continue;
                float d = V2.Distance(a.Pos, new V2(poi.X, poi.Z));
                if (d < poi.Radius * 0.7f + 4f)
                    DiscoverPoi(poi.Id);
            }
        }

        /// <summary>Marks a place discovered: journal, belief, known-map. Idempotent.</summary>
        public void DiscoverPoi(int id)
        {
            var poi = State.World.GetPoi(id);
            if (poi == null || poi.Discovered) return;
            poi.Discovered = true;
            State.Agent.KnownPoiIds.Add(id);

            string line;
            float salience;
            switch (poi.Type)
            {
                case PoiType.InsectileRuin:
                    // Name unknown until learned — use DisplayName-safe wording.
                    line = "I climbed to the hollow hive on the fell. Chitin arches, empty as sky. " +
                           "Something built here long before my kind, and left nothing but shape.";
                    salience = 0.9f; break;
                case PoiType.RuinSite:
                    line = "I found " + poi.DisplayName + ". Old husks of a story, older than stories.";
                    salience = 0.75f; break;
                case PoiType.Overlook:
                    line = "I climbed until the whole vale lay below me — river, loch, den, and all. " +
                           "The wind up there tasted like distance. I sat a long while.";
                    salience = 0.65f; break;
                case PoiType.CrystalCave:
                    line = "I found " + poi.DisplayName + " — a throat in the rock lined with living glass. " +
                           "At night the stones hum faint blue light, like the sky fell in and kept shining. " +
                           "It is dry here, and hidden. A good place to vanish.";
                    salience = 0.85f; break;
                case PoiType.HotSpring:
                    line = "I found " + poi.DisplayName + " — water rising warm out of the cold earth, " +
                           "breathing steam into the morning. I stood in it to my belly and felt my bones " +
                           "unclench. The old ones must have known this place.";
                    salience = 0.8f; break;
                case PoiType.HollowLog:
                    line = "I found " + poi.DisplayName + " — a fallen giant, hollowed by years into a tunnel " +
                           "just my size. It smells of rain and mushrooms. The kits will love this.";
                    salience = 0.55f; break;
                case PoiType.RainbowGrove:
                    // SECRET. This should feel like a once-in-many-lives moment.
                    line = "I pushed through the pines where no trail goes, and the woods opened — and I " +
                           "forgot how to breathe. A grove of crystal, every color I have ever seen and some " +
                           "I have not, growing out of the moss like frozen light. Red as heart's blood, blue " +
                           "as deep water, gold as morning. They hummed when the wind touched them — a chord " +
                           "so pure it hurt, in the good way. I sat among them until the light changed and " +
                           "changed again, and I understood, the way you understand a smell from childhood: " +
                           "this place was waiting for me. It had been waiting a very long time.";
                    salience = 1.0f; break;
                case PoiType.Cairn:
                    line = "I reached the fork cairn. My kind have been adding stones here for years.";
                    salience = 0.4f; break;
                case PoiType.EmberHollow:
                    line = "I found a sheltered ember-hollow — " + poi.Name + ". Warm glow-moss. A good place to rest.";
                    salience = 0.4f; break;
                case PoiType.GlowberryBush:
                    line = "I found a glowberry bush, the berries lit faintly from within.";
                    salience = 0.3f; break;
                default:
                    line = "I discovered " + poi.DisplayName + ".";
                    salience = 0.4f; break;
            }
            Journal(Now, line, JournalCategory.Discovery, salience, 1f, poi.Id);
            // A discovered place may rhyme with a past dream — recognition.
            DreamSystem.CheckRecognition(this, poi.Id);
            if (poi.Type == PoiType.InsectileRuin)
                LineageSystem.MaybeDistillTale(this, "The Hollow Hive", "Caution", 0.04f,
                    "the first climb to the chitin arches");
            else if (salience >= 0.75f)
                LineageSystem.MaybeDistillTale(this, "The First " + poi.DisplayName, "Curiosity", 0.03f,
                    "the first time I saw " + poi.DisplayName);
            State.Beliefs.AddOrUpdate("poi." + poi.Id + ".seen",
                "I have seen " + poi.DisplayName, "saw", 1f, Now);
            // Discovery lifts the spirits — moreso for the truly wondrous places.
            float moodLift = 4f;
            if (poi.Type == PoiType.CrystalCave || poi.Type == PoiType.HotSpring) moodLift = 10f;
            else if (poi.Type == PoiType.Overlook || poi.Type == PoiType.InsectileRuin) moodLift = 8f;
            else if (poi.Type == PoiType.HollowLog) moodLift = 6f;
            else if (poi.Type == PoiType.RainbowGrove) moodLift = 20f; // once in many lives
            State.Agent.Mood = MathX.Clamp(State.Agent.Mood + moodLift, 0f, 100f);
        }

        private void InjuryHook()
        {
            var a = State.Agent;
            if (_prevHealth - a.Health > 15f && a.IsAlive)
            {
                Journal(Now, "I was hurt — properly hurt. I'll need to be careful for a while.",
                    JournalCategory.Combat, 0.65f);
                a.Traits.Nudge("Caution", 0.02f);
            }
            _prevHealth = a.Health;
        }

        // -- helpers ---------------------------------------------------------------------

        public void Journal(float time, string text, JournalCategory category,
                            float salience, float certainty = 1f,
                            int? placeId = null, int? personId = null, string source = "self")
        {
            State.Journal.Add(time, text, category, salience, certainty, placeId, personId, source,
                              State.Lineage.Generation);
        }

        public void KillAgent(string cause)
        {
            // SaveSystem writes the chapter-close entry and runs succession —
            // death is never game over.
            SaveSystem.KillAgent(State, cause);
            LineageRng = new SeededRandom(State.Rng.Lineage);
            ColossusRng = new SeededRandom(State.Rng.Colossus);
            DreamRng = new SeededRandom(State.Rng.Dream);
        }
    }
}
