// Solace.Core — the autonomous mind.
//
// A layered cognition stack in miniature: needs decay, perception builds
// observations, utility-scored actions arbitrate, steering executes movement.
// Every decision leaves a DecisionTrace so the choice stays legible.
// Player suggestions arrive as bounded social evidence — never orders.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    /// <summary>
    /// Temperament dimensions, 0..1. Seeded per life; experience nudges them
    /// slowly within credible bounds. Personality is pressure, not a costume.
    /// </summary>
    public class Personality
    {
        public float Caution;
        public float Curiosity;
        public float Sociability;
        public float Compassion;
        public float Pride;
        public float Patience;

        public static Personality Generate(SeededRandom rng)
        {
            var p = new Personality();
            p.Caution = ClampT(0.5f + rng.NextGaussian() * 0.18f);
            p.Curiosity = ClampT(0.5f + rng.NextGaussian() * 0.18f);
            p.Sociability = ClampT(0.5f + rng.NextGaussian() * 0.18f);
            p.Compassion = ClampT(0.5f + rng.NextGaussian() * 0.18f);
            p.Pride = ClampT(0.5f + rng.NextGaussian() * 0.18f);
            p.Patience = ClampT(0.5f + rng.NextGaussian() * 0.18f);
            return p;
        }

        private static float ClampT(float v) { return MathX.Clamp(v, 0.05f, 0.95f); }

        /// <summary>Slow experience-driven shift. Delta is small by design.</summary>
        public void Nudge(string trait, float delta)
        {
            switch (trait)
            {
                case "Caution": Caution = ClampT(Caution + delta); break;
                case "Curiosity": Curiosity = ClampT(Curiosity + delta); break;
                case "Sociability": Sociability = ClampT(Sociability + delta); break;
                case "Compassion": Compassion = ClampT(Compassion + delta); break;
                case "Pride": Pride = ClampT(Pride + delta); break;
                case "Patience": Patience = ClampT(Patience + delta); break;
            }
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("caution", Caution);
            o.Add("curiosity", Curiosity);
            o.Add("sociability", Sociability);
            o.Add("compassion", Compassion);
            o.Add("pride", Pride);
            o.Add("patience", Patience);
            return o;
        }

        public static Personality FromJson(JsonObject o)
        {
            var p = new Personality();
            p.Caution = JsonHelpers.GetFloat(o, "caution", 0.5f);
            p.Curiosity = JsonHelpers.GetFloat(o, "curiosity", 0.5f);
            p.Sociability = JsonHelpers.GetFloat(o, "sociability", 0.5f);
            p.Compassion = JsonHelpers.GetFloat(o, "compassion", 0.5f);
            p.Pride = JsonHelpers.GetFloat(o, "pride", 0.5f);
            p.Patience = JsonHelpers.GetFloat(o, "patience", 0.5f);
            return p;
        }
    }

    /// <summary>
    /// A player suggestion as social evidence: weighted consideration, never a command.
    /// </summary>
    public class PlayerInfluence
    {
        public string Text;
        public string Topic;   // "eat","drink","rest","explore","social","safety","ruin","watch"
        public float Weight;   // 0..1 at creation; applied bounded
        public float Time;     // game seconds when received

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("text", Text);
            o.Add("topic", Topic);
            o.Add("weight", Weight);
            o.Add("time", Time);
            return o;
        }

        public static PlayerInfluence FromJson(JsonObject o)
        {
            return new PlayerInfluence
            {
                Text = JsonHelpers.GetString(o, "text", ""),
                Topic = JsonHelpers.GetString(o, "topic", ""),
                Weight = JsonHelpers.GetFloat(o, "weight", 0f),
                Time = JsonHelpers.GetFloat(o, "time", 0f)
            };
        }
    }

    /// <summary>One scored component of a utility decision (0..1, signed weight).</summary>
    public class ScoreComponent
    {
        public string Name;
        public float Value;
        public float Weight;
        public float Contribution { get { return Value * Weight; } }
    }

    /// <summary>Legibility record: what was chosen and why.</summary>
    public class DecisionTrace
    {
        public string ActionName = "";
        public List<ScoreComponent> Components = new List<ScoreComponent>();
        public float TotalScore;
        public string Reason = "";

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("action", ActionName);
            var ca = new JsonArray();
            foreach (var c in Components)
            {
                var co = new JsonObject();
                co.Add("name", c.Name);
                co.Add("value", c.Value);
                co.Add("weight", c.Weight);
                ca.Add(co);
            }
            o.Add("components", ca);
            o.Add("total", TotalScore);
            o.Add("reason", Reason);
            return o;
        }

        public static DecisionTrace FromJson(JsonObject o)
        {
            var t = new DecisionTrace();
            t.ActionName = JsonHelpers.GetString(o, "action", "");
            var ca = o["components"].AsArray();
            for (int i = 0; i < ca.Count; i++)
            {
                var co = ca[i].AsObject();
                t.Components.Add(new ScoreComponent
                {
                    Name = JsonHelpers.GetString(co, "name", ""),
                    Value = JsonHelpers.GetFloat(co, "value", 0f),
                    Weight = JsonHelpers.GetFloat(co, "weight", 0f)
                });
            }
            t.TotalScore = JsonHelpers.GetFloat(o, "total", 0f);
            t.Reason = JsonHelpers.GetString(o, "reason", "");
            return t;
        }
    }

    public class Observation
    {
        public string Kind;      // "poi", "predator", "deer", "rabbit", "kindred"
        public int RefId;        // POI id or entity id
        public float X, Z;
        public float Distance;
        public float Salience;   // 0..1
    }

    /// <summary>Solace's embodied state: a lantern-fox of the lineage.</summary>
    public class AgentState
    {
        public int Id = 1;
        public string Name = "Solace";
        public float X, Z;
        public float Facing;   // yaw radians, 0 = +Z
        public float Speed;
        public bool IsAlive = true;

        // Needs: Energy 0..100 (100=blazing). ENERGY IS LIGHT — the creature's
        // luminous core; everything about vigor and sickness reads in the glow.
        // Hunger 0..100 (100=starving). Thirst 0..100 (100=parched).
        // Health 0..100. Mood 0..100 (50=neutral). Curiosity 0..100 (100=restless).
        public float Energy = 80f;
        public float Hunger = 25f;
        public float Thirst = 20f;
        public float Health = 100f;
        public float Mood = 55f;
        public float Curiosity = 60f;

        // Lineage: age in game-years, sickness of the light, the inherited hue.
        public float Age = 4f;
        public float LifespanYears = 16f;   // seeded ~14-20 per life
        public SicknessKind Sickness = SicknessKind.None;
        public float SicknessSeverity;      // 0..1
        public bool SicknessWarned;
        public float SicknessWarnedAt = -9999f;
        public float LightShade = 0.5f;     // inherited hue 0..1 (ember-gold → moonlit blue)
        public Bond Bond;                   // pair-bond, if any (null = unbonded)
        public bool IsProtagonist = true;
        public int Generation = 1;
        public int MotherId = -1;           // kindred entity id, if known
        public int FatherId = -1;           // agent id, if known
        public List<int> TalesKnown = new List<int>(); // tale ids learned
        public bool VigorForeshadowed;      // old-age dimming has been journaled
        public float LastTaleToldAt = -9999f;

        /// <summary>Life stage derived from age and lifespan.</summary>
        public LifeStage Stage { get { return LineageSystem.StageFor(Age, LifespanYears); } }

        /// <summary>
        /// THE readable signal: 0..1 glow of the chest-core, from light
        /// (energy), health, and sickness. Dimming reads here first.
        /// </summary>
        public float Glow
        {
            get
            {
                float g = 0.5f * (Energy / 100f) + 0.3f * (Health / 100f)
                        + 0.2f * (1f - SicknessSeverity);
                return MathX.Clamp01(g);
            }
        }

        /// <summary>Max light declines in the elder years.</summary>
        public float MaxEnergy
        {
            get
            {
                if (Stage != LifeStage.Elder) return 100f;
                float elderStart = LifespanYears * 0.65f;
                return Math.Max(60f, 100f - (Age - elderStart) * 8f);
            }
        }

        /// <summary>Speed factor by stage: kits are quick but small, elders slow.</summary>
        public float MaxSpeedFactor
        {
            get
            {
                switch (Stage)
                {
                    case LifeStage.Kit: return 0.7f;
                    case LifeStage.Juvenile: return 0.9f;
                    case LifeStage.Elder:
                        return Math.Max(0.55f, 0.8f - (Age - LifespanYears * 0.65f) * 0.05f);
                    default: return 1f;
                }
            }
        }

        /// <summary>Caution with the stage's pressure applied (elders warier).</summary>
        public float EffectiveCaution
        {
            get
            {
                float c = Traits.Caution;
                if (Stage == LifeStage.Elder) c += 0.15f;
                else if (Stage == LifeStage.Kit) c -= 0.15f;
                else if (Stage == LifeStage.Juvenile) c -= 0.05f;
                return MathX.Clamp01(c);
            }
        }

        /// <summary>Curiosity with the stage's pressure applied (kits burn curious).</summary>
        public float EffectiveCuriosity
        {
            get { return MathX.Clamp(Curiosity + (Stage == LifeStage.Kit ? 25f : Stage == LifeStage.Juvenile ? 10f : Stage == LifeStage.Elder ? -10f : 0f), 0f, 100f); }
        }

        public string CurrentGoal = "";
        public string CurrentActivity = "waking up";

        public Personality Traits = new Personality();
        public List<PlayerInfluence> Influences = new List<PlayerInfluence>();

        // Movement target (steering).
        public bool HasMoveTarget;
        public float TargetX, TargetZ;
        public float ArriveRadius = 3f;
        public bool RunToTarget;

        // Action target refs.
        public int TargetPoiId = -1;
        public int TargetEntityId = -1;

        public float DecisionTimer;
        public float ActionTimer;   // per-action progress clock
        public float FleeUntil;     // game time until which fleeing is locked in
        public bool InCombat;
        public int CombatTargetId = -1;

        public HashSet<int> KnownPoiIds = new HashSet<int>();
        public int LastObservedEntityId = -1;
        public float LastObservedTime = -9999f;

        public V2 Pos { get { return new V2(X, Z); } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("name", Name);
            o.Add("x", X); o.Add("z", Z);
            o.Add("facing", Facing); o.Add("speed", Speed);
            o.Add("isAlive", IsAlive);
            o.Add("energy", Energy); o.Add("hunger", Hunger); o.Add("thirst", Thirst);
            o.Add("health", Health); o.Add("mood", Mood); o.Add("curiosity", Curiosity);
            o.Add("age", Age); o.Add("lifespanYears", LifespanYears);
            o.Add("sickness", Sickness.ToString());
            o.Add("sicknessSeverity", SicknessSeverity);
            o.Add("sicknessWarned", SicknessWarned);
            o.Add("sicknessWarnedAt", SicknessWarnedAt);
            o.Add("lightShade", LightShade);
            o.Add("bond", Bond == null ? (JsonValue)JsonNull.Instance : (JsonValue)Bond.ToJson());
            o.Add("isProtagonist", IsProtagonist);
            o.Add("generation", Generation);
            o.Add("motherId", MotherId); o.Add("fatherId", FatherId);
            var ta = new JsonArray();
            var sortedTales = new List<int>(TalesKnown);
            sortedTales.Sort();
            foreach (var id in sortedTales) ta.Add(id);
            o.Add("talesKnown", ta);
            o.Add("vigorForeshadowed", VigorForeshadowed);
            o.Add("lastTaleToldAt", LastTaleToldAt);
            o.Add("goal", CurrentGoal); o.Add("activity", CurrentActivity);
            o.Add("traits", Traits.ToJson());
            var ia = new JsonArray();
            foreach (var inf in Influences) ia.Add(inf.ToJson());
            o.Add("influences", ia);
            o.Add("hasMoveTarget", HasMoveTarget);
            o.Add("targetX", TargetX); o.Add("targetZ", TargetZ);
            o.Add("arriveRadius", ArriveRadius); o.Add("runToTarget", RunToTarget);
            o.Add("targetPoiId", TargetPoiId); o.Add("targetEntityId", TargetEntityId);
            o.Add("decisionTimer", DecisionTimer); o.Add("actionTimer", ActionTimer);
            o.Add("fleeUntil", FleeUntil); o.Add("inCombat", InCombat);
            o.Add("combatTargetId", CombatTargetId);
            var ka = new JsonArray();
            var sorted = new List<int>(KnownPoiIds);
            sorted.Sort();
            foreach (var id in sorted) ka.Add(id);
            o.Add("knownPoiIds", ka);
            o.Add("lastObservedEntityId", LastObservedEntityId);
            o.Add("lastObservedTime", LastObservedTime);
            return o;
        }

        public static AgentState FromJson(JsonObject o)
        {
            var a = new AgentState();
            a.Id = JsonHelpers.GetInt(o, "id", 1);
            a.Name = JsonHelpers.GetString(o, "name", "Solace");
            a.X = JsonHelpers.GetFloat(o, "x", 0f); a.Z = JsonHelpers.GetFloat(o, "z", 0f);
            a.Facing = JsonHelpers.GetFloat(o, "facing", 0f); a.Speed = JsonHelpers.GetFloat(o, "speed", 0f);
            a.IsAlive = JsonHelpers.GetBool(o, "isAlive", true);
            a.Energy = JsonHelpers.GetFloat(o, "energy", 80f);
            a.Hunger = JsonHelpers.GetFloat(o, "hunger", 25f);
            a.Thirst = JsonHelpers.GetFloat(o, "thirst", 20f);
            a.Health = JsonHelpers.GetFloat(o, "health", 100f);
            a.Mood = JsonHelpers.GetFloat(o, "mood", 55f);
            a.Curiosity = JsonHelpers.GetFloat(o, "curiosity", 60f);
            a.Age = JsonHelpers.GetFloat(o, "age", 4f);
            a.LifespanYears = JsonHelpers.GetFloat(o, "lifespanYears", 16f);
            JsonValue sv;
            a.Sickness = o.TryGet("sickness", out sv) && !sv.IsNull
                ? (SicknessKind)Enum.Parse(typeof(SicknessKind), sv.AsString()) : SicknessKind.None;
            a.SicknessSeverity = JsonHelpers.GetFloat(o, "sicknessSeverity", 0f);
            a.SicknessWarned = JsonHelpers.GetBool(o, "sicknessWarned", false);
            a.SicknessWarnedAt = JsonHelpers.GetFloat(o, "sicknessWarnedAt", -9999f);
            a.LightShade = JsonHelpers.GetFloat(o, "lightShade", 0.5f);
            JsonValue bv;
            a.Bond = o.TryGet("bond", out bv) && !bv.IsNull ? Bond.FromJson(bv.AsObject()) : null;
            a.IsProtagonist = JsonHelpers.GetBool(o, "isProtagonist", true);
            a.Generation = JsonHelpers.GetInt(o, "generation", 1);
            a.MotherId = JsonHelpers.GetInt(o, "motherId", -1);
            a.FatherId = JsonHelpers.GetInt(o, "fatherId", -1);
            JsonValue tkv;
            if (o.TryGet("talesKnown", out tkv) && !tkv.IsNull)
            {
                var tar = tkv.AsArray();
                for (int i = 0; i < tar.Count; i++) a.TalesKnown.Add(((JsonNumber)tar[i]).AsInt());
            }
            a.VigorForeshadowed = JsonHelpers.GetBool(o, "vigorForeshadowed", false);
            a.LastTaleToldAt = JsonHelpers.GetFloat(o, "lastTaleToldAt", -9999f);
            a.CurrentGoal = JsonHelpers.GetString(o, "goal", "");
            a.CurrentActivity = JsonHelpers.GetString(o, "activity", "waking up");
            JsonValue tv;
            a.Traits = o.TryGet("traits", out tv) ? Personality.FromJson(tv.AsObject()) : new Personality();
            var ia = o["influences"].AsArray();
            for (int i = 0; i < ia.Count; i++) a.Influences.Add(PlayerInfluence.FromJson(ia[i].AsObject()));
            a.HasMoveTarget = JsonHelpers.GetBool(o, "hasMoveTarget", false);
            a.TargetX = JsonHelpers.GetFloat(o, "targetX", 0f);
            a.TargetZ = JsonHelpers.GetFloat(o, "targetZ", 0f);
            a.ArriveRadius = JsonHelpers.GetFloat(o, "arriveRadius", 3f);
            a.RunToTarget = JsonHelpers.GetBool(o, "runToTarget", false);
            a.TargetPoiId = JsonHelpers.GetInt(o, "targetPoiId", -1);
            a.TargetEntityId = JsonHelpers.GetInt(o, "targetEntityId", -1);
            a.DecisionTimer = JsonHelpers.GetFloat(o, "decisionTimer", 0f);
            a.ActionTimer = JsonHelpers.GetFloat(o, "actionTimer", 0f);
            a.FleeUntil = JsonHelpers.GetFloat(o, "fleeUntil", 0f);
            a.InCombat = JsonHelpers.GetBool(o, "inCombat", false);
            a.CombatTargetId = JsonHelpers.GetInt(o, "combatTargetId", -1);
            var ka = o["knownPoiIds"].AsArray();
            for (int i = 0; i < ka.Count; i++) a.KnownPoiIds.Add(((JsonNumber)ka[i]).AsInt());
            a.LastObservedEntityId = JsonHelpers.GetInt(o, "lastObservedEntityId", -1);
            a.LastObservedTime = JsonHelpers.GetFloat(o, "lastObservedTime", -9999f);
            return a;
        }
    }

    /// <summary>Everything an action needs to score and execute. Built per decision.</summary>
    public class BrainContext
    {
        public Simulation Sim;
        public SeededRandom Ai;
        public SeededRandom Ev;
        public float Now;
        public List<Observation> Observations = new List<Observation>();

        public GameState State { get { return Sim.State; } }
        public AgentState Agent { get { return Sim.State.Agent; } }
        public WorldData World { get { return Sim.State.World; } }

        public EntityState FindEntity(int id)
        {
            var list = State.Entities;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return list[i];
            return null;
        }

        /// <summary>Nearest living gloom-maw and its distance; null if none within maxDist.</summary>
        public EntityState NearestPredator(float maxDist, out float dist)
        {
            dist = float.MaxValue;
            EntityState best = null;
            var list = State.Entities;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.Kind != EntityKind.Predator || e.Health <= 0) continue;
                float d = V2.Distance(Agent.Pos, new V2(e.X, e.Z));
                if (d < maxDist && d < dist) { dist = d; best = e; }
            }
            return best;
        }

        public int CountPredators(float maxDist)
        {
            int n = 0;
            var list = State.Entities;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.Kind != EntityKind.Predator || e.Health <= 0) continue;
                if (V2.Distance(Agent.Pos, new V2(e.X, e.Z)) < maxDist) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// The autonomous mind. Tick() advances needs, perception, arbitration, and
    /// steering. Same state + same stream positions → same decisions.
    /// </summary>
    public class AgentBrain
    {
        public const float SightRadius = 80f;
        public const float DecisionInterval = 4f; // game-seconds between arbitrations

        private readonly List<AgentAction> _actions = new List<AgentAction>();
        public DecisionTrace LastDecisionTrace = new DecisionTrace();
        public string CurrentActionName = "";

        public AgentBrain()
        {
            _actions.Add(new FleeAction());
            _actions.Add(new FightAction());
            _actions.Add(new EatAction());
            _actions.Add(new DrinkAction());
            _actions.Add(new RestAction());
            _actions.Add(new ExplorePoiAction());
            _actions.Add(new ObserveWildlifeAction());
            _actions.Add(new GreetKindredAction());
            _actions.Add(new SeekBondAction());
            _actions.Add(new LootRuinAction());
        }

        public AgentAction GetAction(string name)
        {
            for (int i = 0; i < _actions.Count; i++)
                if (_actions[i].Name == name) return _actions[i];
            return null;
        }

        /// <summary>One brain tick of dt game-seconds. Called every fixed step.</summary>
        public void Tick(Simulation sim, float dt)
        {
            var agent = sim.State.Agent;
            if (!agent.IsAlive) return;

            var ctx = new BrainContext
            {
                Sim = sim,
                Ai = sim.AiRng,
                Ev = sim.EventRng,
                Now = sim.State.ElapsedSeconds
            };

            DecayNeeds(agent, dt, sim);
            Perceive(ctx);

            agent.DecisionTimer -= dt;
            bool mustDecide = agent.DecisionTimer <= 0 || string.IsNullOrEmpty(CurrentActionName);
            if (mustDecide)
            {
                Decide(ctx);
                agent.DecisionTimer = DecisionInterval;
            }

            var action = GetAction(CurrentActionName);
            if (action != null)
                action.Update(ctx, dt);

            Steer(sim, agent, dt);
            agent.ActionTimer += dt;
        }

        /// <summary>Runs arbitration immediately and begins the winning action.</summary>
        public string Decide(BrainContext ctx)
        {
            var agent = ctx.Agent;
            Perceive(ctx);

            bool critical = agent.Health < 28f || agent.Hunger > 88f || agent.Thirst > 88f || agent.Energy < 10f;
            bool fleeLocked = ctx.Now < agent.FleeUntil;

            string bestName = null;
            float bestScore = float.MinValue;
            List<ScoreComponent> bestComponents = null;

            for (int i = 0; i < _actions.Count; i++)
            {
                var a = _actions[i];
                if (critical && !a.IsSurvival) continue;
                if (fleeLocked && !(a is FleeAction) && !(a is FightAction)) continue;
                if (!a.CanScore(ctx)) continue;

                var comps = new List<ScoreComponent>();
                float score = a.Score(ctx, comps);

                // Player influence: bounded social evidence, never overriding survival.
                if (!critical)
                {
                    float infl = InfluenceBonus(ctx, a.Topic);
                    if (infl > 0.001f)
                    {
                        comps.Add(new ScoreComponent { Name = "suggestion", Value = infl, Weight = 1f });
                        score += infl;
                    }
                }

                // Tiny deterministic noise breaks close ties only.
                score += ctx.Ai.NextFloat(0f, 0.035f);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestName = a.Name;
                    bestComponents = comps;
                }
            }

            if (bestName == null)
            {
                // Nothing scores: idle and recover.
                CurrentActionName = "";
                agent.CurrentGoal = "";
                agent.CurrentActivity = "catching my breath";
                agent.HasMoveTarget = false;
                LastDecisionTrace = new DecisionTrace
                {
                    ActionName = "(idle)",
                    TotalScore = 0f,
                    Reason = "Nothing needed doing; I stood a while."
                };
                ctx.Sim.State.LastDecision = LastDecisionTrace;
                return "(idle)";
            }

            var winner = GetAction(bestName);
            CurrentActionName = bestName;
            agent.ActionTimer = 0f;
            winner.Begin(ctx);

            LastDecisionTrace = new DecisionTrace
            {
                ActionName = bestName,
                Components = bestComponents,
                TotalScore = bestScore,
                Reason = winner.DescribeReason(ctx)
            };
            // Keep the trace on the state so conversation can answer "why".
            ctx.Sim.State.LastDecision = LastDecisionTrace;
            return bestName;
        }

        private float InfluenceBonus(BrainContext ctx, string topic)
        {
            float bonus = 0f;
            var list = ctx.Agent.Influences;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var inf = list[i];
                // Suggestions fade after six game-hours.
                if (ctx.Now - inf.Time > 6f * 3600f)
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (inf.Topic == topic)
                    bonus += inf.Weight;
            }
            // Bounded: a suggestion can matter, never command.
            return Math.Min(bonus, 0.3f);
        }

        private void DecayNeeds(AgentState a, float dt, Simulation sim)
        {
            bool moving = a.Speed > 0.3f;
            a.Energy = MathX.Clamp(a.Energy - dt * (moving ? 0.10f : 0.035f), 0f, a.MaxEnergy);
            a.Hunger = MathX.Clamp(a.Hunger + dt * 0.045f, 0f, 100f);
            a.Thirst = MathX.Clamp(a.Thirst + dt * 0.06f, 0f, 100f);

            if (a.Hunger > 95f) a.Health -= dt * 0.25f;
            if (a.Thirst > 95f) a.Health -= dt * 0.30f;
            if (a.Hunger < 55f && a.Thirst < 55f && a.Energy > 20f)
                a.Health = Math.Min(100f, a.Health + dt * 0.12f);

            // Mood drifts toward neutral; curiosity grows when idle (kits burn curious).
            a.Mood += (50f - a.Mood) * dt * 0.01f;
            a.Mood = MathX.Clamp(a.Mood, 0f, 100f);
            float curiosityRate = a.Stage == LifeStage.Kit ? 0.06f : 0.03f;
            a.Curiosity = MathX.Clamp(a.Curiosity + dt * (moving ? curiosityRate * 0.27f : curiosityRate), 0f, 100f);

            a.Health = MathX.Clamp(a.Health, 0f, 100f);
            if (a.Health <= 0f && a.IsAlive)
                sim.KillAgent("My strength gave out.");
        }

        private void Perceive(BrainContext ctx)
        {
            ctx.Observations.Clear();
            var agent = ctx.Agent;
            var pos = agent.Pos;

            foreach (var poi in ctx.World.Pois)
            {
                float d = V2.Distance(pos, new V2(poi.X, poi.Z));
                if (d > SightRadius) continue;
                float salience = poi.Discovered ? 0.25f : 0.9f;
                if (poi.Type == PoiType.GlowberryBush && poi.Stock > 0 && agent.Hunger > 40f)
                    salience = Math.Max(salience, 0.75f);
                ctx.Observations.Add(new Observation
                {
                    Kind = "poi", RefId = poi.Id, X = poi.X, Z = poi.Z,
                    Distance = d, Salience = salience
                });
            }

            foreach (var e in ctx.State.Entities)
            {
                if (e.Health <= 0) continue;
                float d = V2.Distance(pos, new V2(e.X, e.Z));
                if (d > SightRadius) continue;
                string kind = e.Kind.ToString().ToLowerInvariant();
                float salience = 0.4f;
                if (e.Kind == EntityKind.Predator) salience = 1f;
                else if (e.Kind == EntityKind.Kindred) salience = 0.65f;
                ctx.Observations.Add(new Observation
                {
                    Kind = kind, RefId = e.Id, X = e.X, Z = e.Z,
                    Distance = d, Salience = salience
                });
            }

            // Attention: keep the most salient few.
            ctx.Observations.Sort((a, b) => b.Salience.CompareTo(a.Salience));
            if (ctx.Observations.Count > 8)
                ctx.Observations.RemoveRange(8, ctx.Observations.Count - 8);
        }

        /// <summary>
        /// Steering: move toward the target with arrival slowdown, repelled from
        /// water (unless going to drink) and from cliffs.
        /// </summary>
        public void Steer(Simulation sim, AgentState a, float dt)
        {
            if (!a.HasMoveTarget || !a.IsAlive)
            {
                a.Speed = Math.Max(0f, a.Speed - dt * 6f);
                return;
            }

            var world = sim.State.World;
            V2 target = new V2(a.TargetX, a.TargetZ);
            V2 to = target - a.Pos;
            float dist = to.Length();
            if (dist < Math.Max(a.ArriveRadius, 0.5f))
            {
                a.HasMoveTarget = false;
                a.Speed = 0f;
                return;
            }

            V2 dir = to / dist;
            float energyFactor = 0.6f + 0.4f * (a.Energy / 100f);
            float speed = (a.RunToTarget ? 5.2f : 2.3f) * energyFactor;
            speed *= a.MaxSpeedFactor;                          // age: elders slow
            speed *= 1f - 0.35f * a.SicknessSeverity;            // sickness drags
            speed *= MathX.Clamp01(dist / 7f); // arrival
            speed = Math.Max(speed, 0.4f);

            bool goingToDrink = CurrentActionName == "Drink";
            V2 probe = a.Pos + dir * 4f;

            // Water repulsion (unless deliberately approaching water to drink).
            if (world.IsWater(probe.X, probe.Z) && !(goingToDrink && dist < 10f))
            {
                V2 left = Rot(dir, 0.7f), right = Rot(dir, -0.7f);
                V2 pl = a.Pos + left * 4f, pr = a.Pos + right * 4f;
                bool lWet = world.IsWater(pl.X, pl.Z);
                bool rWet = world.IsWater(pr.X, pr.Z);
                if (lWet && rWet) { a.Speed = 0f; return; } // boxed in: wait
                dir = lWet ? right : left;
            }

            // Cliff repulsion.
            V2 probe2 = a.Pos + dir * 4f;
            if (world.SlopeAt(probe2.X, probe2.Z) > 1.0f)
            {
                V2 left = Rot(dir, 0.9f), right = Rot(dir, -0.9f);
                float sl = world.SlopeAt((a.Pos + left * 4f).X, (a.Pos + left * 4f).Z);
                float sr = world.SlopeAt((a.Pos + right * 4f).X, (a.Pos + right * 4f).Z);
                dir = sl < sr ? left : right;
            }

            a.X += dir.X * speed * dt;
            a.Z += dir.Z * speed * dt;
            // Stay inside the world.
            float half = world.HalfSize - 4f;
            a.X = MathX.Clamp(a.X, -half, half);
            a.Z = MathX.Clamp(a.Z, -half, half);
            a.Facing = MathX.YawFromDir(dir);
            a.Speed = speed;
        }

        private static V2 Rot(V2 v, float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new V2(v.X * c - v.Z * s, v.X * s + v.Z * c);
        }
    }
}

