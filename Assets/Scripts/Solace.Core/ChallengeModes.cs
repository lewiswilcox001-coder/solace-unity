// Solace.Core — challenge modes: alternate ways to play the same vale.
//
//   Peaceful  — no gloom-maws, ever. For relaxing watching.
//   Hardcore  — hungrier predators, scarcer food, one life: the first death
//               ends the run (no succession).
//   Speedrun  — race to generation 10. Game-time and wall-clock are recorded
//               to a local leaderboard.
//
// Self-contained by design: ChallengeState persists in a per-slot sidecar
// file (slotN-challenge.json) and the leaderboard in challenge-leaderboard.json,
// so GameState, SaveSystem and Simulation stay untouched. The Unity layer owns
// the wiring (follow-up):
//   - when starting: sim = ChallengeModes.NewChallengeLife(seed, mode);
//                    ch   = ChallengeState.NewRun(mode);
//   - each frame after sim.Step(dt): ChallengeDirector.Tick(sim, ch);
//   - alongside slot saves:          ChallengeSave.Save(dir, slot, ch);
//   - when loading a slot:           ch = ChallengeSave.Load(dir, slot) ?? ChallengeState.NewRun(Standard);
//
// Tuning knobs (PredatorAggression, FoodScarcity) are pure functions with
// documented HOOK points where the sim should consult them. Everything else
// in this file is fully functional without any sim edits.
using System;
using System.Collections.Generic;
using System.IO;

namespace Solace.Core
{
    /// <summary>Alternate rulesets for a run. Standard is the base game.</summary>
    public enum ChallengeMode
    {
        Standard = 0,
        Peaceful = 1,
        Hardcore = 2,
        Speedrun = 3,
    }

    /// <summary>Mode metadata and tuning knobs (pure functions).</summary>
    public static class ChallengeModes
    {
        public const int SpeedrunTargetGeneration = 10;
        public const int LeaderboardSize = 10;

        /// <summary>
        /// The mode of the currently-running sim. Set by the Unity layer
        /// (GameBootstrap) whenever a life is started or loaded; read by sim
        /// systems that need mode-aware tuning (e.g. Seasons berry regrowth).
        /// Defaults to Standard so Core tests and headless runs are unaffected.
        /// </summary>
        public static ChallengeMode ActiveMode = ChallengeMode.Standard;

        public static string DisplayName(ChallengeMode mode)
        {
            switch (mode)
            {
                case ChallengeMode.Peaceful: return "Peaceful";
                case ChallengeMode.Hardcore: return "Hardcore";
                case ChallengeMode.Speedrun: return "Speedrun";
                default: return "Standard";
            }
        }

        public static string Description(ChallengeMode mode)
        {
            switch (mode)
            {
                case ChallengeMode.Peaceful:
                    return "No gloom-maws, ever. Just the vale, the fox, and time. For relaxing watching.";
                case ChallengeMode.Hardcore:
                    return "Hungrier predators, scarcer food, one life — the first death ends the run. No succession.";
                case ChallengeMode.Speedrun:
                    return "Race to generation " + SpeedrunTargetGeneration + ". Game-time and wall-clock are recorded to a local leaderboard.";
                default:
                    return "The vale as it was meant to be watched.";
            }
        }

        /// <summary>How many gloom-maws a fresh life starts with.</summary>
        public static int PredatorCount(ChallengeMode mode)
        {
            switch (mode)
            {
                case ChallengeMode.Peaceful: return 0;
                case ChallengeMode.Hardcore: return 5;
                default: return 3;
            }
        }

        /// <summary>
        /// Predator ferocity multiplier. 1.0 = base game.
        /// HOOK (Entities.StepPredator / AttackTarget): multiply hunt speed
        /// (4.6), bite damage (6-13 vs the fox, 14-24 vs kits) and give-up
        /// range (60) by this; divide the hunger hunt-threshold (55) and the
        /// morale-break health (30) by this.
        /// </summary>
        public static float PredatorAggression(ChallengeMode mode)
        {
            return mode == ChallengeMode.Hardcore ? 1.6f : 1.0f;
        }

