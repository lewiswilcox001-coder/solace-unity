// Solace.Core — safe self-authorship: validated, budgeted, journaled tools.
// A tool NEVER executes arbitrary code. Args are plain key=value pairs.
// Every mutation is validated, budgeted, and journaled with provenance so it
// can be inspected and reasoned about afterwards.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public interface ICompanionTool
    {
        string Name { get; }
        string Description { get; }
        /// <summary>Returns null when valid, else an error message.</summary>
        string Validate(GameState s, string args, out string error);
        /// <summary>Executes after successful validation. Returns a result line.</summary>
        string Execute(GameState s, string args);
    }

    public abstract class CompanionToolBase : ICompanionTool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string Validate(GameState s, string args, out string error);
        public abstract string Execute(GameState s, string args);

        /// <summary>Parses "key=value;key=value" args. No code, no nesting — data only.</summary>
        protected static Dictionary<string, string> ParseArgs(string args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(args)) return d;
            var parts = args.Split(';');
            foreach (var part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq).Trim();
                string v = part.Substring(eq + 1).Trim();
                if (k.Length > 0) d[k] = v;
            }
            return d;
        }

        protected static string Get(Dictionary<string, string> d, string key, string def)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : def;
        }

        /// <summary>Deterministic RNG for tool use: consumes the event stream, writes it back.</summary>
        protected static SeededRandom ToolRng(GameState s, out Action commit)
        {
            var r = new SeededRandom(s.Rng.Event);
            commit = () => { s.Rng.Event = r.State; };
            return r;
        }

        protected static int NextEntityId(GameState s)
        {
            int max = 0;
            foreach (var e in s.Entities)
                if (e.Id > max) max = e.Id;
            return max + 1;
        }
    }

    public class ToolRegistry
    {
        private readonly Dictionary<string, ICompanionTool> _tools =
            new Dictionary<string, ICompanionTool>(StringComparer.OrdinalIgnoreCase);

        public void Register(ICompanionTool tool)
        {
            if (tool == null || string.IsNullOrEmpty(tool.Name))
                throw new ArgumentException("Tool must have a name.");
            _tools[tool.Name] = tool;
        }

        public ICompanionTool Get(string name)
        {
            ICompanionTool t;
            return _tools.TryGetValue(name, out t) ? t : null;
        }

        public IEnumerable<ICompanionTool> Tools { get { return _tools.Values; } }

        /// <summary>
        /// Validates, then executes. Returns "OK: ..." or "ERROR: ...".
        /// Validation failure never mutates state.
        /// </summary>
        public string Invoke(GameState s, string name, string args)
        {
            var tool = Get(name);
            if (tool == null) return "ERROR: unknown tool '" + name + "'.";
            if (s == null) return "ERROR: no game state.";
            string error;
            string validation = tool.Validate(s, args, out error);
            if (validation != null)
                return "ERROR: " + error;
            string result;
            try
            {
                result = tool.Execute(s, args);
            }
            catch (Exception ex)
            {
                return "ERROR: tool '" + name + "' failed: " + ex.Message;
            }
            s.ToolBudgets.CountUse(name);
            return "OK: " + result;
        }

        public static ToolRegistry CreateDefault()
        {
            var r = new ToolRegistry();
            r.Register(new SpawnEncounterTool());
            r.Register(new SetWeatherTool());
            r.Register(new AddJournalEntryTool());
            r.Register(new RevealPoiTool());
            return r;
        }
    }

    // -- spawn_encounter ---------------------------------------------------------

    public class SpawnEncounterTool : CompanionToolBase
    {
        public override string Name { get { return "spawn_encounter"; } }
        public override string Description
        {
            get { return "Brings a nearby animal into the scene. Args: kind=deer|rabbit|predator (a gloom-maw); distance=10..60 (meters)."; }
        }

        public override string Validate(GameState s, string args, out string error)
        {
            error = null;
            var d = ParseArgs(args);
            string kind = Get(d, "kind", "").ToLowerInvariant();
            if (kind != "deer" && kind != "rabbit" && kind != "predator" && kind != "wolf")
                return "kind must be deer, rabbit, or predator.";
            float dist;
            if (!float.TryParse(Get(d, "distance", ""), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out dist))
                return "distance must be a number.";
            if (dist < 10f || dist > 60f)
                return "distance must be between 10 and 60 meters.";
            if (s.ToolBudgets.IsCoolingDown(Name, s.ElapsedSeconds))
                return "spawn_encounter is resting (cooldown).";
            if (s.ToolBudgets.GetUseCount(Name) >= 8)
                return "spawn_encounter has been used enough for one life.";
            return null;
        }

        public override string Execute(GameState s, string args)
        {
            var d = ParseArgs(args);
            string kind = Get(d, "kind", "deer").ToLowerInvariant();
            float dist = float.Parse(Get(d, "distance", "20"),
                System.Globalization.CultureInfo.InvariantCulture);

            Action commit;
            var rng = ToolRng(s, out commit);
            var a = s.Agent;
            EntityState e = null;
            for (int k = 0; k < 8; k++)
            {
                float ang = rng.NextFloat(0f, MathF.PI * 2f);
                float x = a.X + (float)Math.Cos(ang) * dist;
                float z = a.Z + (float)Math.Sin(ang) * dist;
                if (s.World.IsWater(x, z)) continue;
                e = new EntityState
                {
                    Id = NextEntityId(s),
                    Kind = (kind == "predator" || kind == "wolf") ? EntityKind.Predator : kind == "rabbit" ? EntityKind.Rabbit : EntityKind.Deer,
                    Name = kind,
                    X = x, Z = z,
                    Health = 100f,
                    Behavior = (kind == "predator" || kind == "wolf") ? "Roam" : "Graze",
                    HomeX = x, HomeZ = z,
                    Hunger = (kind == "predator" || kind == "wolf") ? 40f : 0f
                };
                break;
            }
            commit();
            if (e == null) return "no dry ground in range — nothing appeared.";
            s.Entities.Add(e);
            s.ToolBudgets.SetCooldown(Name, s.ElapsedSeconds, 1800f);
            string seen = (kind == "predator" || kind == "wolf") ? "A gloom-maw" : "A " + kind;
            s.Journal.Add(s.ElapsedSeconds,
                seen + " stepped out of the " + BiomeWord(s.World.GetBiome(e.X, e.Z)) + ", close enough to see clearly.",
                JournalCategory.Discovery, 0.5f, 1f, null, null, "tool:spawn_encounter");
            return "a " + kind + " appeared " + dist.ToString("F0") + "m away.";
        }

        private static string BiomeWord(Biome b)
        {
            switch (b)
            {
                case Biome.Foxpine: return "pines";
                case Biome.Mistmoor: return "mist";
                case Biome.FellCrag: return "crags";
                default: return "wild";
            }
        }
    }

    // -- set_weather -----------------------------------------------------------------

    public class SetWeatherTool : CompanionToolBase
    {
        public override string Name { get { return "set_weather"; } }
        public override string Description
        {
            get { return "Changes the weather. Args: weather=Clear|Cloudy|Rain|Storm. One change per game-hour."; }
        }

        public override string Validate(GameState s, string args, out string error)
        {
            error = null;
            var d = ParseArgs(args);
            string w = Get(d, "weather", "");
            try { Enum.Parse(typeof(Weather), w, true); }
            catch { return "weather must be Clear, Cloudy, Rain, or Storm."; }
            if (s.ToolBudgets.IsCoolingDown(Name, s.ElapsedSeconds))
                return "the sky has changed recently enough (cooldown).";
            return null;
        }

        public override string Execute(GameState s, string args)
        {
            var d = ParseArgs(args);
            var w = (Weather)Enum.Parse(typeof(Weather), Get(d, "weather", "Clear"), true);
            s.Weather = w;
            s.WeatherChangedAt = s.ElapsedSeconds;
            s.ToolBudgets.SetCooldown(Name, s.ElapsedSeconds, 3600f);
            string line;
            switch (w)
            {
                case Weather.Clear: line = "The cloud tore open and the vale filled with light."; break;
                case Weather.Cloudy: line = "Cloud piled over the fell, softening every edge."; break;
                case Weather.Rain: line = "Rain began to fall, hissing on the heather."; break;
                default: line = "A storm came down off the high tops, sudden and loud."; break;
            }
            s.Journal.Add(s.ElapsedSeconds, line, JournalCategory.Weather, 0.45f, 1f, null, null, "tool:set_weather");
            return "weather is now " + w + ".";
        }
    }

    // -- add_journal -----------------------------------------------------------------

    public class AddJournalEntryTool : CompanionToolBase
    {
        public override string Name { get { return "add_journal"; } }
        public override string Description
        {
            get { return "Adds a journal entry in Solace's voice. Args: category=<Category>; text=<up to 280 chars>."; }
        }

        public override string Validate(GameState s, string args, out string error)
        {
            error = null;
            var d = ParseArgs(args);
            string cat = Get(d, "category", "");
            try { Enum.Parse(typeof(JournalCategory), cat, true); }
            catch { return "category must be one of: Discovery, Combat, Social, Survival, Weather, Travel, Reflection, System."; }
            string text = Get(d, "text", "");
            if (text.Length == 0) return "text must not be empty.";
            if (text.Length > 280) return "text must be 280 characters or fewer.";
            return null;
        }

        public override string Execute(GameState s, string args)
        {
            var d = ParseArgs(args);
            var cat = (JournalCategory)Enum.Parse(typeof(JournalCategory), Get(d, "category", "Reflection"), true);
            string text = Get(d, "text", "");
            s.Journal.Add(s.ElapsedSeconds, text, cat, 0.5f, 1f, null, null, "tool:add_journal");
            return "journal entry added.";
        }
    }

    // -- reveal_poi -------------------------------------------------------------------

    public class RevealPoiTool : CompanionToolBase
    {
        public override string Name { get { return "reveal_poi"; } }
        public override string Description
        {
            get { return "Reveals the nearest undiscovered place within range. Args: range=10..150 (meters)."; }
        }

        public override string Validate(GameState s, string args, out string error)
        {
            error = null;
            var d = ParseArgs(args);
            float range;
            if (!float.TryParse(Get(d, "range", ""), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out range))
                return "range must be a number.";
            if (range < 10f || range > 150f)
                return "range must be between 10 and 150 meters.";
            if (s.ToolBudgets.IsCoolingDown(Name, s.ElapsedSeconds))
                return "reveal_poi is resting (cooldown).";
            return null;
        }

        public override string Execute(GameState s, string args)
        {
            var d = ParseArgs(args);
            float range = float.Parse(Get(d, "range", "80"),
                System.Globalization.CultureInfo.InvariantCulture);
            PointOfInterest best = null;
            float bestD = float.MaxValue;
            foreach (var p in s.World.Pois)
            {
                if (p.Discovered) continue;
                float dist = V2.Distance(s.Agent.Pos, new V2(p.X, p.Z));
                if (dist <= range && dist < bestD) { bestD = dist; best = p; }
            }
            if (best == null) return "no undiscovered place within range.";
            best.Discovered = true;
            s.Agent.KnownPoiIds.Add(best.Id);
            s.ToolBudgets.SetCooldown(Name, s.ElapsedSeconds, 1800f);
            s.Journal.Add(s.ElapsedSeconds,
                "I looked up and truly saw " + best.DisplayName + " for the first time.",
                JournalCategory.Discovery, 0.55f, 1f, best.Id, null, "tool:reveal_poi");
            return "revealed " + best.DisplayName + ".";
        }
    }
}