namespace Solace.Core
{
    /// <summary>
    /// One candidate behavior. Stateless across decisions; per-decision targets
    /// are resolved fresh in CanScore/Score and stored on AgentState in Begin.
    /// </summary>
    public abstract class AgentAction
    {
        public abstract string Name { get; }
        public abstract string Topic { get; }
        /// <summary>True for actions that answer critical bodily needs.</summary>
        public virtual bool IsSurvival { get { return false; } }

        public abstract bool CanScore(BrainContext ctx);
        /// <summary>Fills components (0..1 values, signed weights); returns the total.</summary>
        public abstract float Score(BrainContext ctx, List<ScoreComponent> components);
        public abstract void Begin(BrainContext ctx);
        public abstract void Update(BrainContext ctx, float dt);
        public virtual string DescribeReason(BrainContext ctx) { return "It seemed the right thing to do."; }

        protected void Add(List<ScoreComponent> c, string name, float value, float weight)
        {
            c.Add(new ScoreComponent { Name = name, Value = MathX.Clamp01(value), Weight = weight });
        }

        protected float Total(List<ScoreComponent> c)
        {
            float s = 0f;
            foreach (var x in c) s += x.Contribution;
            return s;
        }

        protected void MoveTo(BrainContext ctx, float x, float z, float arriveRadius, bool run)
        {
            var a = ctx.Agent;
            a.HasMoveTarget = true;
            a.TargetX = x; a.TargetZ = z;
            a.ArriveRadius = arriveRadius;
            a.RunToTarget = run;
        }

