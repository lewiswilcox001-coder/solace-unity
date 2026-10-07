// Solace.Core — milestones/achievements: celebration of moments, not game logic.
// Detection only: this system READS state and unlocks milestones. It never
// changes how the fox behaves, how combat resolves, or how the world works.
// Lewis checks in like TikTok — milestones give each check-in a "did you see?!" moment.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public enum MilestoneCategory
    {
        Survival,
        Discovery,
        Lineage,
        Combat,
        Social,
        Seasons,
        Dreams,
        Secrets,   // hidden wonders: the rarest gifts in the vale
        Daily      // the shared daily vale: one world, everyone, every day
    }

    /// <summary>Static definition of a milestone. All 29 are defined in MilestoneDefs.All.</summary>
    public class MilestoneDef
    {
        public string Id;
        public string Name;
        public string Description;
        public MilestoneCategory Category;

        public MilestoneDef(string id, string name, string desc, MilestoneCategory cat)
        {
            Id = id; Name = name; Description = desc; Category = cat;
        }
    }

    public static class MilestoneDefs
    {
        public static readonly List<MilestoneDef> All = new List<MilestoneDef>
        {
            // -- survival --
            new MilestoneDef("first_dawn", "First Dawn",
                "Survived your first full day in the vale.", MilestoneCategory.Survival),
            new MilestoneDef("week_survivor", "Weekling",
                "Survived 7 days.", MilestoneCategory.Survival),
            new MilestoneDef("month_survivor", "Seasoned",
                "Survived 30 days.", MilestoneCategory.Survival),
            new MilestoneDef("yearling", "Yearling",
                "Survived a full turning of the year — 80 days.", MilestoneCategory.Survival),

            // -- discovery --
            new MilestoneDef("wanderer", "Wanderer",
                "Discovered 5 places.", MilestoneCategory.Discovery),
            new MilestoneDef("explorer", "Explorer",
                "Discovered 15 places.", MilestoneCategory.Discovery),
            new MilestoneDef("cartographer", "Cartographer",
                "Found every kind of place in the vale.", MilestoneCategory.Discovery),
            new MilestoneDef("ruin_delver", "Ruin Delver",
                "Stood among the insectile ruins.", MilestoneCategory.Discovery),
            new MilestoneDef("hot_spring", "Warm Waters",
                "Discovered a hot spring.", MilestoneCategory.Discovery),
            new MilestoneDef("crystal_cave", "Crystal Grotto",
                "Discovered a crystal cave.", MilestoneCategory.Discovery),

            // -- lineage --
            new MilestoneDef("new_blood", "New Blood",
                "The first kit was born.", MilestoneCategory.Lineage),
            new MilestoneDef("second_gen", "The Story Continues",
                "The second generation took up the tales.", MilestoneCategory.Lineage),
            new MilestoneDef("dynasty", "Dynasty",
                "Five generations of lantern-foxes.", MilestoneCategory.Lineage),
            new MilestoneDef("immortal_line", "Immortal Line",
                "Ten generations. The chronicle endures.", MilestoneCategory.Lineage),
            new MilestoneDef("tale_weaver", "Tale Weaver",
                "Gathered 10 tales.", MilestoneCategory.Lineage),
            new MilestoneDef("legend", "Legend",
                "Gathered 25 tales. The valley remembers.", MilestoneCategory.Lineage),

            // -- combat --
            new MilestoneDef("first_blood", "First Blood",
                "Drove off a gloom-maw in combat.", MilestoneCategory.Combat),
            new MilestoneDef("predator_bane", "Predator's Bane",
                "Defeated 5 gloom-maws.", MilestoneCategory.Combat),
            new MilestoneDef("apex", "Apex",
                "Defeated 10 gloom-maws. Nothing hunts you.", MilestoneCategory.Combat),

            // -- social --
            new MilestoneDef("first_friend", "First Friend",
                "Greeted another of your kind.", MilestoneCategory.Social),
            new MilestoneDef("bonded", "Bonded",
                "Formed a deep pair-bond.", MilestoneCategory.Social),
            new MilestoneDef("social_butterfly", "Known Far and Wide",
                "Greeted 10 different kindred.", MilestoneCategory.Social),

            // -- seasons --
            new MilestoneDef("spring_survivor", "Spring's Promise",
                "Lived through spring.", MilestoneCategory.Seasons),
            new MilestoneDef("summer_survivor", "Summer's Bounty",
                "Lived through summer.", MilestoneCategory.Seasons),
            new MilestoneDef("autumn_survivor", "Autumn's Change",
                "Lived through autumn.", MilestoneCategory.Seasons),
            new MilestoneDef("winter_survivor", "Winter's Grip",
                "Survived your first winter.", MilestoneCategory.Seasons),
            new MilestoneDef("full_cycle", "Full Cycle",
                "Lived through all four seasons.", MilestoneCategory.Seasons),

            // -- dreams --
            new MilestoneDef("dreamer", "Dreamer",
                "Dreamed the first prophetic dream.", MilestoneCategory.Dreams),
            new MilestoneDef("dreamwalker", "Dreamwalker",
                "Saw a dream come true.", MilestoneCategory.Dreams),
            new MilestoneDef("prophet", "Prophet",
                "Five dreams fulfilled. The sleep-seer.", MilestoneCategory.Dreams),

            // -- secrets (hidden wonders) --
            new MilestoneDef("rainbow_grove", "Where Light Lives",
                "Found the hidden rainbow grove. One world in a hundred grows one.",
                MilestoneCategory.Secrets),
            new MilestoneDef("fox_spirit", "The Watcher",
                "Saw the ancient fox spirit on a full-moon night.", MilestoneCategory.Secrets),
            new MilestoneDef("meteor_shower", "Skyfall",
                "Witnessed a meteor shower.", MilestoneCategory.Secrets),
            new MilestoneDef("curiosity", "Curiosity",
                "Found every secret in the vale. Some foxes are just built that way.",
                MilestoneCategory.Secrets),

            // -- daily (the shared daily vale) --
            new MilestoneDef("daily_first", "Today's Vale",
                "Wandered the daily vale — the same world everyone shares today.",
                MilestoneCategory.Daily),
            new MilestoneDef("daily_streak_7", "A Week of Vales",
                "Visited the daily vale 7 days in a row.", MilestoneCategory.Daily),
            new MilestoneDef("daily_streak_30", "Vale Devotee",
                "Visited the daily vale 30 days in a row.", MilestoneCategory.Daily),
        };

        private static Dictionary<string, MilestoneDef> _byId;

        public static MilestoneDef ById(string id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<string, MilestoneDef>();
                foreach (var d in All) _byId[d.Id] = d;
            }
            MilestoneDef def;
            return _byId.TryGetValue(id, out def) ? def : null;
        }
    }

    /// <summary>
    /// Serializable milestone progress. Lives on GameState. Detection runs in
    /// MilestoneSystem.Tick; this is just data + unlock bookkeeping.
    /// </summary>
    public class MilestoneState
    {
        // Parallel arrays for JSON (dictionaries don't serialize in this codebase's Json).
        public List<string> UnlockedIds = new List<string>();
        public List<float> UnlockedAt = new List<float>(); // game seconds

        // Running counters for edge detection.
        public int TotalKills;
        public int TotalKitsBorn;

        // Bounded ID lists for birth/death edge detection (pruned, never unbounded).
        public List<int> SeenKitIds = new List<int>();
        public List<int> SeenDeadPredatorIds = new List<int>();

        // Seasons survived (completed a full season).
        public List<string> SeasonsSurvived = new List<string>();
        public string LastSeason = "";

        private HashSet<string> _unlockedSet;

        public bool IsUnlocked(string id)
        {
            if (_unlockedSet == null) RebuildSet();
            return _unlockedSet.Contains(id);
        }

        private void RebuildSet()
        {
            _unlockedSet = new HashSet<string>(UnlockedIds);
        }

        /// <summary>Unlocks if not already. Returns true if newly unlocked.</summary>
        public bool Unlock(string id, double nowSeconds)
        {
            if (IsUnlocked(id)) return false;
            UnlockedIds.Add(id);
            UnlockedAt.Add((float)nowSeconds);
            _unlockedSet.Add(id);
            return true;
        }

        public int UnlockedCount { get { return UnlockedIds.Count; } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var ids = new JsonArray();
            foreach (var id in UnlockedIds) ids.Add(id);
            o.Add("unlockedIds", ids);
            var times = new JsonArray();
            foreach (var t in UnlockedAt) times.Add(t);
            o.Add("unlockedAt", times);
            o.Add("totalKills", TotalKills);
            o.Add("totalKitsBorn", TotalKitsBorn);
            var sk = new JsonArray();
            foreach (var id in SeenKitIds) sk.Add(id);
            o.Add("seenKitIds", sk);
            var sp = new JsonArray();
            foreach (var id in SeenDeadPredatorIds) sp.Add(id);
            o.Add("seenDeadPredatorIds", sp);
            var ss = new JsonArray();
            foreach (var s in SeasonsSurvived) ss.Add(s);
            o.Add("seasonsSurvived", ss);
            o.Add("lastSeason", LastSeason ?? "");
            return o;
        }

        public static MilestoneState FromJson(JsonObject o)
        {
            var m = new MilestoneState();
            JsonValue v;
            if (o.TryGet("unlockedIds", out v) && !v.IsNull)
            {
                var arr = v.AsArray();
                for (int i = 0; i < arr.Count; i++) m.UnlockedIds.Add(arr[i].AsString());
            }
            if (o.TryGet("unlockedAt", out v) && !v.IsNull)
            {
                var arr = v.AsArray();
                for (int i = 0; i < arr.Count; i++) m.UnlockedAt.Add(((JsonNumber)arr[i]).AsFloat());
            }
            m.TotalKills = JsonHelpers.GetInt(o, "totalKills", 0);
            m.TotalKitsBorn = JsonHelpers.GetInt(o, "totalKitsBorn", 0);
            if (o.TryGet("seenKitIds", out v) && !v.IsNull)
            {
                var arr = v.AsArray();
                for (int i = 0; i < arr.Count; i++) m.SeenKitIds.Add(((JsonNumber)arr[i]).AsInt());
            }
            if (o.TryGet("seenDeadPredatorIds", out v) && !v.IsNull)
            {
                var arr = v.AsArray();
                for (int i = 0; i < arr.Count; i++) m.SeenDeadPredatorIds.Add(((JsonNumber)arr[i]).AsInt());
            }
            if (o.TryGet("seasonsSurvived", out v) && !v.IsNull)
            {
                var arr = v.AsArray();
                for (int i = 0; i < arr.Count; i++) m.SeasonsSurvived.Add(arr[i].AsString());
            }
            m.LastSeason = JsonHelpers.GetString(o, "lastSeason", "");
            return m;
        }
    }

    /// <summary>
    /// Detection only. Call Tick(sim) once per FixedStep (or less often — it's
    /// cheap). Returns the IDs of milestones unlocked this tick, for celebration.
    /// Never mutates game state except MilestoneState.
    /// </summary>
    public static class MilestoneSystem
    {
        private const int MaxTrackedIds = 400; // bound the edge-detection lists

        public static List<string> Tick(Simulation sim)
        {
            var newly = new List<string>();
            var s = sim.State;
            var ms = s.Milestones;
            double now = s.ElapsedSeconds;

            // -- edge detection: kit births --
            foreach (var kit in s.Kits)
            {
                if (!ms.SeenKitIds.Contains(kit.Id))
                {
                    ms.SeenKitIds.Add(kit.Id);
                    ms.TotalKitsBorn++;
                }
            }
            Prune(ms.SeenKitIds);

            // -- edge detection: predator kills --
            foreach (var e in s.Entities)
            {
                if (e.Kind == EntityKind.Predator && !e.IsAlive &&
                    !ms.SeenDeadPredatorIds.Contains(e.Id))
                {
                    ms.SeenDeadPredatorIds.Add(e.Id);
                    ms.TotalKills++;
                }
            }
            Prune(ms.SeenDeadPredatorIds);

            // -- edge detection: season completion --
            string curSeason = SeasonSystem.Current(s).ToString();
            if (ms.LastSeason == "")
            {
                ms.LastSeason = curSeason; // first tick: just record
            }
            else if (curSeason != ms.LastSeason)
            {
                // We lived through LastSeason to reach the new one.
                if (!ms.SeasonsSurvived.Contains(ms.LastSeason))
                    ms.SeasonsSurvived.Add(ms.LastSeason);
                ms.LastSeason = curSeason;
            }

            // -- survival --
            Check(ms, newly, "first_dawn", now >= 86400, now);
            Check(ms, newly, "week_survivor", now >= 86400 * 7, now);
            Check(ms, newly, "month_survivor", now >= 86400 * 30, now);
            Check(ms, newly, "yearling", now >= 86400 * 80, now);

            // -- discovery --
            int poiCount = 0;
            bool hasRuin = false, hasSpring = false, hasCave = false;
            var typesSeen = new HashSet<PoiType>();
            if (s.World != null && s.World.Pois != null)
            {
                foreach (var p in s.World.Pois)
                {
                    if (!p.Discovered) continue;
                    poiCount++;
                    typesSeen.Add(p.Type);
                    if (p.Type == PoiType.InsectileRuin) hasRuin = true;
                    if (p.Type == PoiType.HotSpring) hasSpring = true;
                    if (p.Type == PoiType.CrystalCave) hasCave = true;
                }
            }
            Check(ms, newly, "wanderer", poiCount >= 5, now);
            Check(ms, newly, "explorer", poiCount >= 15, now);
            Check(ms, newly, "cartographer", typesSeen.Count >= 10, now);
            Check(ms, newly, "ruin_delver", hasRuin, now);
            Check(ms, newly, "hot_spring", hasSpring, now);
            Check(ms, newly, "crystal_cave", hasCave, now);

            // -- lineage --
            Check(ms, newly, "new_blood", ms.TotalKitsBorn >= 1, now);
            Check(ms, newly, "second_gen", s.Lineage.Generation >= 2, now);
            Check(ms, newly, "dynasty", s.Lineage.Generation >= 5, now);
            Check(ms, newly, "immortal_line", s.Lineage.Generation >= 10, now);
            Check(ms, newly, "tale_weaver", s.Lineage.Tales.Count >= 10, now);
            Check(ms, newly, "legend", s.Lineage.Tales.Count >= 25, now);

            // -- combat --
            Check(ms, newly, "first_blood", ms.TotalKills >= 1, now);
            Check(ms, newly, "predator_bane", ms.TotalKills >= 5, now);
            Check(ms, newly, "apex", ms.TotalKills >= 10, now);

            // -- social --
            int greeted = 0;
            foreach (var p in s.Social.People)
                if (p.Greetings > 0) greeted++;
            Check(ms, newly, "first_friend", greeted >= 1, now);
            bool bonded = s.Agent != null && s.Agent.Bond != null && s.Agent.Bond.Strength > 0.7f;
            Check(ms, newly, "bonded", bonded, now);
            Check(ms, newly, "social_butterfly", greeted >= 10, now);

            // -- seasons --
            Check(ms, newly, "spring_survivor", ms.SeasonsSurvived.Contains("Spring"), now);
            Check(ms, newly, "summer_survivor", ms.SeasonsSurvived.Contains("Summer"), now);
            Check(ms, newly, "autumn_survivor", ms.SeasonsSurvived.Contains("Autumn"), now);
            Check(ms, newly, "winter_survivor", ms.SeasonsSurvived.Contains("Winter"), now);
            Check(ms, newly, "full_cycle",
                ms.SeasonsSurvived.Contains("Spring") && ms.SeasonsSurvived.Contains("Summer") &&
                ms.SeasonsSurvived.Contains("Autumn") && ms.SeasonsSurvived.Contains("Winter"), now);

            // -- dreams --
            int dreamCount = s.Dreams != null ? s.Dreams.Dreams.Count : 0;
            int fulfilled = 0;
            if (s.Dreams != null)
                foreach (var d in s.Dreams.Dreams)
                    if (d.Status == DreamStatus.Fulfilled) fulfilled++;
            Check(ms, newly, "dreamer", dreamCount >= 1, now);
            Check(ms, newly, "dreamwalker", fulfilled >= 1, now);
            Check(ms, newly, "prophet", fulfilled >= 5, now);

            // -- secrets (hidden wonders) --
            bool groveFound = false;
            if (s.World != null && s.World.Pois != null)
            {
                foreach (var p in s.World.Pois)
                    if (p.Type == PoiType.RainbowGrove && p.Discovered) { groveFound = true; break; }
            }
            Check(ms, newly, "rainbow_grove", groveFound, now);
            Check(ms, newly, "fox_spirit", s.Eggs.SpiritSeen, now);
            Check(ms, newly, "meteor_shower", s.Eggs.MeteorSeen, now);
            Check(ms, newly, "curiosity",
                groveFound && s.Eggs.SpiritSeen && s.Eggs.MeteorSeen, now);

            // -- daily (the shared daily vale) --
            if (s.DailyInfo != null && s.DailyInfo.IsDailyWorld)
            {
                Check(ms, newly, "daily_first", true, now);
                Check(ms, newly, "daily_streak_7", s.DailyInfo.StreakDays >= 7, now);
                Check(ms, newly, "daily_streak_30", s.DailyInfo.StreakDays >= 30, now);
            }

            return newly;
        }

        private static void Check(MilestoneState ms, List<string> newly,
                                  string id, bool condition, double now)
        {
            if (condition && ms.Unlock(id, now))
                newly.Add(id);
        }

        private static void Prune(List<int> list)
        {
            while (list.Count > MaxTrackedIds)
                list.RemoveAt(0);
        }
    }
}