        /// <summary>
        /// Food availability multiplier. 1.0 = base game.
        /// HOOK (Seasons.Tick berry regrowth): pRegrow *= FoodScarcity(mode).
        /// The mode itself must be plumbed to the season tick (e.g. a GameState
        /// field or a ChallengeModes.ActiveMode static set by the Unity layer).
        /// </summary>
        public static float FoodScarcity(ChallengeMode mode)
        {
            return mode == ChallengeMode.Hardcore ? 0.55f : 1.0f;
        }

        /// <summary>
        /// Whether death passes the tales to an heir. False only in Hardcore.
        /// HOOK (SaveSystem.KillAgent): when false, skip SucceedOnDeath — the
        /// run ends with the death. Until that hook lands, ChallengeDirector
        /// detects the succession and marks the run over for the UI layer.
        /// </summary>
        public static bool SuccessionAllowed(ChallengeMode mode)
        {
            return mode != ChallengeMode.Hardcore;
        }

        /// <summary>
        /// Builds a life with the mode's setup applied. Peaceful removes the
        /// placed predators; Hardcore adds two extra gloom-maws on a derived
        /// deterministic stream (the sim's own RNG streams are untouched).
        /// </summary>
        public static Simulation NewChallengeLife(int seed, ChallengeMode mode)
        {
            var sim = Simulation.NewLife(seed);
            if (mode == ChallengeMode.Peaceful)
            {
                sim.State.Entities.RemoveAll(e => e.Kind == EntityKind.Predator);
            }
            else if (mode == ChallengeMode.Hardcore)
            {
                int extra = PredatorCount(mode) - 3; // base game places 3
                if (extra > 0) AddExtraPredators(sim, seed, extra);
            }
            return sim;
        }

        private static void AddExtraPredators(Simulation sim, int seed, int count)
        {
            var rng = SeededRandom.Derive(seed, "challenge-hardcore");
            var world = sim.State.World;
            for (int i = 0; i < count; i++)
            {
                V2 spot = FindLand(rng, world, 160f, 260f);
                var e = new EntityState
                {
                    Id = sim.NextEntityId(),
                    Kind = EntityKind.Predator,
                    Name = "gloom-maw",
                    X = spot.X, Z = spot.Z,
                    Health = 100f,
                    Behavior = "Roam",
                    HomeX = spot.X, HomeZ = spot.Z,
                    Hunger = 45f,
                    TargetX = spot.X, TargetZ = spot.Z,
                    Facing = rng.NextFloat(0f, (float)Math.PI * 2f),
                };
                sim.State.Entities.Add(e);
            }
        }

        private static V2 FindLand(SeededRandom rng, WorldData world, float minR, float maxR)
        {
            for (int t = 0; t < 80; t++)
            {
                float ang = rng.NextFloat(0f, (float)Math.PI * 2f);
                float r = rng.NextFloat(minR, maxR);
                float x = (float)Math.Cos(ang) * r;
                float z = (float)Math.Sin(ang) * r;
                if (Math.Abs(x) > world.HalfSize - 20f || Math.Abs(z) > world.HalfSize - 20f) continue;
                float h = world.SampleHeight(x, z);
                if (h < WorldData.WaterLevel + 0.6f) continue;
                if (world.SlopeAt(x, z) > 0.7f) continue;
                return new V2(x, z);
            }
            return new V2(minR, 0f);
        }