        protected void Stop(BrainContext ctx)
        {
            var a = ctx.Agent;
            a.HasMoveTarget = false;
            a.RunToTarget = false;
        }

        /// <summary>How dangerous the nearest gloom-maws feel right now, 0..1.</summary>
        protected float PredatorDanger(BrainContext ctx, float radius)
        {
            float d;
            var predator = ctx.NearestPredator(radius, out d);
            if (predator == null) return 0f;
            float closeness = 1f - d / radius;
            float strength = predator.Health / 100f;
            int pack = ctx.CountPredators(radius);
            return MathX.Clamp01(closeness * (0.4f + 0.6f * strength) * (pack > 1 ? 1.3f : 1f));
        }
    }

    // ------------------------------------------------------------------ Flee

    public class FleeAction : AgentAction
    {
        public override string Name { get { return "Flee"; } }
        public override string Topic { get { return "safety"; } }
        public override bool IsSurvival { get { return true; } }

        public override bool CanScore(BrainContext ctx)
        {
            float d;
            var predator = ctx.NearestPredator(17f, out d);
            if (predator == null) return false;
            bool outmatched = predator.Health > 35f || ctx.Agent.Health < 55f || ctx.CountPredators(26f) > 1;
            return outmatched;
        }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            float d;
            ctx.NearestPredator(17f, out d);
            var a = ctx.Agent;
            Add(c, "danger", MathX.Clamp01(1f - d / 17f), 0.55f);
            Add(c, "vulnerability", 1f - a.Health / 100f, 0.25f);
            Add(c, "caution", a.EffectiveCaution, 0.20f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            float d;
            var predator = ctx.NearestPredator(30f, out d);
            a.FleeUntil = ctx.Now + 12f;
            a.InCombat = false;
            a.CurrentGoal = "Flee";
            if (predator != null)
            {
                V2 away = (a.Pos - new V2(predator.X, predator.Z)).Normalized();
                // Run toward the den if it is roughly away from the predator — home is safety.
                var den = FindDen(ctx);
                if (den != null)
                {
                    V2 toHome = (new V2(den.X, den.Z) - a.Pos).Normalized();
                    if (away.Dot(toHome) > 0.2f) away = (away + toHome * 0.7f).Normalized();
                }
                MoveTo(ctx, a.X + away.X * 55f, a.Z + away.Z * 55f, 6f, true);
                a.CurrentActivity = "running from the gloom-maw";
            }
            else
            {
                a.CurrentActivity = "getting to safety";
                Stop(ctx);
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            float d;
            var predator = ctx.NearestPredator(40f, out d);
            if (ctx.Now >= a.FleeUntil || predator == null || d > 34f)
            {
                a.FleeUntil = 0f;
                Stop(ctx);
                a.CurrentActivity = "catching my breath";
                a.DecisionTimer = 0f; // re-decide now that the danger passed
                ctx.Sim.Journal(ctx.Now, "I lost the gloom-maw and stood shaking in the heather.",
                    JournalCategory.Combat, 0.55f);
                // Surviving danger teaches caution; standing ground teaches pride.
                a.Traits.Nudge("Caution", 0.01f);
                // Outrunning a gloom-maw at close quarters becomes a tale.
                if (d < 12f)
                    LineageSystem.MaybeDistillTale(ctx.Sim, "Outrunning the Gloom", "Caution", 0.05f,
                        "outran a gloom-maw on the fell");
            }
            else if (predator != null && d < 20f)
            {
                // Keep running from the current threat position.
                V2 away = (a.Pos - new V2(predator.X, predator.Z)).Normalized();
                a.TargetX = a.X + away.X * 40f;
                a.TargetZ = a.Z + away.Z * 40f;
                a.HasMoveTarget = true;
                a.RunToTarget = true;
            }
        }

        public override string DescribeReason(BrainContext ctx)
        {
            return "A gloom-maw was too close and I was outmatched — running was the only sane choice.";
        }

        private PointOfInterest FindDen(BrainContext ctx)
        {
            foreach (var p in ctx.World.Pois)
                if (p.Type == PoiType.Den) return p;
            return null;
        }
    }

