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

        /// <summary>Game seconds advanced per real second.</summary>
        public float TimeScale = 60f;

        /// <summary>Last auto-save checkpoint (transient; not part of the save).</summary>
        public string LastCheckpointJson = "";

        private int _nextEntityId = 1;
        private float _lastCheckpointAt = -9999f;
        private float _prevHealth = 100f;

        private static readonly string[] VillagerNames =
            { "Mira", "Tam", "Elspeth", "Donal", "Ailsa", "Fergus", "Nessa" };

        public Simulation(GameState state)
        {
            State = state ?? throw new ArgumentNullException("state");
            Brain = new AgentBrain();
            AiRng = new SeededRandom(state.Rng.Ai);
            EventRng = new SeededRandom(state.Rng.Event);
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
        }

        public int NextEntityId() { return _nextEntityId++; }

        public float Now { get { return State.ElapsedSeconds; } }

        // -- life factory ----------------------------------------------------------

        /// <summary>Creates a brand-new life: world, agent, ecology, first journal line.</summary>
        public static Simulation NewLife(int seed)
        {
            var world = WorldGenerator.Generate(new WorldConfig { Seed = seed });
            var ai = SeededRandom.Derive(seed, "ai");
            var ev = SeededRandom.Derive(seed, "event");

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
                if (poi.Type == PoiType.Hamlet || poi.Type == PoiType.Campfire)
                    agent.KnownPoiIds.Add(poi.Id);
            state.Agent = agent;

            state.Rng.Ai = ai.State;
            state.Rng.Event = ev.State;

            var sim = new Simulation(state);
            sim.PlaceEcology();

            state.Journal.Add(0f,
                "I woke on the heather above " + world.HamletName + " as the mist lifted. " +
                "A new life, and " + world.ValleyName + " waiting to be learned.",
                JournalCategory.Reflection, 0.8f);
            state.Beliefs.AddOrUpdate("home.hamlet", world.HamletName + " is home", "saw", 1f, 0f);

            sim.SyncRng();
            return sim;
        }

        private void PlaceEcology()
        {
            var world = State.World;
            var ev = EventRng;
            PointOfInterest hamlet = null;
            foreach (var p in world.Pois)
                if (p.Type == PoiType.Hamlet) hamlet = p;

            // Villagers: a small cast with names, keeping near home.
            var names = new List<string>(VillagerNames);
            ev.Shuffle(names);
            int villagers = Math.Min(5, names.Count);
            for (int i = 0; i < villagers; i++)
            {
                V2 spot = RandomLandNear(ev, hamlet.X, hamlet.Z, 8f, 26f, 0f, 20f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Villager,
                    Name = names[i],
                    X = spot.X, Z = spot.Z,
                    Health = 100f,
                    Behavior = "Wander",
                    HomeX = hamlet.X, HomeZ = hamlet.Z,
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
                V2 spot = RandomLandNear(ev, hamlet.X, hamlet.Z, 20f, 180f, 2f, 24f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Rabbit, Name = "hare",
                    X = spot.X, Z = spot.Z, Health = 100f, Behavior = "Hop",
                    HomeX = spot.X, HomeZ = spot.Z, TargetX = spot.X, TargetZ = spot.Z
                });
            }

            // Wolves: far from the hamlet, up in the woods and crags.
            for (int i = 0; i < 3; i++)
            {
                V2 spot = RandomLandNear(ev, hamlet.X, hamlet.Z, 160f, 260f, 10f, 45f);
                AddEntity(new EntityState
                {
                    Kind = EntityKind.Wolf, Name = "wolf",
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
            Brain.Tick(this, h);
            StepEntities(h);
            DiscoveryCheck();
            InjuryHook();
            State.Social.Decay(State.ElapsedSeconds, h); // stateless per-step: no chunk timer to persist

            if (State.ElapsedSeconds - _lastCheckpointAt > 300f)
            {
                _lastCheckpointAt = State.ElapsedSeconds;
                SyncRng();
                LastCheckpointJson = SaveSystem.Save(State);
            }
        }

        private void SyncRng()
        {
            State.Rng.Ai = AiRng.State;
            State.Rng.Event = EventRng.State;
        }

        // -- subsystems -----------------------------------------------------------------

        private void WeatherDrift()
        {
            if (State.ElapsedSeconds - State.WeatherChangedAt < 2700f) return; // ~45 game-min
            State.WeatherChangedAt = State.ElapsedSeconds;
            Weather next = SaveSystem.RollWeather(State.Weather, EventRng);
            if (next == State.Weather) return;
            State.Weather = next;
            switch (next)
            {
                case Weather.Rain:
                    Journal(Now, "Rain begins to fall over the glen.", JournalCategory.Weather, 0.3f);
                    break;
                case Weather.Storm:
                    Journal(Now, "A storm is coming down off the tops. I should think about shelter.", JournalCategory.Weather, 0.5f);
                    break;
                case Weather.Clear:
                    if (EventRng.NextFloat() < 0.4f)
                        Journal(Now, "The cloud broke and the glen filled with light.", JournalCategory.Weather, 0.25f);
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
                Now = Now,
                Rng = EventRng
            };
            foreach (var e in State.Entities)
                EntityBehaviors.Step(e, ctx, h);

            // Wolf bites on the agent surface here as journaled injuries.
            foreach (var line in ctx.EventLog)
            {
                if (line.StartsWith("bite:"))
                {
                    float dmg = float.Parse(line.Substring(5),
                        System.Globalization.CultureInfo.InvariantCulture);
                    Journal(Now, "A wolf got its teeth into me (" + dmg.ToString("F0") + "). I need to get away.",
                        JournalCategory.Combat, 0.7f);
                    State.Agent.Traits.Nudge("Caution", 0.01f);
                }
                else if (line.StartsWith("kill:"))
                {
                    Journal(Now, line.Substring(5), JournalCategory.Travel, 0.45f);
                }
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
                case PoiType.BrochRuin:
                    // Name unknown until learned — use DisplayName-safe wording.
                    line = "I climbed to the old tower on the fell. No one living built this place.";
                    salience = 0.9f; break;
                case PoiType.RuinSite:
                    line = "I found " + poi.DisplayName + ". Old stones, older stories.";
                    salience = 0.75f; break;
                case PoiType.Overlook:
                    line = "I climbed until the whole glen lay below me — river, loch, hamlet, and all.";
                    salience = 0.6f; break;
                case PoiType.Cairn:
                    line = "I reached the fork cairn. Travellers have been adding stones here for years.";
                    salience = 0.4f; break;
                case PoiType.Campfire:
                    line = "I found a sheltered campfire site — " + poi.Name + ". A good place to rest.";
                    salience = 0.4f; break;
                case PoiType.BerryBush:
                    line = "I found a berry bush, heavy with fruit.";
                    salience = 0.3f; break;
                default:
                    line = "I discovered " + poi.DisplayName + ".";
                    salience = 0.4f; break;
            }
            Journal(Now, line, JournalCategory.Discovery, salience, 1f, poi.Id);
            State.Beliefs.AddOrUpdate("poi." + poi.Id + ".seen",
                "I have seen " + poi.DisplayName, "saw", 1f, Now);
            State.Agent.Mood = MathX.Clamp(State.Agent.Mood + 4f, 0f, 100f);
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
            State.Journal.Add(time, text, category, salience, certainty, placeId, personId, source);
        }

        public void KillAgent(string cause)
        {
            SaveSystem.KillAgent(State, cause);
        }
    }
}
