// Solace.Core — lifetime statistics: tracking only, never game logic.
// Detection only: this system READS state and accumulates counters. It never
// changes how the fox behaves, how combat resolves, or how the world works.
// The dashboard is the measure of a life: distance, discoveries, generations.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    /// <summary>
    /// Lifetime statistics for one world. Two owners:
    ///  - Core (StatSystem.Tick): deterministic counters — distance, counts.
    ///  - Unity (StatView): real-time playtime + daily buckets. Core NEVER
    ///    touches these, so save/load determinism is unaffected.
    /// </summary>
    public class StatState
    {
        // -- Core-owned (deterministic) --
        public float DistanceTraveled;     // meters, from agent position deltas
        public int PoisDiscovered;
        public int KitsBorn;
        public int PredatorsDefeated;
        public int Greetings;
        public int TalesDistilled;
        public int DreamsDreamed;
        public int DreamsFulfilled;
        public int Generations;           // highest generation reached
        public float MaxDistanceFromDen;  // meters, farthest roam

        // Edge detection (bounded, pruned).
        public List<int> SeenKitIds = new List<int>();
        public List<int> SeenDeadPredatorIds = new List<int>();

        // Position continuity across ticks (serialized so save/load stays exact).
        public float LastX, LastZ;
        public bool HasLastPos;
        public float DenX, DenZ;
        public bool HasDen;

        // -- Unity-owned (real time; Core never reads or writes) --
        public float RealPlaySeconds;
        public List<string> DayDates = new List<string>();  // "yyyy-MM-dd", parallel...
        public List<float> DaySeconds = new List<float>();  // ...with per-day playtime

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("distanceTraveled", DistanceTraveled);
            o.Add("poisDiscovered", PoisDiscovered);
            o.Add("kitsBorn", KitsBorn);
            o.Add("predatorsDefeated", PredatorsDefeated);
            o.Add("greetings", Greetings);
            o.Add("talesDistilled", TalesDistilled);
            o.Add("dreamsDreamed", DreamsDreamed);
            o.Add("dreamsFulfilled", DreamsFulfilled);
            o.Add("generations", Generations);
            o.Add("maxDistanceFromDen", MaxDistanceFromDen);
            var sk = new JsonArray();
            foreach (var id in SeenKitIds) sk.Add(id);
            o.Add("seenKitIds", sk);
            var sp = new JsonArray();
            foreach (var id in SeenDeadPredatorIds) sp.Add(id);
            o.Add("seenDeadPredatorIds", sp);
            o.Add("lastX", LastX);
            o.Add("lastZ", LastZ);
            o.Add("hasLastPos", HasLastPos);
            o.Add("denX", DenX);
            o.Add("denZ", DenZ);
            o.Add("hasDen", HasDen);
            o.Add("realPlaySeconds", RealPlaySeconds);
            var dd = new JsonArray();
            foreach (var d in DayDates) dd.Add(d);
            o.Add("dayDates", dd);
            var ds = new JsonArray();
            foreach (var s in DaySeconds) ds.Add(s);
            o.Add("daySeconds", ds);
            return o;
        }

        public static StatState FromJson(JsonObject o)
        {
            var s = new StatState();
            s.DistanceTraveled = JsonHelpers.GetFloat(o, "distanceTraveled", 0f);
            s.PoisDiscovered = JsonHelpers.GetInt(o, "poisDiscovered", 0);
            s.KitsBorn = JsonHelpers.GetInt(o, "kitsBorn", 0);
            s.PredatorsDefeated = JsonHelpers.GetInt(o, "predatorsDefeated", 0);
            s.Greetings = JsonHelpers.GetInt(o, "greetings", 0);
            s.TalesDistilled = JsonHelpers.GetInt(o, "talesDistilled", 0);
            s.DreamsDreamed = JsonHelpers.GetInt(o, "dreamsDreamed", 0);
            s.DreamsFulfilled = JsonHelpers.GetInt(o, "dreamsFulfilled", 0);
            s.Generations = JsonHelpers.GetInt(o, "generations", 1);
            s.MaxDistanceFromDen = JsonHelpers.GetFloat(o, "maxDistanceFromDen", 0f);
            JsonValue v;
            if (o.TryGet("seenKitIds", out v) && !v.IsNull)
            {
                var a = v.AsArray();
                for (int i = 0; i < a.Count; i++) s.SeenKitIds.Add(((JsonNumber)a[i]).AsInt());
            }
            if (o.TryGet("seenDeadPredatorIds", out v) && !v.IsNull)
            {
                var a = v.AsArray();
                for (int i = 0; i < a.Count; i++) s.SeenDeadPredatorIds.Add(((JsonNumber)a[i]).AsInt());
            }
            s.LastX = JsonHelpers.GetFloat(o, "lastX", 0f);
            s.LastZ = JsonHelpers.GetFloat(o, "lastZ", 0f);
            s.HasLastPos = JsonHelpers.GetBool(o, "hasLastPos", false);
            s.DenX = JsonHelpers.GetFloat(o, "denX", 0f);
            s.DenZ = JsonHelpers.GetFloat(o, "denZ", 0f);
            s.HasDen = JsonHelpers.GetBool(o, "hasDen", false);
            s.RealPlaySeconds = JsonHelpers.GetFloat(o, "realPlaySeconds", 0f);
            if (o.TryGet("dayDates", out v) && !v.IsNull)
            {
                var a = v.AsArray();
                for (int i = 0; i < a.Count; i++) s.DayDates.Add(((JsonString)a[i]).Value);
            }
            if (o.TryGet("daySeconds", out v) && !v.IsNull)
            {
                var a = v.AsArray();
                for (int i = 0; i < a.Count; i++) s.DaySeconds.Add(((JsonNumber)a[i]).AsFloat());
            }
            // Guard against mismatched parallel arrays from hand-edited saves.
            while (s.DaySeconds.Count > s.DayDates.Count) s.DaySeconds.RemoveAt(s.DaySeconds.Count - 1);
            while (s.DayDates.Count > s.DaySeconds.Count) s.DayDates.RemoveAt(s.DayDates.Count - 1);
            return s;
        }
    }

    /// <summary>
    /// Accumulates lifetime statistics. Detection only — reads state, updates
    /// StatState, never mutates game logic. Fully deterministic: no RNG use,
    /// no real-time reads, so save/load trajectory tests are unaffected.
    /// </summary>
    public static class StatSystem
    {
        private const int MaxTrackedIds = 400; // bound the edge-detection lists
        // Per-tick distance cap: real movement is ~0.22m/tick max; anything
        // larger is a teleport (succession, heir spawn) and must not count.
        private const float MaxTickDistance = 50f;

        public static void Tick(Simulation sim)
        {
            var s = sim.State;
            var st = s.Stats;
            var a = s.Agent;

            // -- distance traveled (agent position deltas) --
            if (a.IsAlive)
            {
                if (!st.HasLastPos)
                {
                    st.LastX = a.X; st.LastZ = a.Z; st.HasLastPos = true;
                }
                else
                {
                    float dx = a.X - st.LastX, dz = a.Z - st.LastZ;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d < MaxTickDistance) st.DistanceTraveled += d;
                    st.LastX = a.X; st.LastZ = a.Z;
                }
            }

            // -- den location (cached once) + farthest roam --
            if (!st.HasDen)
            {
                foreach (var p in s.World.Pois)
                {
                    if (p.Type == PoiType.Den)
                    {
                        st.DenX = p.X; st.DenZ = p.Z; st.HasDen = true;
                        break;
                    }
                }
            }
            if (st.HasDen && a.IsAlive)
            {
                float dx = a.X - st.DenX, dz = a.Z - st.DenZ;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d > st.MaxDistanceFromDen) st.MaxDistanceFromDen = d;
            }

            // -- places discovered --
            int pois = 0;
            foreach (var p in s.World.Pois) if (p.Discovered) pois++;
            st.PoisDiscovered = pois;

            // -- edge detection: kit births --
            foreach (var kit in s.Kits)
            {
                if (!st.SeenKitIds.Contains(kit.Id))
                {
                    st.SeenKitIds.Add(kit.Id);
                    st.KitsBorn++;
                }
            }
            Prune(st.SeenKitIds);

            // -- edge detection: predator kills --
            foreach (var e in s.Entities)
            {
                if (e.Kind == EntityKind.Predator && !e.IsAlive &&
                    !st.SeenDeadPredatorIds.Contains(e.Id))
                {
                    st.SeenDeadPredatorIds.Add(e.Id);
                    st.PredatorsDefeated++;
                }
            }
            Prune(st.SeenDeadPredatorIds);

            // -- greetings (summed across all known kindred) --
            int g = 0;
            foreach (var person in s.Social.People) g += person.Greetings;
            st.Greetings = g;

            // -- direct reads --
            st.TalesDistilled = s.Lineage.Tales.Count;
            st.DreamsDreamed = s.Dreams.Dreams.Count;
            int fulfilled = 0;
            foreach (var d in s.Dreams.Dreams)
                if (d.Status == DreamStatus.Fulfilled) fulfilled++;
            st.DreamsFulfilled = fulfilled;
            if (s.Lineage.Generation > st.Generations) st.Generations = s.Lineage.Generation;
        }

        private static void Prune(List<int> list)
        {
            while (list.Count > MaxTrackedIds) list.RemoveAt(0);
        }
    }
}