    // ------------------------------------------------------------------ Fight

    public class FightAction : AgentAction
    {
        public override string Name { get { return "Fight"; } }
        public override string Topic { get { return "safety"; } }
        public override bool IsSurvival { get { return true; } }

        public override bool CanScore(BrainContext ctx)
        {
            float d;
            var predator = ctx.NearestPredator(7f, out d);
            if (predator == null) return false;
            var a = ctx.Agent;
            bool strong = a.Health > 60f && ctx.Sim.State.Inventory.HasSword;
            bool desperate = d < 3.5f && ctx.Now < a.FleeUntil; // cornered while fleeing
            bool finishing = predator.Health < 32f && a.Health > 40f;
            return strong || desperate || finishing;
        }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            float d;
            var predator = ctx.NearestPredator(7f, out d);
            var a = ctx.Agent;
            bool armed = ctx.Sim.State.Inventory.HasSword;
            Add(c, "self-defense", d < 4f ? 0.85f : 0.45f, 0.35f);
            Add(c, "advantage", (armed ? 0.8f : 0.35f) * (1f - predator.Health / 100f * 0.5f), 0.30f);
            Add(c, "risk", (1f - a.Health / 100f) * (armed ? 0.7f : 1f), -0.25f);
            Add(c, "pride", a.Traits.Pride * 0.5f, 0.10f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            float d;
            var predator = ctx.NearestPredator(8f, out d);
            a.InCombat = true;
            a.CombatTargetId = predator != null ? predator.Id : -1;
            a.CurrentGoal = "Fight";
            a.CurrentActivity = "fighting the gloom-maw";
            Stop(ctx);
            ctx.Sim.Journal(ctx.Now, "The gloom-maw came on and I stood my ground.",
                JournalCategory.Combat, 0.6f);
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var predator = ctx.FindEntity(a.CombatTargetId);
            if (predator == null || predator.Health <= 0 || predator.Kind != EntityKind.Predator)
            {
                EndCombat(ctx, "The gloom-maw is down. My paws won't stop shaking.");
                return;
            }
            float d = V2.Distance(a.Pos, new V2(predator.X, predator.Z));
            if (d > 3.5f)
            {
                // Close the distance.
                MoveTo(ctx, predator.X, predator.Z, 2f, true);
                return;
            }
            Stop(ctx);
            a.Facing = MathX.YawFromDir(new V2(predator.X - a.X, predator.Z - a.Z));

            // Self-preservation overrides pride.
            if (a.Health < 22f)
            {
                a.InCombat = false;
                a.CombatTargetId = -1;
                ctx.Sim.Journal(ctx.Now, "I was losing — I broke away and ran.",
                    JournalCategory.Combat, 0.7f);
                ctx.Agent.Traits.Nudge("Caution", 0.02f);
                GetFlee(ctx).Begin(ctx);
                ctx.Sim.Brain.CurrentActionName = "Flee";
                return;
            }

            // One round every ~1.6 seconds.
            if (a.ActionTimer >= 1.6f)
            {
                a.ActionTimer = 0f;
                var round = Combat.ResolveRound(a, predator, ctx.Sim.State.Inventory.HasSword, ctx.Ai);
                ctx.Sim.Journal(ctx.Now, round.Log, JournalCategory.Combat, 0.35f);
                if (round.PredatorFlees || round.PredatorDies)
                {
                    predator.Behavior = round.PredatorDies ? "Dead" : "Flee";
                    predator.StateTimer = 6f;
                    string epitaph = round.PredatorDies
                        ? "It is over. The gloom-maw lies still, and I am sorry and alive."
                        : "It turned and ran, tail low. I let it go.";
                    EndCombat(ctx, epitaph);
                    a.Traits.Nudge("Pride", 0.015f);
                    a.Mood = MathX.Clamp(a.Mood + (round.PredatorDies ? -6f : 8f), 0f, 100f);
                    LineageSystem.MaybeDistillTale(ctx.Sim, "Standing Ground", "Pride", 0.05f,
                        "stood down a gloom-maw and lived");
                }
            }
        }

