// Solace.Core — the colossal slow life of the world.
//
// Kilometer-tall migrating trees (tree-walkers) drift along seeded routes,
// and seed-islands drift on the loch. Sim-light: a handful of entities moved
// coarsely in sim time. Presentation reads WorldData.NightRiverGlow (rivers
// glow at night) and the colossi positions to render the wonder.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public enum ColossusKind
    {
        TreeWalker, // a kilometer-tall migrating tree, slow as weather
        SeedIsle    // a drifting seed-island on the loch
    }

    public class ColossusState
    {
        public int Id;
        public ColossusKind Kind;
        public float X, Z;
        public float Heading;        // radians, 0 = +Z
        public float SpeedMPerDay;   // very slow: tens of meters per game-day
        public float RadiusMeters;
        public float LastNotedAt = -9999f; // last proximity journal, game seconds

        public V2 Pos { get { return new V2(X, Z); } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("kind", Kind.ToString());
            o.Add("x", X); o.Add("z", Z);
            o.Add("heading", Heading);
            o.Add("speedMPerDay", SpeedMPerDay);
            o.Add("radiusMeters", RadiusMeters);
            o.Add("lastNotedAt", LastNotedAt);
            return o;
        }

        public static ColossusState FromJson(JsonObject o)
        {
            var c = new ColossusState();
            c.Id = JsonHelpers.GetInt(o, "id", 0);
            c.Kind = (ColossusKind)Enum.Parse(typeof(ColossusKind),
                JsonHelpers.GetString(o, "kind", "TreeWalker"));
            c.X = JsonHelpers.GetFloat(o, "x", 0f); c.Z = JsonHelpers.GetFloat(o, "z", 0f);
            c.Heading = JsonHelpers.GetFloat(o, "heading", 0f);
            c.SpeedMPerDay = JsonHelpers.GetFloat(o, "speedMPerDay", 20f);
            c.RadiusMeters = JsonHelpers.GetFloat(o, "radiusMeters", 100f);
            c.LastNotedAt = JsonHelpers.GetFloat(o, "lastNotedAt", -9999f);
            return c;
        }
    }

    /// <summary>
    /// Drifts the colossi along their slow routes. Called on a coarse cadence
    /// (not every 1/30s step); cost is trivial.
    /// </summary>
    public static class ColossusSystem
    {
        /// <summary>How near (meters) a colossus must come to be journaled.</summary>
        public const float NoteRadius = 160f;

        public static void Tick(GameState s, SeededRandom rng, float dt, float now,
                                Action<float, string, JournalCategory, float> journal)
        {
            float half = s.World.HalfSize - 12f;
            foreach (var c in s.World.Colossi)
            {
                float dayFrac = dt / 86400f;
                float dist = c.SpeedMPerDay * dayFrac;
                c.X += (float)Math.Sin(c.Heading) * dist;
                c.Z += (float)Math.Cos(c.Heading) * dist;

                // Wander the heading very slowly; turn back at the world rim.
                if (rng.NextFloat() < dayFrac * 2f)
                    c.Heading += rng.NextFloat(-0.15f, 0.15f);
                if (Math.Abs(c.X) > half || Math.Abs(c.Z) > half)
                    c.Heading += MathF.PI * 0.92f;
                c.X = MathX.Clamp(c.X, -half, half);
                c.Z = MathX.Clamp(c.Z, -half, half);

                // A passing colossus is worth writing down, at most every 2 days.
                if (journal != null && now - c.LastNotedAt > 2f * 86400f)
                {
                    float d = V2.Distance(c.Pos, s.Agent.Pos);
                    if (s.Agent.IsAlive && d < NoteRadius)
                    {
                        c.LastNotedAt = now;
                        journal(now,
                            c.Kind == ColossusKind.TreeWalker
                                ? "A tree-walker crossed the valley today, slow as weather. The ground hummed under my paws."
                                : "A seed-isle drifted on the still loch, trailing light.",
                            JournalCategory.Travel, 0.7f);
                    }
                }
            }
        }

        /// <summary>Advances colossi silently (used by the away-sim).</summary>
        public static void DriftQuietly(GameState s, SeededRandom rng, float seconds)
        {
            Tick(s, rng, seconds, s.ElapsedSeconds, null);
        }
    }
}
