// Solace.Core — save/load and time-away continuity.
//
// Saves are plain JSON written by the hand-rolled serializer in Json.cs —
// portable, inspectable, recoverable. ApplyOfflineProgress implements the
// three continuity modes from the design: Stillness, QuietLife, LivingWorld.
// Absence creates history, never guilt: budgets bound drama, and the agent
// cannot die while away without a logged severe cause.
using System;

namespace Solace.Core
{
    public static class SaveSystem
    {
        /// <summary>Serializes the full game state to a JSON string.</summary>
        public static string Save(GameState s)
        {
            if (s == null) throw new ArgumentNullException("s");
            return s.ToJson().ToJson();
        }

        /// <summary>Deserializes a save. Throws JsonParseException on bad data.</summary>
        public static GameState Load(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new JsonParseException("Empty save data.");
            JsonValue v = JsonValue.Parse(json);
            return GameState.FromJson(v.AsObject());
        }

        /// <summary>Marks the agent dead with a journaled cause. Used by both live and away sim.</summary>
        public static void KillAgent(GameState s, string cause)
        {
            var a = s.Agent;
            if (!a.IsAlive) return;
            a.IsAlive = false;
            a.Health = 0f;
            a.HasMoveTarget = false;
            a.InCombat = false;
            s.Journal.Add(s.ElapsedSeconds, cause, JournalCategory.System, 1.0f);
        }

        /// <summary>
        /// Advances the world for time spent away. Real away time is capped at
        /// 7 days of effect. Deterministic: consumes the event stream and writes
        /// its position back.
        /// </summary>
        public static void ApplyOfflineProgress(GameState s, TimeSpan away, OfflineMode mode)
        {
            if (s == null || !s.Agent.IsAlive) return;
            double awaySeconds = away.TotalSeconds;
            if (awaySeconds <= 0) return;
            double capped = Math.Min(awaySeconds, 7.0 * 86400.0); // 7-day cap

            if (mode == OfflineMode.Stillness)
                return; // nothing changes — the glen waits

            var ev = new SeededRandom(s.Rng.Event);
            try
            {
                if (mode == OfflineMode.QuietLife)
                    QuietLife(s, capped, ev);
                else
                    LivingWorld(s, capped, ev);
            }
            finally
            {
                s.Rng.Event = ev.State;
            }
        }

        // -- QuietLife ---------------------------------------------------------

        private static void QuietLife(GameState s, double seconds, SeededRandom ev)
        {
            var a = s.Agent;
            double hours = seconds / 3600.0;
            s.ElapsedSeconds += (float)seconds;

            // Mild needs drift; routine, low risk. Bounds keep the agent safe.
            a.Hunger = Math.Min(78f, a.Hunger + (float)hours * 3f);
            a.Thirst = Math.Min(78f, a.Thirst + (float)hours * 3.5f);
            a.Energy = Math.Min(100f, a.Energy + (float)hours * 10f);
            a.Mood += (58f - a.Mood) * 0.5f;
            if (a.Hunger < 70f && a.Thirst < 70f)
                a.Health = Math.Min(100f, a.Health + (float)hours * 1.5f);
            a.Health = Math.Max(a.Health, 25f); // quiet life never harms
            a.Curiosity = Math.Min(100f, a.Curiosity + (float)hours * 2f);

            s.Social.Decay(s.ElapsedSeconds, (float)seconds);
            RegrowBushes(s, seconds);

            // At most one minor episode for the whole absence.
            if (seconds > 3600.0)
            {
                string place = NearestKnownPlaceName(s);
                string[] templates =
                {
                    "The days passed quietly. I kept to my rounds near " + place + ", and the glen kept me.",
                    "A quiet stretch. I mended my pack, watched the weather turn, and thought of little.",
                    "Nothing much happened, and that was fine. The fire stayed lit; I stayed fed."
                };
                s.Journal.Add(s.ElapsedSeconds, templates[ev.NextInt(templates.Length)],
                    JournalCategory.Reflection, 0.35f);
            }

            DriftWeather(s, seconds, ev);
        }

        // -- LivingWorld -------------------------------------------------------

