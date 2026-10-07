// Solace.Core — easter eggs and secrets.
//
// These are GIFTS for Lewis, who loves discovery. They never change gameplay:
// no stats, no advantages, no AI influence. They exist to delight.
//
//   1. Rainbow grove — a hidden grove of rainbow crystals. Only ~1% of worlds
//      grow one. Finding it is a once-in-many-lives moment.
//   2. Ancient fox spirit — on full-moon nights, a ghostly fox may appear,
//      watch the protagonist for a while, then vanish. Visual only.
//   3. Meteor shower — a rare night event. Shooting stars, and a journal entry.
//
// All randomness flows through sim.EventRng (deterministic). All state is
// serializable. The "Curiosity" hidden achievement (all three found) is
// detected in MilestoneSystem.Tick.
using System;

namespace Solace.Core
{
    /// <summary>
    /// Serializable easter-egg state. Lives on GameState.
    /// Visual flags (SpiritVisibleUntil / MeteorUntil) are read by the Unity
    /// layer to show the ghost fox and shooting stars.
    /// </summary>
    public class EasterEggState
    {
        public bool SpiritSeen;
        public bool MeteorSeen;
        /// <summary>Game seconds until which the spirit fox is visible. 0 = hidden.</summary>
        public double SpiritVisibleUntil;
        public float SpiritX;
        public float SpiritZ;
        /// <summary>Game seconds until which meteors fall. 0 = none.</summary>
        public double MeteorUntil;
        // Edge-detection watermarks (per-simulation memory, not saved).
        [NonSerialized] public int LastSpiritDay = -1;
        [NonSerialized] public int LastMeteorDay = -1;

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("spiritSeen", SpiritSeen);
            o.Add("meteorSeen", MeteorSeen);
            o.Add("spiritVisibleUntil", SpiritVisibleUntil);
            o.Add("spiritX", SpiritX);
            o.Add("spiritZ", SpiritZ);
            o.Add("meteorUntil", MeteorUntil);
            return o;
        }

        public static EasterEggState FromJson(JsonObject o)
        {
            var e = new EasterEggState();
            e.SpiritSeen = JsonHelpers.GetBool(o, "spiritSeen", false);
            e.MeteorSeen = JsonHelpers.GetBool(o, "meteorSeen", false);
            e.SpiritVisibleUntil = JsonHelpers.GetDouble(o, "spiritVisibleUntil", 0.0);
            e.SpiritX = JsonHelpers.GetFloat(o, "spiritX", 0f);
            e.SpiritZ = JsonHelpers.GetFloat(o, "spiritZ", 0f);
            e.MeteorUntil = JsonHelpers.GetDouble(o, "meteorUntil", 0.0);
            return e;
        }
    }

    public static class EasterEggSystem
    {
        private const double DaySeconds = 86400.0;
        private const double LunarCycleDays = 29.53;

        /// <summary>Moon phase 0..1. 0 = new moon, 0.5 = full moon.</summary>
        public static double MoonPhase(double elapsedSeconds)
        {
            double d = (elapsedSeconds / DaySeconds) / LunarCycleDays;
            return d - Math.Floor(d);
        }

        public static bool IsFullMoon(double elapsedSeconds)
        {
            double p = MoonPhase(elapsedSeconds);
            return p >= 0.465 && p <= 0.535; // ~2 days of fullness per cycle
        }

        /// <summary>Called from Simulation.FixedStep. Detection + journal only.</summary>
        public static void Tick(Simulation sim, float h)
        {
            var s = sim.State;
            var eggs = s.Eggs;
            var a = s.Agent;
            if (!a.IsAlive) return;
            double now = s.ElapsedSeconds;
            int day = (int)(now / DaySeconds);

            // -- ancient fox spirit: full-moon nights, fox awake and outdoors --
            bool fullMoonNight = IsFullMoon(now) && s.IsNight;
            if (fullMoonNight && eggs.LastSpiritDay != day)
            {
                eggs.LastSpiritDay = day;
                // Rare: ~12% per full-moon night, only if the fox is awake.
                bool awake = a.CurrentActivity != "sleeping" && a.Energy > 15f;
                if (awake && sim.EventRng.NextFloat() < 0.12f)
                    ShowSpirit(sim);
            }

            // -- meteor shower: clear nights, very rare --
            if (s.IsNight && eggs.LastMeteorDay != day)
            {
                eggs.LastMeteorDay = day;
                // ~4% per clear night. Clouds hide the sky.
                if (s.Weather == Weather.Clear && sim.EventRng.NextFloat() < 0.04f)
                    ShowMeteors(sim);
            }
        }

        private static void ShowSpirit(Simulation sim)
        {
            var s = sim.State;
            var eggs = s.Eggs;
            var a = s.Agent;
            // The spirit appears a little way off, watching.
            float ang = sim.EventRng.NextFloat(0f, (float)Math.PI * 2f);
            float dist = sim.EventRng.NextFloat(14f, 24f);
            eggs.SpiritX = a.X + (float)Math.Cos(ang) * dist;
            eggs.SpiritZ = a.Z + (float)Math.Sin(ang) * dist;
            // Visible for ~40 game-minutes, then gone.
            eggs.SpiritVisibleUntil = s.ElapsedSeconds + 40.0 * 60.0;
            bool first = !eggs.SpiritSeen;
            eggs.SpiritSeen = true;

            string line = first
                ? "The moon was full and high, and when I looked up from the trail there was a fox " +
                  "standing on the ridge that was not a fox — pale as moonlight on water, old as the " +
                  "hills themselves. It watched me a long while with eyes like stars. Then the wind " +
                  "moved, and it was gone, and I was not sure I had ever seen it. But my chest felt warm."
                : "The old one was on the ridge again tonight, pale in the moonlight. It watched me " +
                  "the way the hills watch the river — the way something watches what it loves. " +
                  "I bowed my head, and when I looked up it was gone.";
            sim.Journal(sim.Now, line, JournalCategory.Discovery, 0.95f);
            s.Beliefs.AddOrUpdate("spirit",
                "On full-moon nights, an ancient pale fox sometimes watches from the ridges.",
                "saw", 1f, sim.Now);
            a.Mood = MathX.Clamp(a.Mood + 8f, 0f, 100f);
        }

        private static void ShowMeteors(Simulation sim)
        {
            var s = sim.State;
            var eggs = s.Eggs;
            // The shower lasts ~25 game-minutes.
            eggs.MeteorUntil = s.ElapsedSeconds + 25.0 * 60.0;
            bool first = !eggs.MeteorSeen;
            eggs.MeteorSeen = true;

            string line = first
                ? "Tonight the sky fell in pieces and it was beautiful. Streaks of white fire tearing " +
                  "across the black, one after another, too many to count. I sat and watched until my " +
                  "neck ached. The old stories say the sky is a great beast shedding its winter coat. " +
                  "Tonight I believed them."
                : "The sky was falling again tonight — slow white fire, stitch after stitch across the " +
                  "dark. I stopped what I was doing and watched. Some things deserve your whole attention.";
            sim.Journal(sim.Now, line, JournalCategory.Discovery, 0.9f);
            s.Agent.Mood = MathX.Clamp(s.Agent.Mood + 6f, 0f, 100f);
        }
    }
}