        private void EndCombat(BrainContext ctx, string line)
        {
            var a = ctx.Agent;
            a.InCombat = false;
            a.CombatTargetId = -1;
            Stop(ctx);
            a.DecisionTimer = 0f;
            ctx.Sim.Journal(ctx.Now, line, JournalCategory.Combat, 0.75f);
        }

        private FleeAction GetFlee(BrainContext ctx)
        {
            return (FleeAction)ctx.Sim.Brain.GetAction("Flee");
        }

        public override string DescribeReason(BrainContext ctx)
        {
            return "The gloom-maw was on me and I had the strength and the steel — better to end it than be run down.";
        }
    }

    // -------------------------------------------------------------------- Eat

    public class EatAction : AgentAction
    {
        public override string Name { get { return "Eat"; } }
        public override string Topic { get { return "eat"; } }
        public override bool IsSurvival { get { return true; } }

        public PointOfInterest TargetBush(BrainContext ctx)
        {
            PointOfInterest best = null;
            float bestD = float.MaxValue;
            foreach (var poi in ctx.World.Pois)
            {
                if (poi.Type != PoiType.GlowberryBush || poi.Stock <= 0) continue;
                if (!ctx.Agent.KnownPoiIds.Contains(poi.Id)) continue;
                float d = V2.Distance(ctx.Agent.Pos, new V2(poi.X, poi.Z));
                if (d < bestD) { bestD = d; best = poi; }
            }
            return best;
        }

        public override bool CanScore(BrainContext ctx)
        {
            if (ctx.Agent.Hunger < 20f) return false;
            return TargetBush(ctx) != null || ctx.Sim.State.Inventory.Bread > 0;
        }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var bush = TargetBush(ctx);
            float dist = bush != null ? V2.Distance(a.Pos, new V2(bush.X, bush.Z)) : 0f;
            Add(c, "hunger", a.Hunger / 100f, 0.45f);
            Add(c, "meal", bush != null ? 0.55f + 0.35f * (bush.Stock / 8f) : 0.6f, 0.25f);
            Add(c, "effort", dist / 150f, -0.15f);
            Add(c, "risk", PredatorDanger(ctx, 40f), -0.15f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            a.CurrentGoal = "Eat";
            var bush = TargetBush(ctx);
            if (bush != null)
            {
                a.TargetPoiId = bush.Id;
                MoveTo(ctx, bush.X, bush.Z, bush.Radius, false);
                a.CurrentActivity = "going to pick glowberries";
            }
            else
            {
                // Eat bread on the spot.
                a.TargetPoiId = -1;
                if (ctx.Sim.State.Inventory.EatBread())
                {
                    a.Hunger = MathX.Clamp(a.Hunger - 45f, 0f, 100f);
                    a.Mood = MathX.Clamp(a.Mood + 4f, 0f, 100f);
                    ctx.Sim.Journal(ctx.Now, "I sat down and ate a seedcake. Plain, and exactly right.",
                        JournalCategory.Survival, 0.25f);
                }
                Stop(ctx);
                a.DecisionTimer = 2f;
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var bush = ctx.World.GetPoi(a.TargetPoiId);
            if (bush == null || bush.Stock <= 0)
            {
                // Bush exhausted or gone: reconsider.
                if (ctx.Sim.State.Inventory.Bread > 0 && a.Hunger > 45f)
                {
                    ctx.Sim.State.Inventory.EatBread();
                    a.Hunger = MathX.Clamp(a.Hunger - 45f, 0f, 100f);
                    ctx.Sim.Journal(ctx.Now, "The bush was bare, so I ate a seedcake instead.",
                        JournalCategory.Survival, 0.25f);
                }
                a.DecisionTimer = 0f;
                return;
            }
            float d = V2.Distance(a.Pos, new V2(bush.X, bush.Z));
            if (d <= bush.Radius + 1.5f)
            {
                Stop(ctx);
                bush.Stock--;
                a.Hunger = MathX.Clamp(a.Hunger - 30f, 0f, 100f);
                a.Mood = MathX.Clamp(a.Mood + 3f, 0f, 100f);
                a.CurrentActivity = "picking glowberries";
                // Eating well feeds the light: a sick fox eating recovers a little.
                if (a.Sickness != SicknessKind.None)
                    a.SicknessSeverity = MathX.Clamp01(a.SicknessSeverity - 0.05f);
                LineageSystem.MaybeGutTwist(ctx.Sim); // gorging while starving has a price
                ctx.Sim.State.Beliefs.AddOrUpdate("food.bush." + bush.Id,
                    "the bush " + DescribeWhere(ctx, bush) + " has glowberries",
                    "saw", 0.8f, ctx.Now);
                if (ctx.Ev.NextFloat() < 0.35f || bush.Stock == 0)
                    ctx.Sim.Journal(ctx.Now,
                        bush.Stock == 0
                            ? "I stripped the last of the glowberries. The bush will need time."
                            : "I ate glowberries warm from the sun, light on my tongue.",
                        JournalCategory.Survival, 0.3f, 1f, bush.Id);
                if (a.Hunger < 25f) a.DecisionTimer = 0f; // sated: choose anew
            }
        }

        public override string DescribeReason(BrainContext ctx)
        {
            return "My stomach was emptier than my plans — food first, everything else after.";
        }

        private string DescribeWhere(BrainContext ctx, PointOfInterest bush)
        {
            // Nearest learned place name, for a human-readable belief.
            PointOfInterest best = null;
            float bestD = float.MaxValue;
            foreach (var p in ctx.World.Pois)
            {
                if (!p.LearnedName) continue;
                float d = V2.Distance(new V2(bush.X, bush.Z), new V2(p.X, p.Z));
                if (d < bestD) { bestD = d; best = p; }
            }
            return best != null ? "near " + best.Name : "in the glen";
        }
    }

    // ------------------------------------------------------------------ Drink

    public class DrinkAction : AgentAction
    {
        public override string Name { get { return "Drink"; } }
        public override string Topic { get { return "drink"; } }
        public override bool IsSurvival { get { return true; } }

