// Solace.Core — the fox's inner voice.
//
// FoxVoice is where the protagonist becomes a character. Every reactive
// thought, every reflection, every whispered observation lives here —
// written in first person, in the fox's voice, shaped by personality.
//
// The writing bar: "The Little Prince" meets nature documentary. Literary,
// not gamey. The fox thinks in images and feelings, not in systems.
//
// Determinism: all text selection uses sim.EventRng, so the same seed +
// same steps produce the same thoughts. Cooldowns live on the Simulation
// (per-sim, not saved — like season memory). Thoughts are flavor; losing
// them on load costs nothing.
using System;

namespace Solace.Core
{
    /// <summary>
    /// The fox's inner voice: reactive thoughts, reflections, and observations.
    /// All methods are safe to call every frame — internal cooldowns prevent spam.
    /// </summary>
    public static class FoxVoice
    {
        // -- personality-voiced selection --------------------------------------

        /// <summary>
        /// Picks a line voiced by personality. If boldness dominates, uses the
        /// bold set; if timidity dominates, the timid set; otherwise neutral.
        /// Falls back gracefully when a set is empty.
        /// </summary>
        private static string Voiced(SeededRandom rng, Personality t,
                                     string[] bold, string[] timid, string[] neutral)
        {
            float boldDev = t.Boldness - 0.5f;
            float timidDev = t.Caution - 0.5f;
            string[] set = neutral;
            if (boldDev > 0.18f && bold != null && bold.Length > 0) set = bold;
            else if (timidDev > 0.18f && timid != null && timid.Length > 0) set = timid;
            if (set == null || set.Length == 0) set = neutral;
            if (set == null || set.Length == 0) return "";
            return rng.Pick(set);
        }

        /// <summary>Curiosity-voiced variant: the curious notice details others miss.</summary>
        private static string CuriousVoiced(SeededRandom rng, Personality t,
                                            string[] curious, string[] neutral)
        {
            string[] set = (t.Curiosity > 0.68f && curious != null && curious.Length > 0)
                ? curious : neutral;
            if (set == null || set.Length == 0) return "";
            return rng.Pick(set);
        }

        // -- sunset ------------------------------------------------------------

        private static readonly string[] SunsetBold = new[]
        {
            "The sun is going down, and the whole vale holds its breath. Another day survived. Tomorrow, further.",
            "Dusk. The light thins and the world goes quiet. I have seen worse days than this one, and I am still here.",
            "The sky is burning at the edges. Let it. I am not afraid of the dark — I carry my own light.",
        };
        private static readonly string[] SunsetTimid = new[]
        {
            "The sun is going down. The shadows are getting long, and I do not like how they move. Time to find shelter.",
            "Dusk again. The dark is coming, and the dark has teeth. I will stay close to my light tonight.",
            "The light is leaving the sky. I count my breaths until it returns. It always returns. It always returns.",
        };
        private static readonly string[] SunsetNeutral = new[]
        {
            "The sun is sinking, and the vale turns gold, then violet, then blue. I sit and watch it go, the way I always do.",
            "Dusk settles over the Foxpine like a hand over my eyes — gentle, certain. The day is done. I did enough.",
            "The last light catches the river and sets it burning. For a moment the whole world is the color of my chest-light.",
            "Evening. The birds go quiet one by one, like candles being blown out. I am glad for the quiet.",
        };
        private static readonly string[] SunsetCurious = new[]
        {
            "The sun is going down, and I wonder — where does it go? Does it sleep somewhere, the way I do? Does it dream?",
            "Dusk. The sky does this every day and I still do not understand it. The colors have no reason, and that is why I love them.",
        };

        public static void OnSunset(Simulation sim, SeededRandom rng)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = Voiced(rng, a.Traits, SunsetBold, SunsetTimid, SunsetNeutral);
            // The curious sometimes wonder instead.
            if (rng.NextFloat() < 0.3f)
                text = CuriousVoiced(rng, a.Traits, SunsetCurious, new[] { text });
            if (string.IsNullOrEmpty(text)) return;
            sim.Journal(sim.Now, text, JournalCategory.Reflection, 0.45f);
        }

