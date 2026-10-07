// Solace.Core — the daily vale: one shared world per day, Wordle-style.
//
// Everyone gets the same seed for a given calendar date (UTC, so it's the
// same world for every player). Daily worlds live outside the 3 save slots
// as daily-YYYY-MM-DD.json. Streaks track consecutive days visited in
// daily-streak.json (account-level, not per-save).
// Pure System.IO + System — no Unity dependency, fully unit-testable.
using System;
using System.Collections.Generic;
using System.IO;

namespace Solace.Core
{
    /// <summary>
    /// Transient per-boot info about a daily world. Never serialized — the
    /// boot layer sets it when opening today's vale (GameState JSON ignores it).
    /// </summary>
    public class DailyVisitInfo
    {
        public bool IsDailyWorld;
        public string DateString = ""; // "2026-10-06"
        public int StreakDays;
    }

    public static class DailyVale
    {
        // -- dates & seeds ---------------------------------------------------------

        /// <summary>Canonical date key: "2026-10-06". Culture-invariant.</summary>
        public static string DateString(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        /// <summary>Deterministic seed for a calendar date. Same date (UTC)
        /// gives the same seed for every player, on every platform.</summary>
        public static int SeedFor(DateTime date)
        {
            return SeedFor(DateString(date));
        }

        /// <summary>FNV-1a 32-bit hash of the date key, folded into 1..999999999
        /// (the same range NewLifeInSlot draws random seeds from).</summary>
        public static int SeedFor(string dateString)
        {
            uint h = 2166136261u;
            foreach (char c in dateString)
            {
                h ^= c;
                h *= 16777619u;
            }
            return (int)(h % 999999999u) + 1;
        }

        /// <summary>"2026-10-06" -> "Oct 6". Falls back to the raw key.</summary>
        public static string PrettyDate(string dateString)
        {
            try
            {
                string[] months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                    "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
                var parts = dateString.Split('-');
                int m = int.Parse(parts[1]);
                int d = int.Parse(parts[2]);
                if (m >= 1 && m <= 12 && d >= 1 && d <= 31)
                    return months[m - 1] + " " + d;
            }
            catch { }
            return dateString;
        }

        // -- daily world files ------------------------------------------------------

        public static string DailyFile(string dir, string dateString)
        {
            return Path.Combine(dir, "daily-" + dateString + ".json");
        }

        public static string DailyFile(string dir, DateTime date)
        {
            return DailyFile(dir, DateString(date));
        }

        public static bool DailyExists(string dir, DateTime date)
        {
            try { return File.Exists(DailyFile(dir, date)); }
            catch { return false; }
        }

        /// <summary>Raw JSON for a date's daily world, or null when unvisited.</summary>
        public static string LoadDailyJson(string dir, DateTime date)
        {
            try
            {
                string path = DailyFile(dir, date);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch { return null; }
        }

        /// <summary>Atomic write (tmp + move) of a daily world. No backup
        /// rotation — each date is its own file, so history is the backup.</summary>
        public static void SaveDaily(string dir, DateTime date, GameState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            Directory.CreateDirectory(dir);
            string dst = DailyFile(dir, date);
            string json = SaveSystem.Save(state);
            string tmp = dst + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
        }

        /// <summary>How long since this date's daily world was last saved.</summary>
        public static TimeSpan DailyAwayTime(string dir, DateTime date)
        {
            try
            {
                string path = DailyFile(dir, date);
                if (!File.Exists(path)) return TimeSpan.Zero;
                TimeSpan away = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                return away.TotalSeconds > 0 ? away : TimeSpan.Zero;
            }
            catch { return TimeSpan.Zero; }
        }

        // -- streaks ------------------------------------------------------------------

        private static string StreakFile(string dir)
        {
            return Path.Combine(dir, "daily-streak.json");
        }

        public class StreakData
        {
            public List<string> VisitedDates = new List<string>(); // "2026-10-06"

            public JsonObject ToJson()
            {
                var o = new JsonObject();
                var arr = new JsonArray();
                foreach (var d in VisitedDates) arr.Add(d);
                o.Add("visitedDates", arr);
                return o;
            }

            public static StreakData FromJson(JsonObject o)
            {
                var s = new StreakData();
                JsonValue v;
                if (o.TryGet("visitedDates", out v) && !v.IsNull)
                {
                    var arr = v.AsArray();
                    for (int i = 0; i < arr.Count; i++) s.VisitedDates.Add(arr[i].AsString());
                }
                return s;
            }
        }

        public static StreakData LoadStreak(string dir)
        {
            try
            {
                string path = StreakFile(dir);
                if (!File.Exists(path)) return new StreakData();
                return StreakData.FromJson(JsonValue.Parse(File.ReadAllText(path)).AsObject());
            }
            catch { return new StreakData(); }
        }

        private static void SaveStreak(string dir, StreakData data)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string dst = StreakFile(dir);
                string tmp = dst + ".tmp";
                File.WriteAllText(tmp, data.ToJson().ToJson());
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
            }
            catch { }
        }

        /// <summary>
        /// Records a visit for the given date (deduped), prunes history to the
        /// last 90 dates, persists, and returns the current streak.
        /// </summary>
        public static int RecordVisit(string dir, DateTime date)
        {
            var data = LoadStreak(dir);
            string key = DateString(date);
            if (!data.VisitedDates.Contains(key))
                data.VisitedDates.Add(key);
            data.VisitedDates.Sort();
            while (data.VisitedDates.Count > 90)
                data.VisitedDates.RemoveAt(0);
            SaveStreak(dir, data);
            return CurrentStreak(data, date);
        }

        /// <summary>
        /// Consecutive visited days ending today. If today isn't visited yet,
        /// counts the streak ending yesterday (so the menu can show "visit
        /// today to keep your N-day streak").
        /// </summary>
        public static int CurrentStreak(StreakData data, DateTime today)
        {
            if (data == null || data.VisitedDates.Count == 0) return 0;
            var set = new HashSet<string>(data.VisitedDates);
            DateTime cursor = set.Contains(DateString(today)) ? today.Date : today.Date.AddDays(-1);
            int n = 0;
            while (set.Contains(DateString(cursor)))
            {
                n++;
                cursor = cursor.AddDays(-1);
            }
            return n;
        }

        public static int CurrentStreak(string dir, DateTime today)
        {
            return CurrentStreak(LoadStreak(dir), today);
        }
    }
}