        public override bool CanScore(BrainContext ctx) { return ctx.Agent.Thirst > 20f; }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            V2 wp = ctx.World.NearestWaterPoint(a.X, a.Z);
            float dist = V2.Distance(a.Pos, wp);
            Add(c, "thirst", a.Thirst / 100f, 0.50f);
            Add(c, "relief", 0.8f, 0.15f);
            Add(c, "effort", dist / 200f, -0.20f);
            Add(c, "risk", PredatorDanger(ctx, 40f), -0.15f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            a.CurrentGoal = "Drink";
            a.CurrentActivity = "going down to the water";
            V2 wp = ctx.World.NearestWaterPoint(a.X, a.Z);
            a.TargetPoiId = -1;
            MoveTo(ctx, wp.X, wp.Z, 4f, false);
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            V2 wp = ctx.World.NearestWaterPoint(a.X, a.Z);
            float d = V2.Distance(a.Pos, wp);
            bool atWater = d < 6f || ctx.World.IsWater(a.X + (wp.X - a.X) * 0.1f, a.Z + (wp.Z - a.Z) * 0.1f);
            if (atWater)
            {
                Stop(ctx);
                a.Thirst = 0f;
                a.Mood = MathX.Clamp(a.Mood + 2f, 0f, 100f);
                a.CurrentActivity = "drinking from the river";
                if (ctx.Ev.NextFloat() < 0.3f)
                    ctx.Sim.Journal(ctx.Now, "I knelt and drank from the river. Cold enough to ache.",
                        JournalCategory.Survival, 0.2f);
                a.DecisionTimer = 0f;
            }
            else
            {
                // Keep steering at the moving nearest point.
                a.TargetX = wp.X; a.TargetZ = wp.Z;
                a.HasMoveTarget = true;
            }
        }

        public override string DescribeReason(BrainContext ctx)
        {
            return "My throat was dry — the river was the only thing on my mind.";
        }
    }

    // ------------------------------------------------------------------- Rest

    public class RestAction : AgentAction
    {
        public override string Name { get { return "Rest"; } }
        public override string Topic { get { return "rest"; } }
        public override bool IsSurvival { get { return true; } }

        public PointOfInterest TargetShelter(BrainContext ctx)
        {
            PointOfInterest best = null;
            float bestScore = float.MinValue;
            foreach (var poi in ctx.World.Pois)
            {
                if (poi.Type != PoiType.EmberHollow && poi.Type != PoiType.Den) continue;
                if (!ctx.Agent.KnownPoiIds.Contains(poi.Id) && poi.Type != PoiType.Den) continue;
                float d = V2.Distance(ctx.Agent.Pos, new V2(poi.X, poi.Z));
                float comfort = poi.Type == PoiType.EmberHollow ? 1f : 0.8f;
                float score = comfort - d / 300f;
                if (score > bestScore) { bestScore = score; best = poi; }
            }
            return best;
        }

        public override bool CanScore(BrainContext ctx) { return ctx.Agent.Energy < 75f; }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var shelter = TargetShelter(ctx);
            float dist = shelter != null ? V2.Distance(a.Pos, new V2(shelter.X, shelter.Z)) : 0f;
            Add(c, "fatigue", (100f - a.Energy) / 100f, 0.45f);
            Add(c, "comfort", shelter != null ? (shelter.Type == PoiType.EmberHollow ? 0.9f : 0.75f) : 0.4f, 0.20f);
            Add(c, "effort", dist / 300f, -0.15f);
            Add(c, "risk", PredatorDanger(ctx, 45f), -0.20f);
            // Sickness makes rest urgent: the light needs tending.
            if (a.Sickness != SicknessKind.None)
                Add(c, "sickness", 0.5f + 0.5f * a.SicknessSeverity, 0.30f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            a.CurrentGoal = "Rest";
            var shelter = TargetShelter(ctx);
            if (shelter != null)
            {
                a.TargetPoiId = shelter.Id;
                MoveTo(ctx, shelter.X, shelter.Z, shelter.Radius * 0.6f, false);
                a.CurrentActivity = shelter.Type == PoiType.EmberHollow
                    ? "going to rest in the ember-hollow"
                    : "going home to rest";
            }
            else
            {
                a.TargetPoiId = -1;
                Stop(ctx);
                a.CurrentActivity = "resting where I stand";
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var shelter = ctx.World.GetPoi(a.TargetPoiId);
            bool arrived = shelter == null ||
                V2.Distance(a.Pos, new V2(shelter.X, shelter.Z)) <= shelter.Radius;
            if (!arrived) return;

            Stop(ctx);
            float quality = shelter == null ? 1.0f : shelter.Type == PoiType.EmberHollow ? 2.2f : 1.8f;
            // Sickness taxes rest: the light rekindles slower.
            quality *= 1f - 0.55f * a.SicknessSeverity;
            a.Energy = MathX.Clamp(a.Energy + dt * quality, 0f, a.MaxEnergy);
            if (a.Hunger < 60f && a.Thirst < 60f)
                a.Health = Math.Min(100f, a.Health + dt * 0.15f);
            a.CurrentActivity = shelter != null && shelter.Type == PoiType.EmberHollow
                ? "resting in the ember-hollow" : "resting";
            a.Mood = MathX.Clamp(a.Mood + dt * 0.2f, 0f, 100f);

            // Tales are told at rest, near kits or kindred.
            TellTale(ctx);

            if (a.Energy >= a.MaxEnergy * 0.98f)
            {
                a.DecisionTimer = 0f;
                if (ctx.Ev.NextFloat() < 0.5f)
                    ctx.Sim.Journal(ctx.Now, "I woke from a doze, my light steady, feeling like myself again.",
                        JournalCategory.Survival, 0.2f);
            }
        }

        private void TellTale(BrainContext ctx)
        {
            var a = ctx.Agent;
            if (a.TalesKnown.Count == 0) return;
            if (ctx.Now - a.LastTaleToldAt < 7200f) return; // at most every 2 game-hours

            // An audience: kits at the den, or kindred nearby.
            bool kitsNear = false;
            foreach (var k in ctx.Sim.State.Kits)
            {
                if (!k.IsAlive) continue;
                if (V2.Distance(a.Pos, k.Pos) < 25f) { kitsNear = true; break; }
            }
            EntityState kindredNear = null;
            if (!kitsNear)
            {
                foreach (var e in ctx.Sim.State.Entities)
                {
                    if (e.Kind != EntityKind.Kindred || !e.IsAlive) continue;
                    if (V2.Distance(a.Pos, e.Pos) < 15f) { kindredNear = e; break; }
                }
            }
            if (!kitsNear && kindredNear == null) return;

            var tale = ctx.Sim.State.Lineage.GetTale(
                a.TalesKnown[ctx.Ev.NextInt(a.TalesKnown.Count)]);
            if (tale == null) return;
            a.LastTaleToldAt = ctx.Now;
            string audience = kitsNear ? "the kits" : kindredNear.Name;
            ctx.Sim.Journal(ctx.Now,
                "I told " + audience + " the tale of " + tale.Title + ".",
                JournalCategory.Social, 0.5f);
        }

        public override string DescribeReason(BrainContext ctx)
        {
            return "My light was low and my legs were heavy — rest wasn't a choice, it was a debt.";
        }
    }

    // ------------------------------------------------------------- ExplorePoi

    public class ExplorePoiAction : AgentAction
    {
        public override string Name { get { return "Explore"; } }
        public override string Topic { get { return "explore"; } }

        private static float Mystery(PoiType t)
        {
            switch (t)
            {
                case PoiType.InsectileRuin: return 1.0f;
                case PoiType.RuinSite: return 0.85f;
                case PoiType.Overlook: return 0.6f;
                case PoiType.Cairn: return 0.45f;
                case PoiType.EmberHollow: return 0.35f;
                case PoiType.GlowberryBush: return 0.3f;
                default: return 0.4f;
            }
        }

        public PointOfInterest TargetUndiscovered(BrainContext ctx)
        {
            PointOfInterest best = null;
            float bestScore = float.MinValue;
            foreach (var poi in ctx.World.Pois)
            {
                if (poi.Discovered) continue;
                float d = V2.Distance(ctx.Agent.Pos, new V2(poi.X, poi.Z));
                float score = Mystery(poi.Type) * 2f - d / 200f;
                if (score > bestScore) { bestScore = score; best = poi; }
            }
            return best;
        }

