// Solace.Core — the seasonal cycle.
//
// Four seasons, each 20 game-days: Spring (rebirth), Summer (abundance),
// Autumn (change), Winter (scarcity). The season is a pure function of
// ElapsedSeconds, so it needs no save-schema changes and stays fully
// deterministic: same seed + same steps → same seasons, forever.
//
// Gameplay effects (applied by Tick, called from Simulation.FixedStep):
//   - Food: glowberry regrowth scales with FoodAbundance (summer feasts,
//     winter famine). Applies to both live hourly regrowth and offline catch-up.
//   - Energy: winter levies a small hourly cold tax on the protagonist's
//     light; spring grants a vitality bonus. The existing utility AI reacts
//     on its own (low energy → Rest wins), so behavior shifts without
//     touching the brain.
//   - Kits: newborns in spring receive nature's timing — a one-time
//     energy/health blessing, tracked per simulation so it fires once.
//   - Nights: winter nights are long (13h), summer nights short (7h) —
//     the lantern-fox's glow matters most when the dark is deepest.
//   - Journal: every season turn is chronicled.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3,
    }

    /// <summary>
    /// The turning year. All methods are pure functions of GameState except
    /// Tick, which carries the per-simulation memory (last season, hourly
    /// watermark, blessed kit ids) passed in by the Simulation.
    /// </summary>
    public static class SeasonSystem
    {
        public const float SeasonDays = 20f;
        private const float DaySeconds = 86400f;

        /// <summary>Which season it is. Season 0 (Spring) starts at t=0.</summary>
        public static Season Current(GameState s)
        {
            double days = s.ElapsedSeconds / DaySeconds;
            int idx = (int)Math.Floor(days / SeasonDays) % 4;
            if (idx < 0) idx += 4;
            return (Season)idx;
        }

        /// <summary>Whole seasons elapsed since t=0 (for "year N" flavor).</summary>
        public static int SeasonIndex(GameState s)
        {
            return (int)Math.Floor(s.ElapsedSeconds / DaySeconds / SeasonDays);
        }

        /// <summary>Night bounds for a season: dusk hour (night starts) and dawn hour (night ends).</summary>
        public static void NightBounds(Season season, out float duskHour, out float dawnHour)
        {
            switch (season)
            {
                case Season.Spring: duskHour = 20f; dawnHour = 6f; break;
                case Season.Summer: duskHour = 21.5f; dawnHour = 4.5f; break;
                case Season.Autumn: duskHour = 20f; dawnHour = 6f; break;
                default: duskHour = 17.5f; dawnHour = 6.5f; break; // winter: long dark
            }
        }

        public static bool IsNightAt(Season season, float hourOfDay)
        {
            float dusk, dawn;
            NightBounds(season, out dusk, out dawn);
            return hourOfDay >= dusk || hourOfDay < dawn;
        }

        /// <summary>Berry regrowth multiplier. Summer feasts; winter starves.</summary>
        public static float FoodAbundance(Season season)
        {
            switch (season)
            {
                case Season.Spring: return 1.25f;
                case Season.Summer: return 1.6f;
                case Season.Autumn: return 1.0f;
                default: return 0.35f;
            }
        }

        /// <summary>Hourly energy drift for the protagonist: winter cold tax, spring vitality.</summary>
        public static float HourlyEnergyDelta(Season season)
        {
            switch (season)
            {
                case Season.Spring: return 1.2f;
                case Season.Summer: return 0f;
                case Season.Autumn: return -0.4f;
                default: return -2.0f;
            }
        }

        public static string DisplayName(Season season)
        {
            switch (season)
            {
                case Season.Spring: return "Spring";
                case Season.Summer: return "Summer";
                case Season.Autumn: return "Autumn";
                default: return "Winter";
            }
        }

        /// <summary>Journal text for the turning of the year. Written in the fox's voice.</summary>
        public static string ChangeText(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    return "The thaw has come. Green pushes up through the last snow, and the vale " +
                           "smells of rain and new things. Kits born now will know only plenty. Spring is here.";
                case Season.Summer:
                    return "Summer has settled over the vale like a warm hand. The glowberries hang heavy, " +
                           "the nights are short, and there is food in every shadow. I could run forever.";
                case Season.Autumn:
                    return "The leaves are turning. Amber and rust creep through the Foxpine, and the air " +
                           "tastes of endings. Time to eat well and grow heavy — winter is counting the days.";
                default:
                    return "Winter has come to the valley. Snow hushes the pines and the nights are long " +
                           "and hungry. My light is all the warmer for the dark around it.";
            }
        }

        /// <summary>
        /// Per-fixed-step season maintenance. Call from Simulation.FixedStep.
        /// lastSeason / lastHourMark / blessedKits are owned by the Simulation.
        /// </summary>
        public static void Tick(Simulation sim, ref Season lastSeason, ref long lastHourMark,
                                HashSet<int> blessedKits)
        {
            var s = sim.State;
            Season now = Current(s);
            if (now != lastSeason)
            {
                lastSeason = now;
                // The fox's voice speaks the turning of the year, shaped by temperament.
                string seasonText = FoxVoice.SeasonChangeText(sim.EventRng, s.Agent.Traits, now);
                sim.Journal(sim.Now, seasonText, JournalCategory.Weather, 0.7f);
                // First snow of winter gets its own moment.
                if (now == Season.Winter)
                    FoxVoice.OnFirstSnow(sim);
            }

            long hourMark = (long)Math.Floor(s.ElapsedSeconds / 3600f);
            if (hourMark != lastHourMark)
            {
                lastHourMark = hourMark;
                HourlyTick(sim, now, blessedKits);
            }
        }

        private static void HourlyTick(Simulation sim, Season season, HashSet<int> blessedKits)
        {
            var s = sim.State;

            // Live berry regrowth, scaled by season. Gentle: the offline rate
            // is ~1.5 berries/bush/day; live summer matches it, winter starves.
            // Challenge hook: Hardcore runs regrow scarcer food.
            float pRegrow = 0.04f * FoodAbundance(season) * ChallengeModes.FoodScarcity(ChallengeModes.ActiveMode);
            foreach (var poi in s.World.Pois)
            {
                if (poi.Type != PoiType.GlowberryBush || poi.Stock >= 8) continue;
                if (sim.EventRng.NextFloat() < pRegrow) poi.Stock++;
            }

            // Seasonal pressure on the protagonist's light. The utility AI
            // feels this and adjusts on its own (rest more in winter, roam
            // in spring) — no brain changes needed.
            var a = s.Agent;
            if (a.IsAlive)
            {
                float d = HourlyEnergyDelta(season);
                if (d != 0f) a.Energy = MathX.Clamp(a.Energy + d, 0f, a.MaxEnergy);
            }

            // Nature's timing: kits born in spring get a one-time blessing.
            // (Age is in game-years; 0.05 ≈ 18 days — catches true newborns.)
            if (season == Season.Spring && blessedKits != null)
            {
                foreach (var kit in s.Kits)
                {
                    if (!kit.IsAlive || kit.Age >= 0.05f || blessedKits.Contains(kit.Id)) continue;
                    blessedKits.Add(kit.Id);
                    kit.Energy = MathX.Clamp(kit.Energy + 15f, 0f, 100f);
                    kit.Health = MathX.Clamp(kit.Health + 10f, 0f, 100f);
                }
            }
        }
    }
}
