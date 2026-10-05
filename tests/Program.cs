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
        TestSuccession();
        TestTaleCap();
        TestKitLearning();
        TestAging();
        TestSickness();
        TestBonding();
        TestColossi();
        TestLineageSaveRoundTrip();
        TestOfflineLineageSafety();
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
        bool den = false, hive = false, cairn = false, bush = false, overlook = false, hollow = false;
        foreach (var p in w1.Pois)
        {
            if (p.Type == PoiType.Den) den = true;
            if (p.Type == PoiType.InsectileRuin) hive = true;
            if (p.Type == PoiType.Cairn) cairn = true;
            if (p.Type == PoiType.GlowberryBush && p.Stock > 0) bush = true;
            if (p.Type == PoiType.Overlook) overlook = true;
            if (p.Type == PoiType.EmberHollow) hollow = true;
            Check(!w1.IsWater(p.X, p.Z), "POI on dry land (" + p.Type + ")", "POI in water at " + p.X + "," + p.Z);
        }
        Check(den && hive && cairn && bush && overlook && hollow, "all POI kinds placed", "missing kinds");
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
            if (p.Type == PoiType.GlowberryBush && p.Stock > 0) { bush = p; break; }
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
        sim.State.Entities.RemoveAll(e => e.Kind != EntityKind.Kindred); // no animals/predators
        // Kindred far away so Greet can't compete.
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

    // -- 10. Succession ------------------------------------------------------------

    private static void TestSuccession()
    {
        Console.WriteLine("[succession]");
        var sim = Simulation.NewLife(9001);
        var a = sim.State.Agent;
        string parentName = a.Name;

        // Two kits; the second is eldest.
        LineageSystem.BirthLitter(sim, 2, -1);
        sim.State.Kits[0].Age = 0.4f; sim.State.Kits[0].Name = "KitA";
        sim.State.Kits[1].Age = 1.1f; sim.State.Kits[1].Name = "KitB";
        // A tale, and a discovered place the heir must keep knowing.
        LineageSystem.MaybeDistillTale(sim, "The Hollow Hive", "Caution", 0.04f, "test origin");
        a.KnownPoiIds.Add(424242);

        sim.KillAgent("taken by a gloom-maw on the high fell");
        var heir = sim.State.Agent;
        Check(heir.IsProtagonist, "eldest kit becomes protagonist", "flag not passed");
        Check(heir.Name == "KitB", "eldest kit inherits (got " + heir.Name + ")", "wrong heir");
        Check(heir.Generation == 2, "heir generation = 2", "got " + heir.Generation);
        Check(sim.State.Lineage.Generation == 2, "lineage generation = 2", "got " + sim.State.Lineage.Generation);
        Check(heir.TalesKnown.Count > 0, "tales pass to heir", "TalesKnown empty");
        Check(heir.Traits.Caution >= 0.089f, "tale applied as instinct nudge", "Caution=" + heir.Traits.Caution);
        Check(heir.KnownPoiIds.Contains(424242), "POI discovery persists across succession", "lost");
        Check(Math.Abs(heir.LightShade - a.LightShade) <= 0.061f, "light-shade inherited with slight variation",
            "shade " + heir.LightShade + " vs " + a.LightShade);

        bool closeEntry = false;
        foreach (var e in sim.State.Journal.Entries)
            if (e.Category == JournalCategory.Chapter && e.Text.Contains("Chapter 1 ends"))
                closeEntry = true;
        Check(closeEntry, "chapter-close journal entry", "missing");
        Check(sim.State.Lineage.Chapters.Count == 1, "chapter recorded", "count=" + sim.State.Lineage.Chapters.Count);
        Check(sim.State.Kits.Count == 1, "heir removed from kits; sibling remains", "count=" + sim.State.Kits.Count);

        // No living kit: a young distant kin arrives, honestly journaled.
        var sim2 = Simulation.NewLife(9002);
        sim2.KillAgent("dimmed the way evening dims, old and full of tales");
        var heir2 = sim2.State.Agent;
        Check(heir2.Generation == 2, "distant kin: generation = 2", "got " + heir2.Generation);
        Check(heir2.IsProtagonist, "distant kin: becomes protagonist", "flag not passed");
        bool kinLine = false;
        foreach (var e in sim2.State.Journal.Entries)
            if (e.Text.Contains("young kin came down from the high fells")) kinLine = true;
        Check(kinLine, "distant kin arrival journaled honestly", "missing line");
        bool oldClose = false;
        foreach (var e in sim2.State.Journal.Entries)
            if (e.Category == JournalCategory.Chapter && e.Text.Contains("old and full of tales")) oldClose = true;
        Check(oldClose, "peaceful old-age chapter close", "missing");
    }

    // -- 11. Kit learning ------------------------------------------------------------

    private static void TestKitLearning()
    {
        Console.WriteLine("[kits]");
        var sim = Simulation.NewLife(9101);
        var a = sim.State.Agent;
        LineageSystem.BirthLitter(sim, 1, -1);
        var kit = sim.State.Kits[0];

        // Kit AT the parent while the parent eats: fed, learns Forage, no
        // energy wasted oscillating.
        kit.X = a.X; kit.Z = a.Z;
        a.CurrentGoal = "Eat";
        float forage0 = kit.Forage;
        float hunger0 = kit.Hunger;
        for (int i = 0; i < 20; i++) KitBrain.Tick(sim, kit, 60f);
        Check(kit.IsAlive, "kit survives being fed", "died");
        Check(kit.Forage > forage0, "kit near eating parent gains Forage", "Forage=" + kit.Forage);
        Check(kit.Hunger < hunger0, "kit is fed while parent eats", "Hunger=" + kit.Hunger);
        Check(kit.State == "Eat", "kit state = Eat while parent eats (got " + kit.State + ")", "wrong state");

        // Kit follows a nearby parent (fresh stats so hunger can't confound).
        a.CurrentGoal = "Explore";
        kit.X = a.X + 12f; kit.Z = a.Z;
        kit.Energy = 100f; kit.Hunger = 20f; kit.Health = 100f;
        float d0 = V2.Distance(kit.Pos, a.Pos);
        KitBrain.Tick(sim, kit, 5f);
        Check(kit.State == "Follow", "kit follows a close parent (got " + kit.State + ")", "wrong state");
        for (int i = 0; i < 20; i++) KitBrain.Tick(sim, kit, 5f);
        float d1 = V2.Distance(kit.Pos, a.Pos);
        Check(d1 < d0, "kit closes distance to the parent", d0.ToString("F1") + " -> " + d1.ToString("F1"));

        // Kit hides when a gloom-maw is near.
        kit.Energy = 100f; kit.Hunger = 20f; kit.Health = 100f;
        sim.State.Entities.Add(new EntityState
        {
            Id = 9991, Kind = EntityKind.Predator, Name = "gloom-maw",
            X = kit.X + 5f, Z = kit.Z, Health = 100f, Behavior = "Hunt"
        });
        KitBrain.Tick(sim, kit, 5f);
        Check(kit.State == "Hide", "kit hides near a predator (got " + kit.State + ")", "no hide");

        // A starving kit weakens (but the test keeps it short of death).
        kit.Energy = 100f; kit.Hunger = 95f; kit.Health = 80f;
        for (int i = 0; i < 3; i++) KitBrain.Tick(sim, kit, 60f);
        Check(kit.IsAlive && kit.Health < 80f, "starving kit weakens", "Health=" + kit.Health);
    }

    private static void TestTaleCap()
    {
        Console.WriteLine("[tales]");
        var sim = Simulation.NewLife(9801);
        for (int i = 0; i < 15; i++)
            LineageSystem.MaybeDistillTale(sim, "Tale " + i, "Caution", 0.01f, "test " + i);
        int n = sim.State.Lineage.TalesThisGeneration(sim.State.Lineage.Generation);
        Check(n <= 12, "tales capped at 12 per generation (got " + n + ")", "cap exceeded");
        // Same title twice in a generation distills only once.
        LineageSystem.MaybeDistillTale(sim, "Tale 0", "Caution", 0.01f, "dup");
        int n2 = sim.State.Lineage.TalesThisGeneration(sim.State.Lineage.Generation);
        Check(n2 == n, "duplicate titles not re-distilled", n + " -> " + n2);
    }

    // -- 12. Aging -------------------------------------------------------------------

    private static void TestAging()
    {
        Console.WriteLine("[aging]");
        var sim = Simulation.NewLife(9201);
        var a = sim.State.Agent;
        a.LifespanYears = 16f;

        a.Age = 4f;
        Check(a.Stage == LifeStage.Adult, "adult stage at 4 years", "got " + a.Stage);
        Check(a.MaxEnergy == 100f && a.MaxSpeedFactor == 1f, "adult at full vigor", a.MaxEnergy + "/" + a.MaxSpeedFactor);

        a.Age = 0.5f;
        Check(a.Stage == LifeStage.Kit, "kit stage under 1 year", "got " + a.Stage);
        Check(a.MaxSpeedFactor < 1f, "kits slower than adults", "factor=" + a.MaxSpeedFactor);

        a.Age = 13f; // past 65% of 16 = 10.4
        Check(a.Stage == LifeStage.Elder, "elder stage past 65% lifespan", "got " + a.Stage);
        Check(a.MaxEnergy < 100f, "elder max light declines", "MaxEnergy=" + a.MaxEnergy);
        Check(a.MaxSpeedFactor < 1f, "elder slower", "factor=" + a.MaxSpeedFactor);

        // Foreshadowing: the dimming is never sudden.
        a.Age = 16f * 0.93f;
        a.VigorForeshadowed = false;
        LineageSystem.TickAging(sim, 3600f);
        Check(a.VigorForeshadowed, "vigor decline foreshadowed at 93% lifespan", "not flagged");
        bool fore = false;
        foreach (var e in sim.State.Journal.Entries)
            if (e.Text.Contains("my light thinning")) fore = true;
        Check(fore, "foreshadowing journaled", "missing");

        // Past lifespan, the daily chance eventually lands — peacefully.
        a.Age = 17.5f;
        a.VigorForeshadowed = true;
        int guard = 0;
        while (a.IsAlive && guard++ < 400) LineageSystem.TickAging(sim, 86400f); // up to 400 game-days
        Check(!a.IsAlive, "old age eventually dims the light", "still alive after 400 days");
        Check(sim.State.Lineage.Generation == 2, "old-age death still succeeds the line", "gen=" + sim.State.Lineage.Generation);
    }

    // -- 13. Sickness ----------------------------------------------------------------

    private static void TestSickness()
    {
        Console.WriteLine("[sickness]");
        // Untreated severity-1 dim-cough can kill.
        var sim = Simulation.NewLife(9301);
        var a = sim.State.Agent;
        a.Sickness = SicknessKind.DimCough;
        a.SicknessSeverity = 1f;
        a.Hunger = 90f; // starving, not resting: no cure
        a.CurrentGoal = "Explore";
        float glowBefore = a.Glow;
        for (int i = 0; i < 12; i++) LineageSystem.TickSickness(sim, 3600f); // 12 game-hours
        Check(!a.IsAlive || a.Health < 100f, "severe untreated sickness harms", "Health=" + a.Health);
        for (int i = 0; i < 60 && a.IsAlive; i++) LineageSystem.TickSickness(sim, 3600f);
        Check(!a.IsAlive, "untreated severity-1 sickness can kill", "survived");
        bool cause = false;
        foreach (var e in sim.State.Journal.Entries)
            if (e.Text.Contains("dimmed of the dim-cough")) cause = true;
        Check(cause, "sickness death logged with cause", "missing");

        // Rest + food cures slowly.
        var sim2 = Simulation.NewLife(9302);
        var b = sim2.State.Agent;
        b.Sickness = SicknessKind.LightFever;
        b.SicknessSeverity = 0.5f;
        b.Hunger = 30f;
        b.CurrentGoal = "Rest";
        float sev0 = b.SicknessSeverity;
        for (int i = 0; i < 5; i++) LineageSystem.TickSickness(sim2, 3600f);
        Check(b.SicknessSeverity < sev0, "rest + food cures sickness", sev0 + " -> " + b.SicknessSeverity);

        // Glow dims with sickness: the readable signal.
        var sim3 = Simulation.NewLife(9303);
        var c = sim3.State.Agent;
        c.Energy = 90f; c.Health = 90f;
        c.Sickness = SicknessKind.None; c.SicknessSeverity = 0f;
        float well = c.Glow;
        c.Sickness = SicknessKind.DimCough; c.SicknessSeverity = 0.8f;
        float sick = c.Glow;
        Check(sick < well, "sickness dims the glow (" + well.ToString("F2") + " -> " + sick.ToString("F2") + ")", "not dimmed");
        Check(c.Glow >= 0f && c.Glow <= 1f, "glow stays in 0..1", "Glow=" + c.Glow);

        // Contraction near a sick kindred (probabilistic; generous horizon).
        var sim4 = Simulation.NewLife(9304);
        var d = sim4.State.Agent;
        d.Health = 30f; // weak
        sim4.State.Weather = Weather.Rain;
        sim4.State.Entities.Add(new EntityState
        {
            Id = 9992, Kind = EntityKind.Kindred, Name = "Moth",
            X = d.X + 4f, Z = d.Z, Health = 100f, Behavior = "Wander",
            Sickness = SicknessKind.DimCough, SicknessSeverity = 0.4f
        });
        int days = 0;
        while (d.Sickness == SicknessKind.None && days++ < 200)
            LineageSystem.TickSickness(sim4, 86400f);
        Check(d.Sickness != SicknessKind.None, "sickness contracts near sick kindred in bad weather", "never contracted");
    }

    // -- 14. Bonding -----------------------------------------------------------------

    private static void TestBonding()
    {
        Console.WriteLine("[bonding]");
        var sim = Simulation.NewLife(9401);
        var a = sim.State.Agent;
        a.Age = 4f; // adult

        EntityState kindred = null;
        foreach (var e in sim.State.Entities)
            if (e.Kind == EntityKind.Kindred) { kindred = e; break; }
        Check(kindred != null, "test setup: kindred exists", "none");
        if (kindred == null) return;
        kindred.X = a.X + 2f; kindred.Z = a.Z;
        a.TargetEntityId = kindred.Id;
        var rec = sim.State.Social.GetPerson(kindred.Id);
        rec.Trust = 0.9f; // trusted

        var ctx = new BrainContext { Sim = sim, Ai = sim.AiRng, Ev = sim.EventRng, Now = sim.Now };
        var greet = new GreetKindredAction();
        for (int i = 0; i < 9; i++) greet.Update(ctx, 0.1f); // repeated greetings
        Check(a.Bond != null && a.Bond.PartnerId == kindred.Id, "repeated greetings form a bond", "no bond");
        Check(a.Bond.Strength > 0.7f, "bond strengthens past 0.7 (" + a.Bond.Strength.ToString("F2") + ")", "weak");
        bool bonded = false;
        foreach (var e in sim.State.Journal.Entries)
            if (e.Text.Contains("Something has changed between me and")) bonded = true;
        Check(bonded, "bond crossing journaled", "missing");

        // After time, a strong adult bond ripens into kits at the den.
        a.Bond.SinceStrongAt = 1f; // long ago (<=0 would reset to now)
        sim.State.ElapsedSeconds = 3f * 86400f + 1f;
        int kits0 = sim.State.Kits.Count;
        LineageSystem.TickBonding(sim);
        Check(sim.State.Kits.Count > kits0, "kits born from a strong bond", "none born");
        Check(sim.State.Kits.Count - kits0 >= 1 && sim.State.Kits.Count - kits0 <= 3, "litter of 1-3 kits",
            "got " + (sim.State.Kits.Count - kits0));
        Check(a.Bond.LitterBorn, "bond marked as having borne a litter", "flag unset");
        bool celebrated = false;
        foreach (var e in sim.State.Journal.Entries)
            if (e.Text.Contains("tumbled out of the den")) celebrated = true;
        Check(celebrated, "birth celebrated in the journal", "missing");
    }

    // -- 15. Colossi -----------------------------------------------------------------

    private static void TestColossi()
    {
        Console.WriteLine("[colossi]");
        var sim = Simulation.NewLife(9501);
        var col = sim.State.World.Colossi;
        Check(col.Count == 4, "four colossi placed (got " + col.Count + ")", "wrong count");
        int walkers = 0, isles = 0;
        foreach (var c in col)
        {
            if (c.Kind == ColossusKind.TreeWalker) walkers++;
            if (c.Kind == ColossusKind.SeedIsle) isles++;
        }
        Check(walkers == 2 && isles == 2, "two tree-walkers, two seed-isles", walkers + "/" + isles);

        // Drift over sim-days moves them (very slowly, but measurably).
        float x0 = col[0].X, z0 = col[0].Z;
        var rng = new SeededRandom(sim.State.Rng.Colossus);
        ColossusSystem.DriftQuietly(sim.State, rng, 3f * 86400f);
        Check(col[0].X != x0 || col[0].Z != z0, "colossi drift over days", "unmoved");

        // Deterministic: same seed, same drift.
        var simB = Simulation.NewLife(9501);
        var rngB = new SeededRandom(simB.State.Rng.Colossus);
        ColossusSystem.DriftQuietly(simB.State, rngB, 3f * 86400f);
        Check(Math.Abs(simB.State.World.Colossi[0].X - col[0].X) < 0.001f, "colossus drift deterministic", "diverged");

        // A near colossus is journaled (at most every 2 days).
        var sim2 = Simulation.NewLife(9502);
        var near = sim2.State.World.Colossi[0];
        near.X = sim2.State.Agent.X + 50f; near.Z = sim2.State.Agent.Z;
        near.LastNotedAt = -999999f;
        int journaled = 0;
        ColossusSystem.Tick(sim2.State, new SeededRandom(5u), 3600f, sim2.Now,
            (t, text, cat, sal) => { journaled++; });
        Check(journaled == 1, "near colossus journaled", "got " + journaled);
        ColossusSystem.Tick(sim2.State, new SeededRandom(5u), 3600f, sim2.Now + 3600f,
            (t, text, cat, sal) => { journaled++; });
        Check(journaled == 1, "colossus note throttled to every 2 days", "got " + journaled);
    }

    // -- 16. Lineage save round-trip --------------------------------------------------

    private static void TestLineageSaveRoundTrip()
    {
        Console.WriteLine("[lineage-save]");
        var sim = Simulation.NewLife(9601);
        var a = sim.State.Agent;
        LineageSystem.BirthLitter(sim, 2, -1);
        sim.State.Kits[0].Forage = 0.4f;
        LineageSystem.MaybeDistillTale(sim, "The Hollow Hive", "Caution", 0.04f, "test");
        a.Bond = new Bond { PartnerId = 7, Strength = 0.8f, SinceStrongAt = 100f };
        // Round-trip while the bonded generation-1 agent still lives.
        string json1 = SaveSystem.Save(sim.State);
        GameState loaded = SaveSystem.Load(json1);
        Check(loaded.Lineage.Generation == 1, "generation survives save/load", "got " + loaded.Lineage.Generation);
        Check(loaded.Kits.Count == 2, "kits survive save/load", "lost kits");
        Check(Math.Abs(loaded.Kits[0].Forage - 0.4f) < 0.001f, "kit learning survives save/load", "lost skills");
        Check(loaded.Agent.TalesKnown.Count > 0, "tales survive save/load", "lost tales");
        Check(loaded.Agent.Bond != null && loaded.Agent.Bond.Strength > 0.7f, "bond survives save/load", "lost bond");
        Check(loaded.World.Colossi.Count == 4, "colossi survive save/load", "lost colossi");
        // Succession, then round-trip the new generation.
        sim.KillAgent("dimmed the way evening dims, old and full of tales"); // -> generation 2
        string json2 = SaveSystem.Save(sim.State);
        GameState loaded2 = SaveSystem.Load(json2);
        Check(loaded2.Lineage.Generation == 2, "generation 2 survives save/load", "got " + loaded2.Lineage.Generation);
        Check(loaded2.Lineage.Chapters.Count == 1, "chapters survive save/load", "lost chapters");
        Check(loaded2.Agent.Bond == null, "heir starts unbonded (bond does not transfer)", "bond leaked");
        string json3 = SaveSystem.Save(loaded2);
        Check(json2 == json3, "lineage save->load->save byte-identical", "diverged");
        var simB = new Simulation(loaded2);
        simB.Step(2f);
        Check(simB.State.Agent.IsAlive, "generation 2 continues living after load", "dead");
    }

    // -- 17. Offline lineage safety ---------------------------------------------------

    private static void TestOfflineLineageSafety()
    {
        Console.WriteLine("[offline-lineage]");
        // An old agent left alone does not die of old age unfairly.
        var sim = Simulation.NewLife(9701);
        var a = sim.State.Agent;
        a.Age = a.LifespanYears - 0.01f;
        a.VigorForeshadowed = false;
        SaveSystem.ApplyOfflineProgress(sim.State, TimeSpan.FromDays(7), OfflineMode.QuietLife);
        Check(a.IsAlive, "offline aging never kills unfairly", "died while away");
        Check(a.Age <= a.LifespanYears, "offline age clamped at lifespan", "Age=" + a.Age);
        Check(a.VigorForeshadowed, "away aging foreshadows the dimming", "not flagged");

        // Kits are bounded while away: hungry but never starved to death.
        LineageSystem.BirthLitter(sim, 2, -1);
        foreach (var k in sim.State.Kits) { k.Hunger = 80f; k.Health = 60f; }
        SaveSystem.ApplyOfflineProgress(sim.State, TimeSpan.FromDays(7), OfflineMode.QuietLife);
        bool kitsOk = true;
        foreach (var k in sim.State.Kits)
            if (!k.IsAlive || k.Hunger > 85.5f || k.Health < 34f) kitsOk = false;
        Check(kitsOk, "offline kits bounded (hunger<=85, health floor)", "a kit suffered");

        // Sickness never kills while away.
        a.Sickness = SicknessKind.DimCough;
        a.SicknessSeverity = 0.9f;
        SaveSystem.ApplyOfflineProgress(sim.State, TimeSpan.FromDays(7), OfflineMode.QuietLife);
        Check(a.IsAlive, "offline sickness never kills", "died while away");
        Check(a.SicknessSeverity < 0.9f, "away sickness eases", "severity=" + a.SicknessSeverity);

        // Colossi drift while away.
        var sim2 = Simulation.NewLife(9702);
        float cx = sim2.State.World.Colossi[0].X;
        SaveSystem.ApplyOfflineProgress(sim2.State, TimeSpan.FromDays(3), OfflineMode.QuietLife);
        Check(sim2.State.World.Colossi[0].X != cx, "colossi drift during away time", "unmoved");
    }

}