        public override bool CanScore(BrainContext ctx) { return TargetUndiscovered(ctx) != null; }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var poi = TargetUndiscovered(ctx);
            float d = V2.Distance(a.Pos, new V2(poi.X, poi.Z));
            float mystery = Mystery(poi.Type);
            Add(c, "novelty", (0.35f + 0.65f * a.EffectiveCuriosity / 100f) * mystery, 0.40f);
            Add(c, "promise", mystery, 0.15f);
            Add(c, "effort", d / 400f, -0.20f);
            Add(c, "risk", PredatorDanger(ctx, 50f) * 0.6f + a.EffectiveCaution * (d / 400f) * 0.6f, -0.15f);
            Add(c, "curiosity", a.EffectiveCuriosity / 100f * mystery, 0.10f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            var poi = TargetUndiscovered(ctx);
            a.CurrentGoal = "Explore";
            if (poi != null)
            {
                a.TargetPoiId = poi.Id;
                MoveTo(ctx, poi.X, poi.Z, poi.Radius, false);
                a.CurrentActivity = "going to see " + poi.DisplayName;
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var poi = ctx.World.GetPoi(a.TargetPoiId);
            if (poi == null || poi.Discovered) { a.DecisionTimer = 0f; return; }
            float d = V2.Distance(a.Pos, new V2(poi.X, poi.Z));
            if (d <= poi.Radius + 4f)
            {
                Stop(ctx);
                ctx.Sim.DiscoverPoi(poi.Id);
                a.Curiosity = Math.Max(20f, a.Curiosity - 35f);
                a.Mood = MathX.Clamp(a.Mood + 6f, 0f, 100f);
                a.DecisionTimer = 2f;
            }
        }

        public override string DescribeReason(BrainContext ctx)
        {
            var poi = ctx.World.GetPoi(ctx.Agent.TargetPoiId);
            string what = poi != null ? poi.DisplayName : "something new";
            return "I couldn't stop wondering about " + what + " — so I went to look.";
        }
    }

    // -------------------------------------------------------- ObserveWildlife

    public class ObserveWildlifeAction : AgentAction
    {
        public override string Name { get { return "Observe"; } }
        public override string Topic { get { return "watch"; } }