        private static void LivingWorld(GameState s, double seconds, SeededRandom ev)
        {
            var a = s.Agent;
            double simSeconds = Math.Min(seconds, 4.0 * 3600.0); // bounded: max 4 in-game hours simulated
            double quietSeconds = seconds - simSeconds;

            bool severeAtClose = a.InCombat || a.Health < 25f;
            int meaningfulEncounters = 0;
            const int maxMeaningful = 2;

            // Simulate in 5-minute abstract ticks.
            double t = 0;
            while (t < simSeconds)
            {
                double dt = Math.Min(300.0, simSeconds - t);
                t += dt;
                s.ElapsedSeconds += (float)dt;
                float h = (float)dt;

                // Needs drift at near-live rates.
                a.Energy = MathX.Clamp(a.Energy - h * 0.06f, 0f, 100f);
                a.Hunger = MathX.Clamp(a.Hunger + h * 0.045f, 0f, 100f);
                a.Thirst = MathX.Clamp(a.Thirst + h * 0.06f, 0f, 100f);

                // Abstract self-care: during routine time Solace looks after
                // itself (forages, drinks, rests) — this is routine, not drama.
                if (a.Hunger > 72f)
                {
                    a.Hunger = MathX.Clamp(a.Hunger - 35f, 0f, 100f);
                    if (ev.NextFloat() < 0.25f)
                        s.Journal.Add(s.ElapsedSeconds, "I found enough to eat along the way.",
                            JournalCategory.Survival, 0.2f);
                }
                if (a.Thirst > 72f) a.Thirst = 25f;
                if (a.Energy < 18f) a.Energy = Math.Min(100f, a.Energy + 45f);

                if (a.Hunger > 95f) a.Health -= h * 0.2f;
                if (a.Thirst > 95f) a.Health -= h * 0.25f;
                a.Mood += (50f - a.Mood) * h * 0.005f;

                // Abstract travel: drift toward the current move target.
                if (a.HasMoveTarget)
                {
                    float dx = a.TargetX - a.X, dz = a.TargetZ - a.Z;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    float step = 2.0f * h;
                    if (dist <= Math.Max(step, a.ArriveRadius))
                    {
                        a.X = a.TargetX; a.Z = a.TargetZ;
                        a.HasMoveTarget = false;
                        var poi = s.World.GetPoi(a.TargetPoiId);
                        if (poi != null && !poi.Discovered && meaningfulEncounters < maxMeaningful)
                        {
                            meaningfulEncounters++;
                            DiscoverQuietly(s, poi);
                        }
                    }
                    else
                    {
                        a.X += dx / dist * step;
                        a.Z += dz / dist * step;
                    }
                }

                // Encounter roll: bounded novelty and danger.
                if (ev.NextFloat() < 0.055f)
                {
                    float roll = ev.NextFloat();
                    if (roll < 0.28f && meaningfulEncounters < maxMeaningful)
                    {
                        // Wolf encounter — the danger budget.
                        meaningfulEncounters++;
                        float dmg = ev.NextFloat(5f, 15f);
                        a.Health = MathX.Clamp(a.Health - dmg, 0f, 100f);
                        a.Traits.Nudge("Caution", 0.02f);
                        s.Journal.Add(s.ElapsedSeconds,
                            "A wolf shadowed me on the fell while you were gone. I got away, but it cost me.",
                            JournalCategory.Combat, 0.8f);
                    }
                    else if (roll < 0.50f)
                    {
                        s.Journal.Add(s.ElapsedSeconds,
                            "I stood a while and watched deer move through the mist. It steadied me.",
                            JournalCategory.Travel, 0.3f);
                        a.Mood = MathX.Clamp(a.Mood + 4f, 0f, 100f);
                    }
                    else if (roll < 0.70f)
                    {
                        a.Hunger = MathX.Clamp(a.Hunger - 20f, 0f, 100f);
                        s.Journal.Add(s.ElapsedSeconds,
                            "I found berries along the way and ate my fill.",
                            JournalCategory.Survival, 0.3f);
                    }
                    else if (roll < 0.85f)
                    {
                        s.Journal.Add(s.ElapsedSeconds,
                            "The weather turned while I walked; I pulled my cloak close and kept going.",
                            JournalCategory.Weather, 0.3f);
                    }
                    else
                    {
                        a.Mood = MathX.Clamp(a.Mood + 5f, 0f, 100f);
                        s.Journal.Add(s.ElapsedSeconds,
                            "For a little while the light on the loch was so beautiful I just stopped.",
                            JournalCategory.Reflection, 0.4f);
                    }
                }

                DriftWeather(s, dt, ev);

                if (a.Health <= 0f)
                {
                    if (severeAtClose)
                    {
                        KillAgent(s, "While you were gone, the wild took me. I was already hurting when you left, and I couldn't outrun it.");
                        return;
                    }
                    // Absence is never a death sentence without a logged cause.
                    a.Health = 5f;
                    s.Journal.Add(s.ElapsedSeconds,
                        "I pushed too far and nearly didn't come back. I found shelter in time — barely.",
                        JournalCategory.Survival, 0.85f);
                }
            }

            // The remaining away time passes as quiet routine.
            if (quietSeconds > 0)
            {
                double hours = quietSeconds / 3600.0;
                s.ElapsedSeconds += (float)quietSeconds;
                a.Hunger = Math.Min(80f, a.Hunger + (float)hours * 3f);
                a.Thirst = Math.Min(80f, a.Thirst + (float)hours * 3.5f);
                a.Energy = Math.Min(100f, a.Energy + (float)hours * 8f);
                if (a.Hunger < 75f && a.Thirst < 75f)
                    a.Health = Math.Min(100f, a.Health + (float)hours * 1f);
                if (a.Health <= 0f) a.Health = 5f;
                s.Social.Decay(s.ElapsedSeconds, (float)quietSeconds);
                RegrowBushes(s, seconds);
            }

            // Clamp: no death without a severe logged cause.
            if (a.IsAlive && a.Health < 5f && !severeAtClose)
                a.Health = 5f;
        }

