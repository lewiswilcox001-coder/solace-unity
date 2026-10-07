// Solace.Core — the authoritative game state: everything the simulation owns.
// Serializable verbatim via SaveSystem. Presentation, language, and platform
// layers may read this; only the simulation (and validated tools) may write it.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public enum Weather
    {
        Clear,
        Cloudy,
        Rain,
        Storm
    }

    /// <summary>Player-chosen continuity mode for time away.</summary>
    public enum OfflineMode
    {
        Stillness,   // pause safely: nothing changes
        QuietLife,   // routine, low risk: mild needs drift, at most one minor episode
        LivingWorld  // full bounded simulation: chunks, budgets, journaled encounters
    }

    /// <summary>Positions of the domain-partitioned RNG streams.</summary>
    public class RngStates
    {
        public uint Ai;
        public uint Event;
        public uint Lineage;  // aging, sickness, kits, bonding, succession
        public uint Colossus; // colossi drift
        public uint Dream;    // dream rolls (content itself derives from the seed)

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("ai", Ai);
            o.Add("event", Event);
            o.Add("lineage", Lineage);
            o.Add("colossus", Colossus);
            o.Add("dream", Dream);
            return o;
        }

        public static RngStates FromJson(JsonObject o)
        {
            return new RngStates
            {
                Ai = JsonHelpers.GetUInt(o, "ai", 0),
                Event = JsonHelpers.GetUInt(o, "event", 0),
                Lineage = JsonHelpers.GetUInt(o, "lineage", 0),
                Colossus = JsonHelpers.GetUInt(o, "colossus", 0),
                Dream = JsonHelpers.GetUInt(o, "dream", 0)
            };
        }
    }

    /// <summary>Per-tool budgets: cooldowns and use counts, keyed by tool name.</summary>
    public class ToolBudgetState
    {
        public Dictionary<string, float> CooldownUntil = new Dictionary<string, float>();
        public Dictionary<string, int> UseCounts = new Dictionary<string, int>();

        public bool IsCoolingDown(string tool, float now)
        {
            float until;
            return CooldownUntil.TryGetValue(tool, out until) && now < until;
        }

        public void SetCooldown(string tool, float now, float cooldownSeconds)
        {
            CooldownUntil[tool] = now + cooldownSeconds;
        }

        public int GetUseCount(string tool)
        {
            int n;
            return UseCounts.TryGetValue(tool, out n) ? n : 0;
        }

        public void CountUse(string tool)
        {
            int n;
            UseCounts.TryGetValue(tool, out n);
            UseCounts[tool] = n + 1;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var ck = new List<string>(CooldownUntil.Keys);
            ck.Sort(StringComparer.Ordinal);
            var ca = new JsonObject();
            foreach (var k in ck) ca.Add(k, CooldownUntil[k]);
            o.Add("cooldowns", ca);
            var uk = new List<string>(UseCounts.Keys);
            uk.Sort(StringComparer.Ordinal);
            var ua = new JsonObject();
            foreach (var k in uk) ua.Add(k, UseCounts[k]);
            o.Add("uses", ua);
            return o;
        }

        public static ToolBudgetState FromJson(JsonObject o)
        {
            var t = new ToolBudgetState();
            JsonValue cv;
            if (o.TryGet("cooldowns", out cv))
                foreach (var kv in cv.AsObject().Members)
                    t.CooldownUntil[kv.Key] = ((JsonNumber)kv.Value).AsFloat();
            JsonValue uv;
            if (o.TryGet("uses", out uv))
                foreach (var kv in uv.AsObject().Members)
                    t.UseCounts[kv.Key] = ((JsonNumber)kv.Value).AsInt();
            return t;
        }
    }

    /// <summary>
    /// The whole life. SchemaVersion gates migration; saves are forward-only.
    /// </summary>
    public class GameState
    {
        public const int SchemaVersion = 2;

        public int Seed;
        // Game seconds since the life began. DOUBLE, not float: at 20+ days
        // (1.7M seconds) float epsilon exceeds the 1/30s fixed step and time
        // would freeze. A year is 80 days; the sim must track them all.
        public double ElapsedSeconds;
        public float StartHour = 9f;   // time of day at t=0
        public Weather Weather = Weather.Clear;
        public float WeatherChangedAt;

        public WorldData World;
        public AgentState Agent;
        public List<EntityState> Entities = new List<EntityState>();
        /// <summary>The living kits of the current generation's den.</summary>
        public List<KitState> Kits = new List<KitState>();
        /// <summary>The lineage: generations, chapters, tales, kin.</summary>
        public LineageState Lineage = new LineageState();
        public Journal Journal = new Journal();
        public BeliefStore Beliefs = new BeliefStore();
        public SocialMemory Social = new SocialMemory();
        public Inventory Inventory = new Inventory();
        /// <summary>
        /// Transient: set at boot when opening a daily vale, never serialized.
        /// Null for slot/custom worlds. Tells the HUD and milestone detection
        /// that this is the shared daily world (and the current streak).
        /// </summary>
        public DailyVisitInfo DailyInfo;
        public CompanionState Companion = new CompanionState();
        public RngStates Rng = new RngStates();
        public ToolBudgetState ToolBudgets = new ToolBudgetState();
        /// <summary>Last arbitration trace, kept for legibility ("why did you…").</summary>
        public DecisionTrace LastDecision = new DecisionTrace();
        /// <summary>Every dream ever dreamed — part of the multi-generational chronicle.</summary>
        public DreamJournal Dreams = new DreamJournal();
        /// <summary>Milestones/achievements: celebration of moments. Detection only.</summary>
        public MilestoneState Milestones = new MilestoneState();
        /// <summary>Easter eggs and secrets: gifts for the curious. Detection only.</summary>
        public EasterEggState Eggs = new EasterEggState();
        /// <summary>Lifetime statistics: the measure of a life. Tracking only.</summary>
        public StatState Stats = new StatState();
        /// <summary>
        /// Leftover fractional game-time in the fixed-step accumulator.
        /// Serialized so save/load doesn't shift the step cadence.
        /// </summary>
        public float StepRemainder;

        /// <summary>Time of day in [0, 24).</summary>
        public float TimeOfDay
        {
            get
            {
                double t = (StartHour + ElapsedSeconds / 3600.0) % 24.0;
                if (t < 0) t += 24.0;
                return (float)t;
            }
        }

        public bool IsNight
        {
            // Nights breathe with the year: long and hungry in winter, brief in summer.
            get { return SeasonSystem.IsNightAt(SeasonSystem.Current(this), TimeOfDay); }
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("schemaVersion", SchemaVersion);
            o.Add("seed", Seed);
            o.Add("elapsedSeconds", ElapsedSeconds);
            o.Add("startHour", StartHour);
            o.Add("weather", Weather.ToString());
            o.Add("weatherChangedAt", WeatherChangedAt);
            o.Add("world", World.ToJson());
            o.Add("agent", Agent.ToJson());
            var ea = new JsonArray();
            for (int i = 0; i < Entities.Count; i++) ea.Add(Entities[i].ToJson());
            o.Add("entities", ea);
            var ka = new JsonArray();
            for (int i = 0; i < Kits.Count; i++) ka.Add(Kits[i].ToJson());
            o.Add("kits", ka);
            o.Add("lineage", Lineage.ToJson());
            o.Add("journal", Journal.ToJson());
            o.Add("beliefs", Beliefs.ToJson());
            o.Add("social", Social.ToJson());
            o.Add("inventory", Inventory.ToJson());
            o.Add("companion", Companion.ToJson());
            o.Add("dreams", Dreams.ToJson());
            o.Add("milestones", Milestones.ToJson());
            o.Add("easterEggs", Eggs.ToJson());
            o.Add("stats", Stats.ToJson());
            o.Add("rng", Rng.ToJson());
            o.Add("toolBudgets", ToolBudgets.ToJson());
            o.Add("lastDecision", LastDecision.ToJson());
            o.Add("stepRemainder", StepRemainder);
            return o;
        }

        public static GameState FromJson(JsonObject o)
        {
            int schema = JsonHelpers.GetInt(o, "schemaVersion", 1);
            if (schema < 1 || schema > SchemaVersion)
                throw new JsonParseException("Unsupported save schema version: " + schema);
            var s = new GameState();
            s.Seed = JsonHelpers.GetInt(o, "seed", 0);
            s.ElapsedSeconds = JsonHelpers.GetDouble(o, "elapsedSeconds", 0.0);
            s.StartHour = JsonHelpers.GetFloat(o, "startHour", 9f);
            s.Weather = (Weather)Enum.Parse(typeof(Weather), JsonHelpers.GetString(o, "weather", "Clear"));
            s.WeatherChangedAt = JsonHelpers.GetFloat(o, "weatherChangedAt", 0f);
            s.World = WorldData.FromJson(o["world"].AsObject());
            s.Agent = AgentState.FromJson(o["agent"].AsObject());
            var ea = o["entities"].AsArray();
            s.Entities = new List<EntityState>(ea.Count);
            for (int i = 0; i < ea.Count; i++) s.Entities.Add(EntityState.FromJson(ea[i].AsObject()));
            JsonValue kv;
            if (o.TryGet("kits", out kv) && !kv.IsNull)
            {
                var kar = kv.AsArray();
                s.Kits = new List<KitState>(kar.Count);
                for (int i = 0; i < kar.Count; i++) s.Kits.Add(KitState.FromJson(kar[i].AsObject()));
            }
            JsonValue lv;
            s.Lineage = o.TryGet("lineage", out lv) && !lv.IsNull
                ? LineageState.FromJson(lv.AsObject()) : new LineageState();
            s.Journal = Journal.FromJson(o["journal"].AsObject());
            s.Beliefs = BeliefStore.FromJson(o["beliefs"].AsObject());
            s.Social = SocialMemory.FromJson(o["social"].AsObject());
            s.Inventory = Inventory.FromJson(o["inventory"].AsObject());
            s.Companion = CompanionState.FromJson(o["companion"].AsObject());
            JsonValue drjv;
            s.Dreams = o.TryGet("dreams", out drjv) && !drjv.IsNull
                ? DreamJournal.FromJson(drjv.AsObject()) : new DreamJournal();
            JsonValue mmv;
            s.Milestones = o.TryGet("milestones", out mmv) && !mmv.IsNull
                ? MilestoneState.FromJson(mmv.AsObject()) : new MilestoneState();
            JsonValue egv;
            s.Eggs = o.TryGet("easterEggs", out egv) && !egv.IsNull
                ? EasterEggState.FromJson(egv.AsObject()) : new EasterEggState();
            JsonValue stv;
            s.Stats = o.TryGet("stats", out stv) && !stv.IsNull
                ? StatState.FromJson(stv.AsObject()) : new StatState();
            s.Rng = RngStates.FromJson(o["rng"].AsObject());
            s.ToolBudgets = ToolBudgetState.FromJson(o["toolBudgets"].AsObject());
            JsonValue ldv;
            s.LastDecision = o.TryGet("lastDecision", out ldv) && !ldv.IsNull
                ? DecisionTrace.FromJson(ldv.AsObject()) : new DecisionTrace();
            s.StepRemainder = JsonHelpers.GetFloat(o, "stepRemainder", 0f);
            return s;
        }
    }
}