        // -- sunrise -----------------------------------------------------------

        private static readonly string[] SunriseBold = new[]
        {
            "Dawn. The world remakes itself, and so do I. Today I will go further than yesterday.",
            "Morning. Light spills over the ridge like water. I am awake, I am hungry, I am ready. Let the day try me.",
        };
        private static readonly string[] SunriseTimid = new[]
        {
            "Morning came. I made it through another night. The light is back, and so am I — smaller, but here.",
            "Dawn. I check the treeline before I move. Old habit. The sun does not judge me for being careful.",
        };
        private static readonly string[] SunriseNeutral = new[]
        {
            "Dawn. The mist lifts off the river in slow ribbons, and the world smells new. Every morning is a small forgiveness.",
            "Morning. My light dims a little in the daylight, the way stars do — not gone, just waiting its turn.",
            "The sun comes up and the dew catches fire on every blade of grass. I walk through a field of tiny suns.",
        };

        public static void OnSunrise(Simulation sim, SeededRandom rng)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = Voiced(rng, a.Traits, SunriseBold, SunriseTimid, SunriseNeutral);
            if (string.IsNullOrEmpty(text)) return;
            sim.Journal(sim.Now, text, JournalCategory.Reflection, 0.40f);
        }

        // -- eating ------------------------------------------------------------

        private static readonly string[] AteStarving = new[]
        {
            "I ate until the shaking stopped. Food is the only prayer I know, and today it was answered.",
            "The hunger was a hollow place inside me, and now it is full. I had forgotten how good full feels.",
            "I found food when I needed it most. The vale provides. I will remember this kindness.",
        };
        private static readonly string[] AteGrateful = new[]
        {
            "Glowberries, sweet and warm from the sun. I eat slowly, the way my mother taught me — taste everything.",
            "I sat among the bushes and ate my fill. The juice stains my paws gold. Small joys are still joys.",
            "A good meal. My light burns a little brighter for it. The body knows what the mind forgets: we are kept.",
        };
        private static readonly string[] AteCurious = new[]
        {
            "These berries glow from the inside. I wonder if they are little suns, or if they are eating light the way I do.",
        };

        /// <summary>Called when the fox eats. Hunger-before shapes the gratitude.</summary>
        public static void OnAte(Simulation sim, float hungerBefore)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text;
            if (hungerBefore > 70f)
                text = sim.EventRng.Pick(AteStarving);
            else if (sim.EventRng.NextFloat() < 0.25f)
                text = CuriousVoiced(sim.EventRng, a.Traits, AteCurious, AteGrateful);
            else
                text = sim.EventRng.Pick(AteGrateful);
            sim.Journal(sim.Now, text, JournalCategory.Survival, 0.30f);
        }

        // -- fear and relief -----------------------------------------------------

        private static readonly string[] FledBold = new[]
        {
            "I ran, and I hate that I ran. But I am alive, and being alive is the first revenge.",
            "The gloom-maw wanted me and did not have me. I am faster than fear. Remember that.",
        };
        private static readonly string[] FledTimid = new[]
        {
            "I ran until my legs gave out and my chest-light flickered. I am safe. I am safe. I keep saying it until I believe it.",
            "It almost had me. I can still feel its breath on my tail. I will not go that way again for a long, long time.",
        };
        private static readonly string[] FledNeutral = new[]
        {
            "I lost the gloom-maw in the heather. My heart is still hammering against my ribs like it wants out.",
            "Safe. For now. The fear drains out of me slowly, like water from a cracked cup.",
            "I ran and I am still running in my mind. It takes a while for the body to believe the danger is gone.",
        };

        /// <summary>Called when the fox reaches safety after fleeing.</summary>
        public static void OnFledToSafety(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = Voiced(sim.EventRng, a.Traits, FledBold, FledTimid, FledNeutral);
            if (string.IsNullOrEmpty(text)) return;
            sim.Journal(sim.Now, text, JournalCategory.Combat, 0.50f);
        }

        // -- standing ground -----------------------------------------------------

        private static readonly string[] StoodGround = new[]
        {
            "It came for me and I did not move. My light burned so bright it hurt to look at. It left. I am still shaking, but I did not move.",
            "I stood my ground and the dark backed down. There is a kind of courage that is just refusing to run. Today I had it.",
        };

        public static void OnStoodGround(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(StoodGround), JournalCategory.Combat, 0.65f);
        }

        // -- kits ------------------------------------------------------------------

        private static readonly string[] KitsPlaying = new[]
        {
            "The kits are tumbling over each other in the grass, all paws and squeaks. I watch from the shade and my chest feels too full.",
            "They chase their own tails and fall over and get up laughing. I remember being that small. It was not so long ago.",
            "One of the kits pounced on a butterfly and missed by a mile. They are terrible hunters and perfect children.",
            "I watch them play and something in me goes quiet and warm. This is why I endure the lean days. For this.",
        };
        private static readonly string[] KitsPlayingProud = new[]
        {
            "The kits are wrestling in the sun, and the boldest one is winning. That is my child. I did that.",
        };
        private static readonly string[] KitsSleeping = new[]
        {
            "The kits are asleep in a pile, breathing together. I curl around them and my light dims to a ember-glow. Guard the small ones.",
            "They sleep the deep sleep of the young, twitching with dreams. I wonder what kits dream about. Probably butterflies.",
        };

        /// <summary>Called when kits are playing nearby and the fox is watching.</summary>
        public static void OnKitsPlaying(Simulation sim, int kitCount, SeededRandom rng)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text;
            if (a.Traits.Pride > 0.68f && rng.NextFloat() < 0.4f)
                text = rng.Pick(KitsPlayingProud);
            else
                text = rng.Pick(KitsPlaying);
            sim.Journal(sim.Now, text, JournalCategory.Social, 0.55f);
        }

        public static void OnKitsSleeping(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(KitsSleeping), JournalCategory.Social, 0.45f);
        }

        // -- loneliness ------------------------------------------------------------

        private static readonly string[] LonelySociable = new[]
        {
            "I have not seen another of my kind in days. The vale is beautiful, but beauty is meant to be shared. I miss voices.",
            "I called out at dusk and only the mist answered. It is a poor conversation, mist. It never asks how I am.",
            "Solitude is a heavy pelt to wear. I would trade a week of glowberries for one evening of company.",
        };
        private static readonly string[] LonelyNeutral = new[]
        {
            "It has been quiet. Too quiet. I find myself talking to the river just to hear something answer.",
            "Days since I saw another fox. I am managing. But I check the treeline more often than I need to.",
        };

        /// <summary>Called when the fox has been alone for a long stretch.</summary>
        public static void OnLonely(Simulation sim, SeededRandom rng)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = a.Traits.Sociability > 0.6f
                ? rng.Pick(LonelySociable)
                : rng.Pick(LonelyNeutral);
            sim.Journal(sim.Now, text, JournalCategory.Reflection, 0.50f);
        }

        // -- seasonal awareness ------------------------------------------------------

        /// <summary>Personality-voiced seasonal change text. Replaces the neutral ChangeText.</summary>
        public static string SeasonChangeText(SeededRandom rng, Personality t, Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    if (t.Boldness > 0.68f)
                        return "Spring. The thaw, the green, the whole vale waking up hungry. I was born for this season — everything is beginning, and so am I.";
                    if (t.Caution > 0.68f)
                        return "Spring is here. New growth, new scents, new dangers I have not mapped yet. I will explore carefully. The vale is beautiful and I do not trust it.";
                    return "The thaw has come. Green pushes up through the last snow, and the vale smells of rain and new things. Kits born now will know only plenty. Spring is here.";
                case Season.Summer:
                    if (t.Curiosity > 0.68f)
                        return "Summer. Long days, and every one of them is a question. What is over that ridge? What lives in that hollow? I intend to find out.";
                    return "Summer has settled over the vale like a warm hand. The glowberries hang heavy, the nights are short, and there is food in every shadow. I could run forever.";
                case Season.Autumn:
                    if (t.Patience > 0.68f)
                        return "Autumn. The leaves turn and fall, and I am in no hurry. Every season is a teacher. This one teaches letting go.";
                    return "The leaves are turning. Amber and rust creep through the Foxpine, and the air tastes of endings. Time to eat well and grow heavy — winter is counting the days.";
                default: // Winter
                    if (t.Boldness > 0.68f)
                        return "Winter. Snow, hunger, long dark nights. Good. Let the vale try its worst — my light has never burned brighter than when the dark is deepest.";
                    if (t.Caution > 0.68f)
                        return "Winter has come. I have cached what I could and mapped the sheltered hollows. The cold is patient, but so am I. We will wait it out together, my light and I.";
                    return "Winter has come to the valley. Snow hushes the pines and the nights are long and hungry. My light is all the warmer for the dark around it.";
            }
        }

        private static readonly string[] FirstSnow = new[]
        {
            "The first snow. It falls without sound, the way important things happen. The vale is holding its breath.",
            "Snow. I catch a flake on my tongue and it tastes like nothing and everything. Winter is really here.",
            "The first snow covers my tracks as fast as I make them. The vale is wiping the slate clean.",
        };

        public static void OnFirstSnow(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(FirstSnow), JournalCategory.Weather, 0.60f);
        }

        // -- milestones --------------------------------------------------------------

        private static readonly string[] BirthdayReflections = new[]
        {
            "Another year. My paws are a little slower and my stories are a little longer. I would not trade.",
            "I have lived another year in this vale. The river is still singing. I am still listening. That is enough.",
            "A year older. The kits I once was would not recognize me — and would be proud anyway.",
        };
        private static readonly string[] BirthdayOld = new[]
        {
            "Another year, and my light is not what it was. But it is still mine, and it still burns. I have learned that is enough.",
            "I am old now. I feel it in my bones when the rain comes. But I have seen a hundred dawns, and each one was a gift.",
        };

        /// <summary>Called on the fox's birthday (each game-year of age).</summary>
        public static void OnBirthday(Simulation sim, int ageYears, SeededRandom rng)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            // Foxes live ~10-14 years; 8+ is elderly.
            string text = ageYears >= 8
                ? rng.Pick(BirthdayOld)
                : rng.Pick(BirthdayReflections);
            sim.Journal(sim.Now, text, JournalCategory.Reflection, 0.70f);
        }

        private static readonly string[] FirstDiscovery = new[]
        {
            "I found a place today that I have never seen before. The vale is bigger than I knew. I wonder what else I have missed.",
        };

        /// <summary>Called on the very first POI discovery of a life.</summary>
        public static void OnFirstDiscovery(Simulation sim, string placeName)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = "I found " + placeName + " today — the first new place of my life. " +
                          "The vale is bigger than I knew. I wonder what else I have missed.";
            sim.Journal(sim.Now, text, JournalCategory.Discovery, 0.65f);
        }

        // -- night ---------------------------------------------------------------------

        private static readonly string[] NightPeaceful = new[]
        {
            "Night. The stars are out, and my chest-light answers them. We are all just lights in the dark, keeping each other company.",
            "The night is deep and my glow is the brightest thing for miles. I do not feel small. I feel like a lantern someone left on for the world.",
            "Owls call from the Foxpine. The river glows faintly below. Night is when the vale tells the truth.",
        };
        private static readonly string[] NightLonely = new[]
        {
            "Night again. The dark presses close and my light feels very small. I am glad it is mine.",
        };

        /// <summary>Called occasionally during peaceful nights.</summary>
        public static void OnNightThought(Simulation sim, SeededRandom rng)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = a.Traits.Sociability > 0.65f && rng.NextFloat() < 0.3f
                ? rng.Pick(NightLonely)
                : rng.Pick(NightPeaceful);
            sim.Journal(sim.Now, text, JournalCategory.Reflection, 0.40f);
        }

        // -- weather (shelter arrival + rain joy; sense/shelter-call live below) ---------

        private static readonly string[] StormSeekBold = new[]
        {
            "The sky is angry. Fine. I know where the dry places are, and I am faster than the rain.",
            "Storm coming. I have weathered worse. Still — no shame in a roof when the sky starts throwing stones.",
        };
        private static readonly string[] StormSeekTimid = new[]
        {
            "The sky is angry. I need to get home. I need to get home right now.",
            "The air tastes like lightning. Every hair on my pelt is standing up. Shelter. Now. Please.",
            "I can hear the storm chewing the ridge. I am too small for this sky. I need walls.",
        };
        private static readonly string[] StormSeekNeutral = new[]
        {
            "The sky is angry. I need to get home.",
            "The clouds are stacking up black over the tops, and the wind has teeth. Time to be somewhere with a roof.",
            "Thunder, far off but coming closer. The wise fox is the dry fox. I am going to be very wise.",
        };

        /// <summary>Called when the fox decides to run for shelter from a storm.</summary>
        public static void OnSeekShelter(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            string text = Voiced(sim.EventRng, a.Traits, StormSeekBold, StormSeekTimid, StormSeekNeutral);
            if (string.IsNullOrEmpty(text)) return;
            sim.Journal(sim.Now, text, JournalCategory.Weather, 0.65f);
        }

        private static readonly string[] StormSheltered = new[]
        {
            "I made it under cover as the sky broke open. The rain hammers down outside and I am dry. Small victories.",
            "Sheltered. The storm rages and I am curled in the dry dark, listening to it try and fail to reach me.",
            "The wind howls through the cracks but it cannot have me. I tuck my nose under my tail and wait it out.",
        };

        /// <summary>Called when the fox reaches shelter while a storm rages.</summary>
        public static void OnStormSheltered(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            // Don't spam: only if we haven't said it recently (stateless via derived RNG gate).
            var rng = SeededRandom.Derive(sim.State.Seed, "voice:sheltered:" + (long)(sim.State.ElapsedSeconds / 3600.0));
            if (rng.NextFloat() < 0.5f) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(StormSheltered), JournalCategory.Weather, 0.45f);
        }

        private static readonly string[] StormSenseLines = new[]
        {
            "I smell rain on the wind — the heavy kind. Best to eat while I can.",
            "The air has gone still and sweet. A storm is gathering somewhere beyond the ridge. I should fill my belly first.",
            "My nose tells me the sky is planning something. I have learned to trust my nose. Time to eat.",
        };

        /// <summary>Called when the fox senses a storm coming while it is merely cloudy.</summary>
        public static void OnStormSense(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(StormSenseLines), JournalCategory.Weather, 0.55f);
        }

        private static readonly string[] FalseAlarmLines = new[]
        {
            "The wind lied to me. No storm. My nose is losing its touch — or the sky changed its mind.",
            "I was sure a storm was coming. The clouds thought better of it. I am not disappointed.",
        };

        /// <summary>Called when a sensed storm never arrives.</summary>
        public static void OnFalseAlarm(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(FalseAlarmLines), JournalCategory.Weather, 0.35f);
        }

        private static readonly string[] RainKitsPlaying = new[]
        {
            "The kits are out in the rain, shrieking with joy, soaked to the skin. I should scold them. I am laughing instead.",
            "Rain, and the kits are dancing in it like the drops are toys. Their joy is waterproof. Mine too, apparently.",
            "The little ones chase raindrops and slip in the mud and get up giggling. The rain is their playground. I remember.",
        };

        /// <summary>Called when kits play in the rain near the fox.</summary>
        public static void OnRainKitsPlaying(Simulation sim)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;
            sim.Journal(sim.Now, sim.EventRng.Pick(RainKitsPlaying), JournalCategory.Social, 0.55f);
        }

        // -- the tick: ambient thoughts --------------------------------------------------

        /// <summary>
        /// Called from Simulation.FixedStep. Rolls for ambient thoughts:
        /// sunset/sunrise (once daily), night musings, loneliness, kits playing.
        ///
        /// FULLY STATELESS AND DETERMINISTIC: triggers are pure functions of
        /// (seed, elapsed time), using derived RNG streams. No mutable cooldowns,
        /// no EventRng consumption — save/load safe by construction.
        /// </summary>
        public static void Tick(Simulation sim, float h)
        {
            var a = sim.State.Agent;
            if (!a.IsAlive) return;

            double elapsed = sim.State.ElapsedSeconds;
            if (elapsed < h) return;

            // Hour boundaries: detect crossings by comparing previous and current hour.
            float currHour = sim.State.TimeOfDay;
            float prevHour = (float)(((elapsed - h) / 3600.0) % 24.0);
            if (prevHour < 0) prevHour += 24f;
            long dayIndex = (long)(elapsed / 86400.0);

            // Sunset: crossed 18.4h today.
            if (prevHour < 18.4f && currHour >= 18.4f && currHour < 19.5f)
            {
                var rng = SeededRandom.Derive(sim.State.Seed, "voice:sunset:" + dayIndex);
                if (rng.NextFloat() < 0.65f) OnSunset(sim, rng);
            }
            // Sunrise: crossed 6.0h today. (Handles midnight wrap: prevHour 5.9 -> currHour 6.0.)
            if (prevHour < 6.0f && currHour >= 6.0f && currHour < 7.0f)
            {
                var rng = SeededRandom.Derive(sim.State.Seed, "voice:sunrise:" + dayIndex);
                if (rng.NextFloat() < 0.55f) OnSunrise(sim, rng);
            }
            // Night musings: crossed 23.0h.
            if (prevHour < 23.0f && currHour >= 23.0f)
            {
                var rng = SeededRandom.Derive(sim.State.Seed, "voice:night:" + dayIndex);
                if (rng.NextFloat() < 0.35f) OnNightThought(sim, rng);
            }
            // Loneliness: every ~33 game-hours (120000s), on boundary crossing.
            long lonelyPeriod = 120000L;
            long currLonelyIdx = (long)(elapsed / lonelyPeriod);
            long prevLonelyIdx = (long)((elapsed - h) / lonelyPeriod);
            if (currLonelyIdx > prevLonelyIdx)
            {
                var rng = SeededRandom.Derive(sim.State.Seed, "voice:lonely:" + currLonelyIdx);
                // Only if truly alone: no kindred within 60u.
                bool anyoneNear = false;
                foreach (var e in sim.State.Entities)
                {
                    if (e.Kind != EntityKind.Kindred || e.Health <= 0) continue;
                    float dx = e.X - a.X, dz = e.Z - a.Z;
                    if (dx * dx + dz * dz < 3600f) { anyoneNear = true; break; }
                }
                if (!anyoneNear && rng.NextFloat() < 0.5f)
                {
                    // The solitary barely notice; the gregarious ache.
                    if (a.Traits.Sociability >= 0.4f || rng.NextFloat() >= 0.6f)
                        OnLonely(sim, rng);
                }
                // Kits playing: same slow cadence, separate roll.
                int playingNear = 0;
                foreach (var kit in sim.State.Kits)
                {
                    if (!kit.IsAlive || kit.State != "Play") continue;
                    float dx = kit.X - a.X, dz = kit.Z - a.Z;
                    if (dx * dx + dz * dz < 900f) playingNear++;
                }
                if (playingNear >= 1 && rng.NextFloat() < 0.30f)
                    OnKitsPlaying(sim, playingNear, rng);
                // Rain makes kit-play extra joyful: a dedicated line.
                if (playingNear >= 1 && sim.State.Weather == Weather.Rain && rng.NextFloat() < 0.35f)
                    OnRainKitsPlaying(sim);
            }
            // Birthday: the fox reflects on another year. Deterministic via Age.
            // Age increments by h/86400/DaysPerYear per step; detect the integer crossing.
            double ageNow = a.Age;
            double agePrev = a.Age - h / 86400.0 / LineageSystem.DaysPerYear;
            if ((int)ageNow > (int)agePrev && (int)ageNow > 0)
            {
                var rng = SeededRandom.Derive(sim.State.Seed, "voice:birthday:" + (int)ageNow);
                OnBirthday(sim, (int)ageNow, rng);
            }
        }
    }
}