        private static void DiscoverQuietly(GameState s, PointOfInterest poi)
        {
            poi.Discovered = true;
            s.Agent.KnownPoiIds.Add(poi.Id);
            s.Journal.Add(s.ElapsedSeconds,
                "I reached " + poi.DisplayName + " while you were away. " + DiscoveryLine(poi) + ".",
                JournalCategory.Discovery, 0.7f, 1f, poi.Id);
        }

        private static string DiscoveryLine(PointOfInterest poi)
        {
            switch (poi.Type)
            {
                case PoiType.BrochRuin: return "No one living built that tower";
                case PoiType.RuinSite: return "Old stones, older stories";
                case PoiType.Overlook: return "The whole glen lay below me";
                case PoiType.Cairn: return "Travellers have been adding stones for years";
                case PoiType.Campfire: return "A good sheltered spot for a fire";
                default: return "Worth remembering";
            }
        }

        private static void RegrowBushes(GameState s, double seconds)
        {
            double days = seconds / 86400.0;
            int regrow = (int)(days * 1.5);
            if (regrow <= 0) return;
            foreach (var poi in s.World.Pois)
                if (poi.Type == PoiType.BerryBush)
                    poi.Stock = Math.Min(8, poi.Stock + regrow);
        }

        private static string NearestKnownPlaceName(GameState s)
        {
            var a = s.Agent;
            PointOfInterest best = null;
            float bestD = float.MaxValue;
            foreach (var id in a.KnownPoiIds)
            {
                var p = s.World.GetPoi(id);
                if (p == null) continue;
                float d = V2.Distance(a.Pos, new V2(p.X, p.Z));
                if (d < bestD) { bestD = d; best = p; }
            }
            return best != null ? best.DisplayName : "the hamlet";
        }

        /// <summary>Slow weather drift shared by quiet and living catch-up.</summary>
        private static void DriftWeather(GameState s, double dtSeconds, SeededRandom ev)
        {
            // Roll at most once per call per ~45 game-minutes of drift.
            double span = dtSeconds;
            while (span > 0)
            {
                double step = Math.Min(span, 2700.0);
                span -= step;
                s.WeatherChangedAt += (float)step;
                if (ev.NextFloat() < step / 2700.0 * 0.35f)
                {
                    Weather next = RollWeather(s.Weather, ev);
                    if (next != s.Weather)
                    {
                        s.Weather = next;
                        if (next == Weather.Rain)
                            s.Journal.Add(s.ElapsedSeconds, "Rain came while you were gone, drumming on the heather.",
                                JournalCategory.Weather, 0.25f);
                        else if (next == Weather.Storm)
                            s.Journal.Add(s.ElapsedSeconds, "A storm rolled through the glen in your absence.",
                                JournalCategory.Weather, 0.4f);
                    }
                }
            }
        }

        internal static Weather RollWeather(Weather current, SeededRandom ev)
        {
            float r = ev.NextFloat();
            switch (current)
            {
                case Weather.Clear:
                    return r < 0.65f ? Weather.Clear : r < 0.90f ? Weather.Cloudy : Weather.Rain;
                case Weather.Cloudy:
                    return r < 0.25f ? Weather.Clear : r < 0.70f ? Weather.Cloudy : r < 0.95f ? Weather.Rain : Weather.Storm;
                case Weather.Rain:
                    return r < 0.05f ? Weather.Clear : r < 0.45f ? Weather.Cloudy : r < 0.85f ? Weather.Rain : Weather.Storm;
                case Weather.Storm:
                    return r < 0.50f ? Weather.Rain : r < 0.80f ? Weather.Cloudy : Weather.Storm;
                default:
                    return Weather.Clear;
            }
        }
    }
}
