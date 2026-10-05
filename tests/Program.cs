// Solace.Core test suite — plain console runner, no test framework dependency.
using System;
using System.Collections.Generic;
using Solace.Core;

public static class Tests
{
    private static int _passed = 0;
    private static int _failed = 0;

    private static void Check(bool cond, string name, string detail)
    {
        if (cond) { _passed++; Console.WriteLine("  PASS " + name); }
        else { _failed++; Console.WriteLine("  FAIL " + name + " — " + detail); }
    }

    public static int Main()
    {
        Console.WriteLine("Solace.Core tests");
        TestRngDeterminism();
        TestNoiseDeterminism();
        TestWorldGenDeterminism();
        TestSaveRoundTrip();
        TestStepDeterminismAcrossSaveLoad();
        TestAiScoringSanity();
        TestOfflineBounds();
        TestCompanionTruth();
        TestCompanionTools();
        Console.WriteLine();
        Console.WriteLine("passed: " + _passed + ", failed: " + _failed);
        return _failed == 0 ? 0 : 1;
    }

    // -- 1. RNG determinism --------------------------------------------------------

    private static void TestRngDeterminism()
    {
        Console.WriteLine("[rng]");
        var a = new SeededRandom(12345u);
        var b = new SeededRandom(12345u);
        bool same = true;
        for (int i = 0; i < 2000; i++)
        {
            if (a.NextInt(0, 1000000) != b.NextInt(0, 1000000)) same = false;
            if (a.NextFloat() != b.NextFloat()) same = false;
            if (a.NextDouble() != b.NextDouble()) same = false;
        }
        Check(same, "same seed -> same 6000-draw sequence", "sequences diverged");

        // State save/restore resumes the exact sequence.
        var c = new SeededRandom(777u);
        for (int i = 0; i < 500; i++) c.NextUInt();
        uint saved = c.State;
        var d = new SeededRandom(1u);
        d.State = saved;
        bool resume = true;
        for (int i = 0; i < 500; i++)
            if (c.NextUInt() != d.NextUInt()) resume = false;
        Check(resume, "state save/restore resumes sequence", "resumed sequence diverged");

        // Domain partitioning: same master seed, different domains differ.
        var w = SeededRandom.Derive(42, "world");
        var ai = SeededRandom.Derive(42, "ai");
        Check(w.NextUInt() != ai.NextUInt(), "domain streams diverge", "world/ai streams identical");
        var w2 = SeededRandom.Derive(42, "world");
        var w3 = SeededRandom.Derive(42, "world");
        Check(w2.NextUInt() == w3.NextUInt(), "derived stream is reproducible", "derive not stable");

        // Shuffle determinism.
        var s1 = new SeededRandom(9u);
        var s2 = new SeededRandom(9u);
        var l1 = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
        var l2 = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
        s1.Shuffle(l1); s2.Shuffle(l2);
        bool shuf = true;
        for (int i = 0; i < l1.Count; i++) if (l1[i] != l2[i]) shuf = false;
        Check(shuf, "shuffle deterministic", "shuffles diverged");
    }

    // -- 2. Noise determinism ------------------------------------------------------

    private static void TestNoiseDeterminism()
    {
        Console.WriteLine("[noise]");
        float v1 = Noise.Value(12.34f, -56.78f, 1234u);
        float v2 = Noise.Value(12.34f, -56.78f, 1234u);
        Check(v1 == v2, "value noise stable", v1 + " != " + v2);
        float f1 = Noise.Fbm(3.1f, 4.7f, 4, 2.0f, 0.5f, 999u);
        float f2 = Noise.Fbm(3.1f, 4.7f, 4, 2.0f, 0.5f, 999u);
        Check(f1 == f2, "fbm stable", f1 + " != " + f2);
        Check(f1 >= 0f && f1 <= 1f, "fbm in [0,1]", "f1=" + f1);
        float f3 = Noise.Fbm(3.1f, 4.7f, 4, 2.0f, 0.5f, 1000u);
        Check(f3 != f1, "fbm varies with seed", "identical across seeds");
        Check(Noise.Hash2(-5, -7, 42u) == Noise.Hash2(-5, -7, 42u), "integer hash stable", "hash moved");
    }