        public EntityState TargetAnimal(BrainContext ctx)
        {
            EntityState best = null;
            float bestD = float.MaxValue;
            foreach (var e in ctx.State.Entities)
            {
                if (e.Health <= 0) continue;
                if (e.Kind != EntityKind.Deer && e.Kind != EntityKind.Rabbit) continue;
                float d = V2.Distance(ctx.Agent.Pos, new V2(e.X, e.Z));
                if (d < 45f && d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        public override bool CanScore(BrainContext ctx)
        {
            if (ctx.Agent.Energy < 25f) return false;
            float d;
            if (ctx.NearestPredator(30f, out d) != null) return false;
            return TargetAnimal(ctx) != null;
        }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var animal = TargetAnimal(ctx);
            float d = V2.Distance(a.Pos, new V2(animal.X, animal.Z));
            bool fresh = a.LastObservedEntityId != animal.Id || ctx.Now - a.LastObservedTime > 3600f;
            Add(c, "wonder", 0.25f + 0.65f * a.EffectiveCuriosity / 100f, 0.45f);
            Add(c, "novelty", fresh ? 0.8f : 0.25f, 0.20f);
            Add(c, "ease", 1f - d / 45f, 0.15f);
            Add(c, "patience", a.Traits.Patience, 0.10f);
            Add(c, "compassion", a.Traits.Compassion * 0.6f, 0.10f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            var animal = TargetAnimal(ctx);
            a.CurrentGoal = "Observe";
            a.CurrentActivity = animal != null
                ? "watching the " + animal.Kind.ToString().ToLowerInvariant()
                : "watching the wild";
            a.TargetEntityId = animal != null ? animal.Id : -1;
            Stop(ctx);
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var animal = ctx.FindEntity(a.TargetEntityId);
            if (animal == null || animal.Health <= 0)
            {
                a.DecisionTimer = 0f;
                return;
            }
            float d = V2.Distance(a.Pos, new V2(animal.X, animal.Z));
            if (d > 50f) { a.DecisionTimer = 0f; return; } // it wandered off
            // Stand still and watch.
            a.Facing = MathX.YawFromDir(new V2(animal.X - a.X, animal.Z - a.Z));
            a.Speed = 0f;
            a.CurrentActivity = "watching the " + animal.Kind.ToString().ToLowerInvariant() + " graze";

            if (a.ActionTimer > 14f)
            {
                a.LastObservedEntityId = animal.Id;
                a.LastObservedTime = ctx.Now;
                a.Mood = MathX.Clamp(a.Mood + 4f, 0f, 100f);
                a.Curiosity = Math.Max(15f, a.Curiosity - 15f);
                string kind = animal.Kind == EntityKind.Deer ? "deer" : "hare";
                ctx.Sim.Journal(ctx.Now,
                    "I stood a long while watching a " + kind + ". It never knew how close we were.",
                    JournalCategory.Travel, 0.3f);
                a.DecisionTimer = 0f;
            }
        }

        public override string DescribeReason(BrainContext ctx)
        {
            return "There was no hurry in me, and the wild was showing itself — I stopped to watch.";
        }
    }

    // ------------------------------------------------------------ GreetKindred

    public class GreetKindredAction : AgentAction
    {
        public override string Name { get { return "Greet"; } }
        public override string Topic { get { return "social"; } }

        public EntityState TargetKindred(BrainContext ctx)
        {
            EntityState best = null;
            float bestD = float.MaxValue;
            foreach (var e in ctx.State.Entities)
            {
                if (e.Kind != EntityKind.Kindred || e.Health <= 0) continue;
                float d = V2.Distance(ctx.Agent.Pos, new V2(e.X, e.Z));
                if (d < 32f && d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        public override bool CanScore(BrainContext ctx) { return TargetKindred(ctx) != null; }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var v = TargetKindred(ctx);
            float d = V2.Distance(a.Pos, new V2(v.X, v.Z));
            var rec = ctx.Sim.State.Social.GetPerson(v.Id);
            float familiarity = rec != null ? rec.Trust : 0.3f;
            Add(c, "sociability", a.Traits.Sociability * (0.5f + 0.5f * (1f - familiarity)), 0.40f);
            Add(c, "closeness", 1f - d / 32f, 0.20f);
            Add(c, "warmth", a.Mood / 100f * 0.7f + 0.3f, 0.15f);
            Add(c, "promise", 0.5f, 0.10f); // talk carries news
            Add(c, "effort", d / 32f, -0.15f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            var v = TargetKindred(ctx);
            a.CurrentGoal = "Greet";
            if (v != null)
            {
                a.TargetEntityId = v.Id;
                MoveTo(ctx, v.X, v.Z, 2.5f, false);
                a.CurrentActivity = "going to greet " + v.Name;
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var v = ctx.FindEntity(a.TargetEntityId);
            if (v == null || v.Kind != EntityKind.Kindred) { a.DecisionTimer = 0f; return; }
            float d = V2.Distance(a.Pos, new V2(v.X, v.Z));
            if (d > 3.5f) return; // still walking over
            Stop(ctx);
            v.Behavior = "Greeted";
            v.StateTimer = 8f;
            v.Facing = MathX.YawFromDir(new V2(a.X - v.X, a.Z - v.Z));
            a.Facing = MathX.YawFromDir(new V2(v.X - a.X, v.Z - a.Z));

            ctx.Sim.State.Social.RecordGreeting(v.Id, ctx.Now);
            var rec = ctx.Sim.State.Social.GetPerson(v.Id);
            a.Mood = MathX.Clamp(a.Mood + 3f + 3f * a.Traits.Sociability, 0f, 100f);

            // Repeated greetings with a trusted adult kindred deepen into a bond.
            bool adult = a.Stage == LifeStage.Adult || a.Stage == LifeStage.Elder;
            if (adult && rec.Trust > 0.55f)
                DeepenBond(ctx, a, v);

            // Kindred share news: with trust-scaled probability they name an unknown place.
            string rumorText = "";
            if (ctx.Ai.NextFloat() < 0.25f + rec.Trust * 0.45f)
            {
                var unknown = new List<PointOfInterest>();
                foreach (var p in ctx.World.Pois)
                    if (!p.LearnedName && p.Type != PoiType.Den && p.Type != PoiType.GlowberryBush)
                        unknown.Add(p);
                if (unknown.Count > 0)
                {
                    var poi = unknown[ctx.Ai.NextInt(unknown.Count)];
                    poi.LearnedName = true;
                    a.KnownPoiIds.Add(poi.Id);
                    ctx.Sim.State.Beliefs.AddOrUpdate("poi." + poi.Id + ".name",
                        poi.Name + " is " + poi.DisplayName, "told by " + v.Name, 0.85f, ctx.Now);
                    rumorText = " " + v.Name + " told me of " + poi.Name + ".";
                }
            }

            ctx.Sim.Journal(ctx.Now,
                "I greeted " + v.Name + " by the track." + rumorText,
                JournalCategory.Social, 0.4f, 0.95f, null, v.Id,
                "told by " + v.Name);
            a.Traits.Nudge("Sociability", 0.008f);
            a.DecisionTimer = 3f;
        }

        private void DeepenBond(BrainContext ctx, AgentState a, EntityState v)
        {
            if (a.Bond == null || a.Bond.PartnerId != v.Id)
                a.Bond = new Bond { PartnerId = v.Id, Strength = 0.1f };
            float before = a.Bond.Strength;
            a.Bond.Strength = MathX.Clamp01(a.Bond.Strength + 0.08f);
            if (before <= 0.7f && a.Bond.Strength > 0.7f)
            {
                a.Bond.SinceStrongAt = ctx.Now;
                ctx.Sim.Journal(ctx.Now,
                    "Something has changed between me and " + v.Name +
                    ". We walk the same trails now, and the den feels like ours.",
                    JournalCategory.Social, 0.85f, 1f, null, v.Id);
                LineageSystem.MaybeDistillTale(ctx.Sim, "The Bonding", "Sociability", 0.05f,
                    "bonded with " + v.Name);
            }
        }

        public override string DescribeReason(BrainContext ctx)
        {
            var v = ctx.FindEntity(ctx.Agent.TargetEntityId);
            string who = v != null ? v.Name : "one of my kind";
            return "It's good to be known by someone — I went to say hello to " + who + ".";
        }
    }

    // -------------------------------------------------------------- SeekBond

    /// <summary>
    /// Seeking out a bond-mate: adults nurture the pair-bond by spending time
    /// together. Strong bonds ripen into litters (see LineageSystem.TickBonding).
    /// </summary>
    public class SeekBondAction : AgentAction
    {
        public override string Name { get { return "SeekBond"; } }
        public override string Topic { get { return "social"; } }

        private EntityState BondPartner(BrainContext ctx)
        {
            var bond = ctx.Agent.Bond;
            if (bond == null || bond.PartnerId < 0) return null;
            var e = ctx.FindEntity(bond.PartnerId);
            return e != null && e.IsAlive && e.Kind == EntityKind.Kindred ? e : null;
        }

        public override bool CanScore(BrainContext ctx)
        {
            var a = ctx.Agent;
            if (a.Stage != LifeStage.Adult && a.Stage != LifeStage.Elder) return false;
            if (a.SicknessSeverity > 0.6f) return false;
            return BondPartner(ctx) != null;
        }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var p = BondPartner(ctx);
            float d = V2.Distance(a.Pos, new V2(p.X, p.Z));
            Add(c, "bond", a.Bond.Strength, 0.35f);
            Add(c, "sociability", a.Traits.Sociability, 0.25f);
            Add(c, "closeness", 1f - Math.Min(d, 120f) / 120f, 0.15f);
            Add(c, "longing", a.Bond.LitterBorn ? 0.3f : 0.7f, 0.15f);
            Add(c, "effort", d / 120f, -0.15f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            var p = BondPartner(ctx);
            a.CurrentGoal = "SeekBond";
            if (p != null)
            {
                a.TargetEntityId = p.Id;
                MoveTo(ctx, p.X, p.Z, 4f, false);
                a.CurrentActivity = "going to find " + p.Name;
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var p = BondPartner(ctx);
            if (p == null) { a.DecisionTimer = 0f; return; }
            float d = V2.Distance(a.Pos, new V2(p.X, p.Z));
            if (d > 5f) return; // still walking over
            Stop(ctx);
            a.Facing = MathX.YawFromDir(new V2(p.X - a.X, p.Z - a.Z));
            a.CurrentActivity = "sitting with " + p.Name;
            // Nurture: time together deepens the bond a little.
            float before = a.Bond.Strength;
            a.Bond.Strength = MathX.Clamp01(a.Bond.Strength + dt * 0.008f);
            if (before <= 0.7f && a.Bond.Strength > 0.7f)
            {
                a.Bond.SinceStrongAt = ctx.Now;
                ctx.Sim.Journal(ctx.Now,
                    "Something has changed between me and " + p.Name +
                    ". We walk the same trails now, and the den feels like ours.",
                    JournalCategory.Social, 0.85f, 1f, null, p.Id);
            }
            a.Mood = MathX.Clamp(a.Mood + dt * 0.3f, 0f, 100f);
            if (a.ActionTimer > 40f) a.DecisionTimer = 0f;
        }

        public override string DescribeReason(BrainContext ctx)
        {
            var p = BondPartner(ctx);
            string who = p != null ? p.Name : "my bond-mate";
            return "My heart was set on company — I went to be near " + who + ".";
        }
    }


    // --------------------------------------------------------------- LootRuin

    public class LootRuinAction : AgentAction
    {
        public override string Name { get { return "Loot"; } }
        public override string Topic { get { return "ruin"; } }

        public PointOfInterest TargetRuin(BrainContext ctx)
        {
            PointOfInterest best = null;
            float bestScore = float.MinValue;
            foreach (var poi in ctx.World.Pois)
            {
                if (poi.Looted) continue;
                if (poi.Type != PoiType.RuinSite && poi.Type != PoiType.InsectileRuin) continue;
                if (!ctx.Agent.KnownPoiIds.Contains(poi.Id) && !poi.Discovered) continue;
                float d = V2.Distance(ctx.Agent.Pos, new V2(poi.X, poi.Z));
                if (d > 450f) continue;
                float score = (poi.Type == PoiType.InsectileRuin ? 1.2f : 0.8f) - d / 300f;
                if (score > bestScore) { bestScore = score; best = poi; }
            }
            return best;
        }

        public override bool CanScore(BrainContext ctx) { return TargetRuin(ctx) != null; }

        public override float Score(BrainContext ctx, List<ScoreComponent> c)
        {
            var a = ctx.Agent;
            var poi = TargetRuin(ctx);
            float d = V2.Distance(a.Pos, new V2(poi.X, poi.Z));
            Add(c, "curiosity", a.EffectiveCuriosity / 100f * 0.8f + 0.2f, 0.35f);
            Add(c, "promise", poi.Type == PoiType.InsectileRuin ? 0.85f : 0.55f, 0.25f);
            Add(c, "effort", d / 400f, -0.20f);
            Add(c, "risk", PredatorDanger(ctx, 50f) * 0.5f + a.EffectiveCaution * 0.4f, -0.20f);
            return Total(c);
        }

        public override void Begin(BrainContext ctx)
        {
            var a = ctx.Agent;
            var poi = TargetRuin(ctx);
            a.CurrentGoal = "Loot";
            if (poi != null)
            {
                a.TargetPoiId = poi.Id;
                MoveTo(ctx, poi.X, poi.Z, poi.Radius * 0.7f, false);
                a.CurrentActivity = "searching " + poi.DisplayName;
            }
        }

        public override void Update(BrainContext ctx, float dt)
        {
            var a = ctx.Agent;
            var poi = ctx.World.GetPoi(a.TargetPoiId);
            if (poi == null || poi.Looted) { a.DecisionTimer = 0f; return; }
            float d = V2.Distance(a.Pos, new V2(poi.X, poi.Z));
            if (d > poi.Radius) return;
            Stop(ctx);
            poi.Looted = true;

            // The hollow hive may be denning something.
            if (poi.Type == PoiType.InsectileRuin && ctx.Ev.NextFloat() < 0.30f)
            {
                var predator = new EntityState
                {
                    Id = ctx.Sim.NextEntityId(),
                    Kind = EntityKind.Predator,
                    Name = "gloom-maw",
                    X = poi.X + ctx.Ev.NextFloat(-12f, 12f),
                    Z = poi.Z + ctx.Ev.NextFloat(-12f, 12f),
                    Health = 70f,
                    Behavior = "Hunt",
                    HomeX = poi.X, HomeZ = poi.Z,
                    Hunger = 80f,
                    TargetKind = 1 // the agent
                };
                ctx.Sim.State.Entities.Add(predator);
                ctx.Sim.Journal(ctx.Now, "Something moved in the dark of " + poi.Name + " — a gloom-maw's eyes caught the light.",
                    JournalCategory.Combat, 0.85f, 1f, poi.Id);
                a.Traits.Nudge("Caution", 0.02f);
                a.DecisionTimer = 0f; // reassess immediately (likely Flee)
                return;
            }

            float roll = ctx.Ev.NextFloat();
            string found;
            if (roll < 0.35f)
            {
                int n = ctx.Ev.NextInt(1, 3);
                ctx.Sim.State.Inventory.AddBread(n);
                found = n + " seedcakes wrapped in leaves";
            }
            else if (roll < 0.55f)
            {
                ctx.Sim.State.Inventory.AddPotions(1);
                found = "a gourd of bitter root-tea";
            }
            else
            {
                string[] keepsakes = { "a carved antler", "a chitin shard, smooth as glass", "a smooth river stone", "a black feather", "a spiral shell" };
                string k = keepsakes[ctx.Ev.NextInt(keepsakes.Length)];
                ctx.Sim.State.Inventory.AddKeepsake(k);
                found = k;
            }
            ctx.Sim.Journal(ctx.Now,
                "I searched " + poi.DisplayName + " and found " + found + ".",
                JournalCategory.Discovery, 0.6f, 1f, poi.Id);
            ctx.Sim.State.Beliefs.AddOrUpdate("poi." + poi.Id + ".searched",
                poi.DisplayName + " has been searched", "saw", 1f, ctx.Now);
            a.Mood = MathX.Clamp(a.Mood + 5f, 0f, 100f);
            a.DecisionTimer = 2f;
        }

        public override string DescribeReason(BrainContext ctx)
        {
            var poi = ctx.World.GetPoi(ctx.Agent.TargetPoiId);
            string what = poi != null ? poi.DisplayName : "the old stones";
            return "Old places keep old things — I wanted to see what " + what + " was hiding.";
        }
    }
}