        /// <summary>Formats a duration in seconds as H:MM:SS (or Dd H:MM:SS).</summary>
        public static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            long s = (long)Math.Floor(totalSeconds);
            long d = s / 86400; s -= d * 86400;
            long h = s / 3600; s -= h * 3600;
            long m = s / 60; s -= m * 60;
            string t = h + ":" + m.ToString("D2") + ":" + s.ToString("D2");
            return d > 0 ? d + "d " + t : t;
        }
    }

    /// <summary>
    /// Serializable per-run challenge state. Lives in the slot sidecar file,
    /// not in GameState, so challenge runs never disturb standard saves.
    /// </summary>
    public class ChallengeState
    {
        public ChallengeMode Mode = ChallengeMode.Standard;
        /// <summary>Hardcore: set when the first death ends the run.</summary>
        public bool RunOver = false;
        public int DeathGeneration = 0;
        public string DeathCause = "";
        /// <summary>Last generation seen by the director (edge detection).</summary>
        public int LastSeenGeneration = 0;
        public double SpeedrunStartGameSeconds = 0.0;
        public long SpeedrunStartRealTicks = 0;
        public bool SpeedrunComplete = false;
        public double SpeedrunGameSeconds = 0.0;
        public double SpeedrunRealSeconds = 0.0;
        /// <summary>
        /// The entry for the just-finished speedrun, for the Unity layer to
        /// record via ChallengeSave.RecordRun. Transient: never serialized.
        /// </summary>
        public SpeedrunEntry LastSpeedrunEntry = null;

        public static ChallengeState NewRun(ChallengeMode mode)
        {
            return new ChallengeState
            {
                Mode = mode,
                SpeedrunStartRealTicks = DateTime.UtcNow.Ticks,
            };
        }

        public double SpeedrunElapsedGame(GameState s)
        {
            return Math.Max(0.0, s.ElapsedSeconds - SpeedrunStartGameSeconds);
        }

        public double SpeedrunElapsedReal()
        {
            if (SpeedrunStartRealTicks <= 0) return 0.0;
            return Math.Max(0.0, (DateTime.UtcNow - new DateTime(SpeedrunStartRealTicks, DateTimeKind.Utc)).TotalSeconds);
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("mode", (int)Mode);
            o.Add("runOver", RunOver);
            o.Add("deathGeneration", DeathGeneration);
            o.Add("deathCause", DeathCause ?? "");
            o.Add("lastSeenGeneration", LastSeenGeneration);
            o.Add("speedrunStartGameSeconds", SpeedrunStartGameSeconds);
            o.Add("speedrunStartRealTicks", SpeedrunStartRealTicks.ToString());
            o.Add("speedrunComplete", SpeedrunComplete);
            o.Add("speedrunGameSeconds", SpeedrunGameSeconds);
            o.Add("speedrunRealSeconds", SpeedrunRealSeconds);
            return o;
        }

        private static long ParseTicks(JsonObject o, string key)
        {
            long v;
            return long.TryParse(JsonHelpers.GetString(o, key, "0"), out v) ? v : 0;
        }

        public static ChallengeState FromJson(JsonObject o)
        {
            var c = new ChallengeState();
            c.Mode = (ChallengeMode)JsonHelpers.GetInt(o, "mode", 0);
            c.RunOver = JsonHelpers.GetBool(o, "runOver", false);
            c.DeathGeneration = JsonHelpers.GetInt(o, "deathGeneration", 0);
            c.DeathCause = JsonHelpers.GetString(o, "deathCause", "");
            c.LastSeenGeneration = JsonHelpers.GetInt(o, "lastSeenGeneration", 0);
            c.SpeedrunStartGameSeconds = JsonHelpers.GetDouble(o, "speedrunStartGameSeconds", 0.0);
            c.SpeedrunStartRealTicks = ParseTicks(o, "speedrunStartRealTicks");
            c.SpeedrunComplete = JsonHelpers.GetBool(o, "speedrunComplete", false);
            c.SpeedrunGameSeconds = JsonHelpers.GetDouble(o, "speedrunGameSeconds", 0.0);
            c.SpeedrunRealSeconds = JsonHelpers.GetDouble(o, "speedrunRealSeconds", 0.0);
            return c;
        }
    }

    /// <summary>One finished speedrun, as stored on the local leaderboard.</summary>
    public class SpeedrunEntry
    {
        public int Seed;
        public string FoxName = "";
        public double GameSeconds;
        public double RealSeconds;
        public long DateUtcTicks;
        public int Generations = ChallengeModes.SpeedrunTargetGeneration;

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("seed", Seed);
            o.Add("foxName", FoxName ?? "");
            o.Add("gameSeconds", GameSeconds);
            o.Add("realSeconds", RealSeconds);
            o.Add("dateUtcTicks", DateUtcTicks.ToString());
            o.Add("generations", Generations);
            return o;
        }

        public static SpeedrunEntry FromJson(JsonObject o)
        {
            return new SpeedrunEntry
            {
                Seed = JsonHelpers.GetInt(o, "seed", 0),
                FoxName = JsonHelpers.GetString(o, "foxName", ""),
                GameSeconds = JsonHelpers.GetDouble(o, "gameSeconds", 0.0),
                RealSeconds = JsonHelpers.GetDouble(o, "realSeconds", 0.0),
                DateUtcTicks = ParseTicks(o, "dateUtcTicks"),
                Generations = JsonHelpers.GetInt(o, "generations", ChallengeModes.SpeedrunTargetGeneration),
            };
        }

        private static long ParseTicks(JsonObject o, string key)
        {
            long v;
            return long.TryParse(JsonHelpers.GetString(o, key, "0"), out v) ? v : 0;
        }
    }

    /// <summary>
    /// Per-frame/per-step challenge logic. Call after sim.Step(dt), every frame.
    /// Idempotent: flags guard every one-shot. Consumes no RNG (deterministic).
    /// Detection only, except the Peaceful predator sweep (enforcement).
    /// </summary>
    public static class ChallengeDirector
    {
        public static void Tick(Simulation sim, ChallengeState ch)
        {
            if (sim == null || ch == null) return;
            if (ch.RunOver || ch.Mode == ChallengeMode.Standard) return;

            int gen = sim.State.Lineage.Generation;
            if (ch.LastSeenGeneration == 0)
                ch.LastSeenGeneration = gen; // first sight: baseline, no edge

            if (ch.Mode == ChallengeMode.Peaceful)
                SweepPredators(sim);

            if (ch.Mode == ChallengeMode.Hardcore && !ch.RunOver && gen > ch.LastSeenGeneration)
                OnHardcoreDeath(sim, ch, ch.LastSeenGeneration);

            if (ch.Mode == ChallengeMode.Speedrun && !ch.SpeedrunComplete)
                CheckSpeedrun(sim, ch, gen);

            ch.LastSeenGeneration = gen;
        }

        /// <summary>
        /// Peaceful enforcement: any gloom-maw that appears (e.g. the hollow
        /// hive's 30% ambush) is quietly put down. No RNG, no journal — the
        /// fox only ever thinks it saw shadows move.
        /// </summary>
        private static void SweepPredators(Simulation sim)
        {
            foreach (var e in sim.State.Entities)
            {
                if (e.Kind == EntityKind.Predator && e.IsAlive)
                {
                    e.Health = 0f;
                    e.Behavior = "Dead";
                }
            }
        }

        private static void OnHardcoreDeath(Simulation sim, ChallengeState ch, int deathGen)
        {
            ch.RunOver = true;
            ch.DeathGeneration = deathGen;
            ch.DeathCause = LastChapterCause(sim);
            sim.Journal((float)sim.State.ElapsedSeconds,
                "The light has gone out. This was a hardcore run — there is no heir, no next chapter. " +
                "The vale keeps the tales now.",
                JournalCategory.Chapter, 1.0f);
        }

        private static string LastChapterCause(Simulation sim)
        {
            var chapters = sim.State.Lineage.Chapters;
            if (chapters.Count == 0) return "";
            return chapters[chapters.Count - 1].Cause ?? "";
        }

        private static void CheckSpeedrun(Simulation sim, ChallengeState ch, int gen)
        {
            int target = ChallengeModes.SpeedrunTargetGeneration;
            if (gen < target || ch.LastSeenGeneration >= target) return; // edge: crossing the line
            ch.SpeedrunComplete = true;
            ch.SpeedrunGameSeconds = ch.SpeedrunElapsedGame(sim.State);
            ch.SpeedrunRealSeconds = ch.SpeedrunElapsedReal();
            var entry = new SpeedrunEntry
            {
                Seed = sim.State.Seed,
                FoxName = sim.State.Agent != null ? sim.State.Agent.Name : "",
                GameSeconds = ch.SpeedrunGameSeconds,
                RealSeconds = ch.SpeedrunRealSeconds,
                DateUtcTicks = DateTime.UtcNow.Ticks,
                Generations = target,
            };
            // The finished entry is stashed on the state for the Unity layer,
            // which records it via ChallengeSave.RecordRun(dir, ch.LastSpeedrunEntry).
            sim.Journal((float)sim.State.ElapsedSeconds,
                "Generation " + target + "! The speedrun is complete — " +
                ChallengeModes.FormatDuration(ch.SpeedrunGameSeconds) + " of vale-time, " +
                ChallengeModes.FormatDuration(ch.SpeedrunRealSeconds) + " watched. " +
                "The chroniclers will argue about this one for years.",
                JournalCategory.Chapter, 1.0f);
            ch.LastSpeedrunEntry = entry;
        }
    }

    /// <summary>
    /// Sidecar persistence: slotN-challenge.json per slot plus a global
    /// challenge-leaderboard.json. Atomic writes, never throws — mirrors the
    /// SaveSlots conventions.
    /// </summary>
    public static class ChallengeSave
    {
        public static string ChallengeFile(string dir, int slot)
        {
            return Path.Combine(dir, "slot" + slot + "-challenge.json");
        }

        public static string LeaderboardFile(string dir)
        {
            return Path.Combine(dir, "challenge-leaderboard.json");
        }

        public static void Save(string dir, int slot, ChallengeState ch)
        {
            if (ch == null) return;
            try
            {
                Directory.CreateDirectory(dir);
                string dst = ChallengeFile(dir, slot);
                string tmp = dst + ".tmp";
                File.WriteAllText(tmp, ch.ToJson().ToJson());
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
            }
            catch { }
        }

        /// <summary>Returns null when the slot has no challenge state (standard play).</summary>
        public static ChallengeState Load(string dir, int slot)
        {
            try
            {
                string path = ChallengeFile(dir, slot);
                if (!File.Exists(path)) return null;
                var o = JsonValue.Parse(File.ReadAllText(path)).AsObject();
                return ChallengeState.FromJson(o);
            }
            catch
            {
                return null;
            }
        }

        public static void Delete(string dir, int slot)
        {
            try
            {
                string path = ChallengeFile(dir, slot);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        public static List<SpeedrunEntry> LoadLeaderboard(string dir)
        {
            var list = new List<SpeedrunEntry>();
            try
            {
                string path = LeaderboardFile(dir);
                if (!File.Exists(path)) return list;
                var arr = JsonValue.Parse(File.ReadAllText(path)).AsArray();
                for (int i = 0; i < arr.Count; i++)
                    list.Add(SpeedrunEntry.FromJson(arr[i].AsObject()));
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Records a finished run. Kept to the fastest LeaderboardSize entries
        /// by game-time. Returns the rank (1-based) or -1 if it didn't place.
        /// </summary>
        public static int RecordRun(string dir, SpeedrunEntry entry)
        {
            if (entry == null) return -1;
            try
            {
                var list = LoadLeaderboard(dir);
                list.Add(entry);
                list.Sort((a, b) => a.GameSeconds.CompareTo(b.GameSeconds));
                int rank = -1;
                for (int i = 0; i < list.Count; i++)
                    if (ReferenceEquals(list[i], entry)) { rank = i + 1; break; }
                while (list.Count > ChallengeModes.LeaderboardSize)
                    list.RemoveAt(list.Count - 1);
                if (rank > ChallengeModes.LeaderboardSize) rank = -1;
                var arr = new JsonArray();
                foreach (var e in list) arr.Add(e.ToJson());
                Directory.CreateDirectory(dir);
                string dst = LeaderboardFile(dir);
                string tmp = dst + ".tmp";
                File.WriteAllText(tmp, arr.ToJson());
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
                return rank;
            }
            catch
            {
                return -1;
            }
        }
    }
}