    // -- 3. World-gen determinism ----------------------------------------------------

    private static void TestWorldGenDeterminism()
    {
        Console.WriteLine("[worldgen]");
        var cfg = new WorldConfig { Seed = 20261005 };
        var w1 = WorldGenerator.Generate(cfg);
        var w2 = WorldGenerator.Generate(cfg); // same process: catches static-state leaks

        bool heights = w1.Heights.Length == w2.Heights.Length;
        if (heights)
            for (int i = 0; i < w1.Heights.Length; i++)
                if (w1.Heights[i] != w2.Heights[i]) { heights = false; break; }
        Check(heights, "same seed -> identical heights", "heightmaps differ");

        bool pois = w1.Pois.Count == w2.Pois.Count;
        if (pois)
            for (int i = 0; i < w1.Pois.Count; i++)
            {
                var p = w1.Pois[i]; var q = w2.Pois[i];
                if (p.Id != q.Id || p.Type != q.Type || p.X != q.X || p.Z != q.Z
                    || p.Name != q.Name || p.Discovered != q.Discovered) { pois = false; break; }
            }
        Check(pois, "same seed -> identical POIs", "POI lists differ");

        bool river = w1.RiverPath.Count == w2.RiverPath.Count;
        if (river)
            for (int i = 0; i < w1.RiverPath.Count; i++)
                if (w1.RiverPath[i].X != w2.RiverPath[i].X || w1.RiverPath[i].Z != w2.RiverPath[i].Z) { river = false; break; }
        Check(river, "same seed -> identical river", "river paths differ");

        var w3 = WorldGenerator.Generate(new WorldConfig { Seed = 1 });
        bool differs = false;
        for (int i = 0; i < w1.Heights.Length; i += 97)
            if (w1.Heights[i] != w3.Heights[i]) { differs = true; break; }
        Check(differs, "different seed -> different world", "worlds identical across seeds");

        // Structural sanity: required POI kinds exist.
        bool hamlet = false, broch = false, cairn = false, bush = false, overlook = false, campfire = false;
        foreach (var p in w1.Pois)
        {
            if (p.Type == PoiType.Hamlet) hamlet = true;
            if (p.Type == PoiType.BrochRuin) broch = true;
            if (p.Type == PoiType.Cairn) cairn = true;
            if (p.Type == PoiType.BerryBush && p.Stock > 0) bush = true;
            if (p.Type == PoiType.Overlook) overlook = true;
            if (p.Type == PoiType.Campfire) campfire = true;
            Check(!w1.IsWater(p.X, p.Z), "POI on dry land (" + p.Type + ")", "POI in water at " + p.X + "," + p.Z);
        }
        Check(hamlet && broch && cairn && bush && overlook && campfire, "all POI kinds placed", "missing kinds");
        Check(!w1.IsWater(w1.SpawnPoint.X, w1.SpawnPoint.Z), "spawn on dry land", "spawn in water");
    }

    // -- 4. Save round-trip ----------------------------------------------------------

    private static void TestSaveRoundTrip()
    {
        Console.WriteLine("[save]");
        var sim = Simulation.NewLife(777);
        sim.Step(2f); // let life happen briefly
        string json1 = SaveSystem.Save(sim.State);
        Check(json1.Length > 10000, "save is substantial (" + json1.Length + " chars)", "save suspiciously small");
        GameState loaded = null;
        try { loaded = SaveSystem.Load(json1); }
        catch (Exception ex) { Check(false, "load parses", ex.Message); return; }
        Check(loaded != null, "load parses", "null state");
        string json2 = SaveSystem.Save(loaded);
        Check(json1 == json2, "save->load->save byte-identical", "lengths " + json1.Length + " vs " + json2.Length);
    }

    // -- 5. Determinism across save/load ----------------------------------------------

    private static void TestStepDeterminismAcrossSaveLoad()
    {
        Console.WriteLine("[determinism]");
        var simA = Simulation.NewLife(4242);
        simA.Step(2f);
        string checkpoint = SaveSystem.Save(simA.State);
        var simB = new Simulation(SaveSystem.Load(checkpoint));
        simA.Step(2f);
        simB.Step(2f);
        string a = SaveSystem.Save(simA.State);
        string b = SaveSystem.Save(simB.State);
        Check(a == b, "save/load preserves trajectory", "states diverged after continued stepping");
    }

