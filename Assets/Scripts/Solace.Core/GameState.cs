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
        // Note: the world-gen stream is consumed entirely during generation,
        // so it needs no persisted position.

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("ai", Ai);
            o.Add("event", Event);
            return o;
        }

        public static RngStates FromJson(JsonObject o)
        {
            return new RngStates
            {
                Ai = JsonHelpers.GetUInt(o, "ai", 0),
                Event = JsonHelpers.GetUInt(o, "event", 0)
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
        public const int SchemaVersion = 1;

        public int Seed;
        public float ElapsedSeconds;   // game seconds since the life began
        public float StartHour = 9f;   // time of day at t=0
        public Weather Weather = Weather.Clear;
        public float WeatherChangedAt;

        public WorldData World;
        public AgentState Agent;
        public List<EntityState> Entities = new List<EntityState>();
        public Journal Journal = new Journal();
        public BeliefStore Beliefs = new BeliefStore();
        public SocialMemory Social = new SocialMemory();
        public Inventory Inventory = new Inventory();
        public CompanionState Companion = new CompanionState();
        public RngStates Rng = new RngStates();
        public ToolBudgetState ToolBudgets = new ToolBudgetState();
        /// <summary>Last arbitration trace, kept for legibility ("why did you…").</summary>
        public DecisionTrace LastDecision = new DecisionTrace();
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
                float t = (StartHour + ElapsedSeconds / 3600f) % 24f;
                return t < 0 ? t + 24f : t;
            }
        }

        public bool IsNight
        {
            get { float h = TimeOfDay; return h < 5.5f || h > 21.5f; }
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
            o.Add("journal", Journal.ToJson());
            o.Add("beliefs", Beliefs.ToJson());
            o.Add("social", Social.ToJson());
            o.Add("inventory", Inventory.ToJson());
            o.Add("companion", Companion.ToJson());
            o.Add("rng", Rng.ToJson());
            o.Add("toolBudgets", ToolBudgets.ToJson());
            o.Add("lastDecision", LastDecision.ToJson());
            o.Add("stepRemainder", StepRemainder);
            return o;
        }

        public static GameState FromJson(JsonObject o)
        {
            int schema = JsonHelpers.GetInt(o, "schemaVersion", 1);
            if (schema != SchemaVersion)
                throw new JsonParseException("Unsupported save schema version: " + schema);
            var s = new GameState();
            s.Seed = JsonHelpers.GetInt(o, "seed", 0);
            s.ElapsedSeconds = JsonHelpers.GetFloat(o, "elapsedSeconds", 0f);
            s.StartHour = JsonHelpers.GetFloat(o, "startHour", 9f);
            s.Weather = (Weather)Enum.Parse(typeof(Weather), JsonHelpers.GetString(o, "weather", "Clear"));
            s.WeatherChangedAt = JsonHelpers.GetFloat(o, "weatherChangedAt", 0f);
            s.World = WorldData.FromJson(o["world"].AsObject());
            s.Agent = AgentState.FromJson(o["agent"].AsObject());
            var ea = o["entities"].AsArray();
            s.Entities = new List<EntityState>(ea.Count);
            for (int i = 0; i < ea.Count; i++) s.Entities.Add(EntityState.FromJson(ea[i].AsObject()));
            s.Journal = Journal.FromJson(o["journal"].AsObject());
            s.Beliefs = BeliefStore.FromJson(o["beliefs"].AsObject());
            s.Social = SocialMemory.FromJson(o["social"].AsObject());
            s.Inventory = Inventory.FromJson(o["inventory"].AsObject());
            s.Companion = CompanionState.FromJson(o["companion"].AsObject());
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