    // -- 6. AI scoring sanity ------------------------------------------------------------

    private static void TestAiScoringSanity()
    {
        Console.WriteLine("[ai]");
        var sim = Simulation.NewLife(31337);
        var agent = sim.State.Agent;

        // Starving-ish agent next to a known berry bush, no threats around.
        PointOfInterest bush = null;
        foreach (var p in sim.State.World.Pois)
            if (p.Type == PoiType.BerryBush && p.Stock > 0) { bush = p; break; }
        Check(bush != null, "test setup: berry bush exists", "no bush placed");
        if (bush == null) return;

        agent.KnownPoiIds.Add(bush.Id);
        agent.X = bush.X + 6f;
        agent.Z = bush.Z;
        agent.Hunger = 80f;
        agent.Thirst = 10f;
        agent.Energy = 90f;
        agent.Health = 100f;
        agent.Curiosity = 20f;
        agent.Influences.Clear();
        sim.State.Entities.RemoveAll(e => e.Kind != EntityKind.Villager); // no animals/wolves
        // Villagers far away so Greet can't compete.
        foreach (var e in sim.State.Entities) { e.X += 1000f; e.Z += 1000f; }

        var ctx = new BrainContext { Sim = sim, Ai = sim.AiRng, Ev = sim.EventRng, Now = sim.Now };
        string choice = sim.Brain.Decide(ctx);
        Check(choice == "Eat", "hungry agent near berries chooses Eat (got " + choice + ")",
            "trace: " + sim.Brain.LastDecisionTrace.Reason);

        // Survival override: critical hunger must ignore even attractive novelty.
        agent.Hunger = 95f;
        agent.Influences.Add(new PlayerInfluence { Text = "go explore!", Topic = "explore", Weight = 0.6f, Time = sim.Now });
        string choice2 = sim.Brain.Decide(ctx);
        Check(choice2 == "Eat", "critical hunger ignores suggestion, still Eat (got " + choice2 + ")", "override failed");

        // Trace legibility.
        var trace = sim.Brain.LastDecisionTrace;
        Check(trace.Components.Count > 0 && trace.Reason.Length > 10, "decision trace is legible",
            "empty trace");
    }

    // -- 7. Offline progression bounds -----------------------------------------------------

    private static void TestOfflineBounds()
    {
        Console.WriteLine("[offline]");
        var sim = Simulation.NewLife(555);
        var s = sim.State;
        s.Agent.Hunger = 30f; s.Agent.Thirst = 30f; s.Agent.Energy = 80f; s.Agent.Health = 90f;

        SaveSystem.ApplyOfflineProgress(s, TimeSpan.FromDays(7), OfflineMode.Stillness);
        Check(s.ElapsedSeconds == 0f, "stillness changes nothing", "elapsed=" + s.ElapsedSeconds);

        SaveSystem.ApplyOfflineProgress(s, TimeSpan.FromDays(7), OfflineMode.QuietLife);
        Check(s.Agent.IsAlive, "7-day quiet life: agent alive", "agent died");
        Check(s.Agent.Hunger <= 80f && s.Agent.Thirst <= 80f, "quiet life: needs bounded",
            "hunger=" + s.Agent.Hunger + " thirst=" + s.Agent.Thirst);
        Check(s.Agent.Health >= 25f, "quiet life: no harm", "health=" + s.Agent.Health);

        var sim2 = Simulation.NewLife(556);
        var s2 = sim2.State;
        s2.Agent.Health = 60f;
        int journalBefore = s2.Journal.Count;
        SaveSystem.ApplyOfflineProgress(s2, TimeSpan.FromDays(7), OfflineMode.LivingWorld);
        Check(s2.Agent.IsAlive, "7-day living world (no severe cause): agent alive", "agent died without cause");
        Check(s2.Agent.Health >= 5f, "living world: health floored", "health=" + s2.Agent.Health);
        int idx = 0, meaningful = 0;
        foreach (var e in s2.Journal.Entries)
        {
            if (idx++ >= journalBefore && e.Salience >= 0.7f) meaningful++;
        }
        Check(meaningful <= 6, "living world: bounded drama (" + meaningful + " salient entries)", "too many major events");

        // 30-day absence is capped to 7 days of effect.
        var sim3 = Simulation.NewLife(557);
        SaveSystem.ApplyOfflineProgress(sim3.State, TimeSpan.FromDays(30), OfflineMode.QuietLife);
        Check(sim3.State.ElapsedSeconds <= 7 * 86400 + 1f, "away time capped at 7 days",
            "elapsed=" + sim3.State.ElapsedSeconds);
    }

    // -- 8. Companion truth contract ----------------------------------------------------------

    private static void TestCompanionTruth()
    {
        Console.WriteLine("[companion]");
        var sim = Simulation.NewLife(606);
        var s = sim.State;

        var r1 = Companion.Respond(s, "Who is Zxq?");
        Check(r1.Intent == "memory" && r1.Text.Contains("don't know"), "unknown person -> honest unknown",
            "said: " + r1.Text);

        var r2 = Companion.Respond(s, "You should rest more.");
        Check(r2.Intent == "suggestion" && r2.Influence != null && r2.Influence.Topic == "rest",
            "suggestion logs influence", "intent=" + r2.Intent);
        Check(s.Agent.Influences.Count > 0, "influence stored on agent", "no influence stored");

        var r3 = Companion.Respond(s, "What are you doing?");
        Check(r3.Intent == "status" && r3.Text.Length > 20, "status answers from state", "said: " + r3.Text);
        Check(!r3.Text.Contains("Zxq"), "status invents nothing", "hallucinated");

        var r4 = Companion.Respond(s, "Why did you do that?");
        Check(r4.Intent == "reason", "why -> reason", "intent=" + r4.Intent);

        var r5 = Companion.Respond(s, "hello there");
        Check(r5.Intent == "greeting", "greeting detected", "intent=" + r5.Intent);

        var r6 = Companion.Respond(s, "goodbye");
        Check(r6.Intent == "farewell", "farewell detected", "intent=" + r6.Intent);
    }

    // -- 9. Companion tools ----------------------------------------------------------------------

    private static void TestCompanionTools()
    {
        Console.WriteLine("[tools]");
        var sim = Simulation.NewLife(707);
        var s = sim.State;
        var reg = ToolRegistry.CreateDefault();

        string bad = reg.Invoke(s, "set_weather", "weather=Blizzard");
        Check(bad.StartsWith("ERROR"), "invalid weather rejected", bad);

        string ok = reg.Invoke(s, "set_weather", "weather=Rain");
        Check(ok.StartsWith("OK") && s.Weather == Weather.Rain, "set_weather works", ok);

        string cool = reg.Invoke(s, "set_weather", "weather=Clear");
        Check(cool.StartsWith("ERROR"), "weather cooldown enforced", cool);

        int entitiesBefore = s.Entities.Count;
        string sp = reg.Invoke(s, "spawn_encounter", "kind=deer;distance=25");
        Check(sp.StartsWith("OK") && s.Entities.Count == entitiesBefore + 1, "spawn_encounter works", sp);

        string spBad = reg.Invoke(s, "spawn_encounter", "kind=dragon;distance=25");
        Check(spBad.StartsWith("ERROR"), "invalid kind rejected", spBad);

        string jn = reg.Invoke(s, "add_journal", "category=Reflection;text=A quiet moment.");
        Check(jn.StartsWith("OK"), "add_journal works", jn);

        string jnBad = reg.Invoke(s, "add_journal", "category=Reflection;text=");
        Check(jnBad.StartsWith("ERROR"), "empty journal text rejected", jnBad);

        string rv = reg.Invoke(s, "reveal_poi", "range=150");
        Check(rv.StartsWith("OK"), "reveal_poi works", rv);

        string unk = reg.Invoke(s, "nope", "");
        Check(unk.StartsWith("ERROR"), "unknown tool rejected", unk);
    }
}
