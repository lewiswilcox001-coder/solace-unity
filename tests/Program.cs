// Solace.Core test suite — plain console runner, no test framework dependency.
using System;
using System.Collections.Generic;
using System.IO;
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
        TestDreams();
        TestPlaytestFixes();
        TestPersonality();
        TestHerdBehaviorTemp();
        TestSeasons();
        TestNewPois();
        TestMilestones();
        TestSaveSlots();
        TestWeatherGameplay();
        TestEasterEggs();
        TestDailyVale();
        TestStats();
        TestChallengeModes();
        TestChallengeWiring();
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

    // -- 19. Prophetic dreams ----------------------------------------------------------

    private static void TestDreams()
    {
        Console.WriteLine("[dreams]");

        // Generation: a dream is 2-4 vocabulary elements + mood, first-person text.
        var sim = Simulation.NewLife(9801);
        var dream = DreamSystem.GenerateDream(sim);
        Check(dream.Elements.Count >= 2 && dream.Elements.Count <= 4,
            "dream holds 2-4 elements (got " + dream.Elements.Count + ")", "bad recipe");
        Check(dream.Text.StartsWith("I dreamed "),
            "dream written in the fox's voice", "'" + dream.Text + "'");
        Check(dream.Status == DreamStatus.Unresolved, "new dreams are unresolved", dream.Status.ToString());
        bool journaled = false, uncertain = false;
        foreach (var e in sim.State.Journal.Entries)
        {
            if (e.Category == JournalCategory.Dream && e.Text == dream.Text)
            {
                journaled = true;
                if (e.Certainty < 1f) uncertain = true;
            }
        }
        Check(journaled, "dream written to the journal", "missing entry");
        Check(uncertain, "dream journaled with uncertainty", "certainty wrong");

        // Determinism: same seed -> same dream, same seeding outcome.
        var simB = Simulation.NewLife(9801);
        var dreamB = DreamSystem.GenerateDream(simB);
        Check(dreamB.Text == dream.Text, "same seed -> same dream text",
            "'" + dreamB.Text + "' vs '" + dream.Text + "'");
        Check(dreamB.SeededPlaceId == dream.SeededPlaceId, "same seed -> same seeding outcome",
            dreamB.SeededPlaceId + " vs " + dream.SeededPlaceId);

        // Seeding: a forced dream grows a genuinely new, distant, valid place.
        var sim2 = Simulation.NewLife(9802);
        var crafted = new DreamState
        {
            Id = sim2.State.Dreams.NextId++,
            Time = 0f,
            Elements = new List<DreamElement> { DreamElement.Ruin, DreamElement.Cairn },
            Mood = DreamMood.Strange,
            Text = "I dreamed arches of chitin, empty as sky, stones piled by hands I could not see, and something in it was looking back at me."
        };
        sim2.State.Dreams.Dreams.Add(crafted);
        int poisBefore = sim2.State.World.Pois.Count;
        DreamSystem.SeedDreamPlace(sim2, crafted);
        Check(crafted.SeededPlaceId >= 0, "dream seeds a place", "nothing seeded");
        var poi = sim2.State.World.GetPoi(crafted.SeededPlaceId);
        Check(poi != null && sim2.State.World.Pois.Count == poisBefore + 1,
            "seeded place is genuinely new", "not added");
        Check(poi.DreamId == crafted.Id, "seeded place tagged with its dream", "dreamId=" + poi.DreamId);
        Check(!poi.Discovered, "seeded place starts undiscovered", "already discovered");
        Check(poi.Type == PoiType.RuinSite, "ruin dream grows a ruin site (got " + poi.Type + ")", "wrong type");
        PointOfInterest den = null;
        foreach (var p in sim2.State.World.Pois) if (p.Type == PoiType.Den) den = p;
        float dhx = poi.X - den.X, dhz = poi.Z - den.Z;
        Check(dhx * dhx + dhz * dhz >= 150f * 150f, "seeded place far from home", "too close");
        float adx = poi.X - sim2.State.Agent.X, adz = poi.Z - sim2.State.Agent.Z;
        Check(adx * adx + adz * adz >= 120f * 120f, "seeded place far from the sleeper", "too close");
        Check(!sim2.State.World.IsWater(poi.X, poi.Z), "seeded place on dry land", "in water");

        // Recognition: discovering the seeded place fulfills the dream.
        sim2.State.Agent.X = poi.X; sim2.State.Agent.Z = poi.Z;
        sim2.DiscoverPoi(poi.Id);
        Check(crafted.Status == DreamStatus.Fulfilled,
            "discovering the seeded place fulfills the dream", crafted.Status.ToString());
        Check(crafted.ResolvedPlaceId == poi.Id,
            "resolution points at the found place", "id=" + crafted.ResolvedPlaceId);
        bool recognized = false;
        foreach (var e in sim2.State.Journal.Entries)
            if (e.Category == JournalCategory.Dream && e.Text.Contains("This is the place from my dream"))
                recognized = true;
        Check(recognized, "recognition journaled in the fox's voice", "missing");

        // Loose matching: a natural place can rhyme with a dream too.
        var sim3 = Simulation.NewLife(9803);
        PointOfInterest cairn = null;
        foreach (var p in sim3.State.World.Pois)
            if (p.Type == PoiType.Cairn && !p.Discovered) { cairn = p; break; }
        var rhyme = new DreamState
        {
            Id = sim3.State.Dreams.NextId++,
            Time = 0f,
            Elements = new List<DreamElement> { DreamElement.Cairn, DreamElement.Overlook },
            Mood = DreamMood.Quiet,
            Text = "I dreamed stones piled by hands I could not see, the whole vale laid out below me like a pelt, and I was not afraid."
        };
        sim3.State.Dreams.Dreams.Add(rhyme);
        sim3.DiscoverPoi(cairn.Id);
        Check(rhyme.Status == DreamStatus.Fulfilled,
            "feature overlap fulfills without exact identity", rhyme.Status.ToString());

        // Mystery: some dreams never come true, and stay unresolved.
        var sim4 = Simulation.NewLife(9804);
        var mystery = new DreamState
        {
            Id = sim4.State.Dreams.NextId++,
            Time = 0f,
            Elements = new List<DreamElement> { DreamElement.Snow, DreamElement.TreeWalker },
            Mood = DreamMood.Vast,
            Text = "I dreamed white silence all the way up, a tree walking, slow as weather, and I felt very small, and glad of it."
        };
        sim4.State.Dreams.Dreams.Add(mystery);
        foreach (var p in new List<PointOfInterest>(sim4.State.World.Pois))
            if (!p.Discovered) sim4.DiscoverPoi(p.Id);
        Check(mystery.Status == DreamStatus.Unresolved,
            "unmatched dreams stay mysteries", mystery.Status.ToString());

        // Sleep gating: at most one dream roll per rest session.
        var sim5 = Simulation.NewLife(9805);
        var a5 = sim5.State.Agent;
        sim5.Brain.CurrentActionName = "Rest";
        a5.CurrentActivity = "resting in the ember-hollow";
        a5.LastDreamRolledAt = -9999f;
        int dreamsBefore = sim5.State.Dreams.Dreams.Count;
        DreamSystem.TickSleep(sim5);
        Check(a5.LastDreamRolledAt == sim5.Now, "sleep roll stamps the session", "not stamped");
        int afterFirst = sim5.State.Dreams.Dreams.Count;
        DreamSystem.TickSleep(sim5);
        Check(sim5.State.Dreams.Dreams.Count == afterFirst, "no second roll in the same rest", "rolled again");
        Check(afterFirst - dreamsBefore <= 1, "at most one dream per rest", "too many");
        sim5.Brain.CurrentActionName = "Explore";
        a5.LastDreamRolledAt = -9999f;
        int beforeExplore = sim5.State.Dreams.Dreams.Count;
        DreamSystem.TickSleep(sim5);
        Check(sim5.State.Dreams.Dreams.Count == beforeExplore, "no dreams while awake", "dreamed awake");

        // Save round-trip preserves dreams byte-identically.
        var sim6 = Simulation.NewLife(9806);
        DreamSystem.GenerateDream(sim6);
        DreamSystem.GenerateDream(sim6);
        var loaded = SaveSystem.Load(SaveSystem.Save(sim6.State));
        Check(loaded.Dreams.Dreams.Count == sim6.State.Dreams.Dreams.Count,
            "dreams survive save/load",
            loaded.Dreams.Dreams.Count + " vs " + sim6.State.Dreams.Dreams.Count);
        bool textsMatch = true;
        for (int i = 0; i < loaded.Dreams.Dreams.Count; i++)
            if (loaded.Dreams.Dreams[i].Text != sim6.State.Dreams.Dreams[i].Text) textsMatch = false;
        Check(textsMatch, "dream text byte-identical after load", "diverged");
    }

    // -- 20. First-playtest fixes ------------------------------------------------------

    private static void TestPlaytestFixes()
    {
        Console.WriteLine("[playtest-fixes]");

        // Bug 1: greeting spam — a greeted kindred must not be re-targeted during
        // its 8s chat cooldown, and becomes greetable again once it expires.
        {
            var sim = Simulation.NewLife(4242);
            var agent = sim.State.Agent;
            sim.State.Entities.RemoveAll(e => e.Kind == EntityKind.Kindred);
            var kin = new EntityState
            {
                Id = 9001, Kind = EntityKind.Kindred, Name = "Ember",
                X = agent.X + 5f, Z = agent.Z, Health = 100f, Behavior = "Wander"
            };
            sim.State.Entities.Add(kin);
            var ctx = new BrainContext { Sim = sim, Ai = sim.AiRng, Ev = sim.EventRng, Now = sim.Now };
            var greet = new GreetKindredAction();

            var first = greet.TargetKindred(ctx);
            Check(first == kin, "greet targets nearby kindred", "no target found");

            // Simulate what GreetKindredAction.Update does after a greeting.
            kin.Behavior = "Greeted";
            kin.StateTimer = 8f;
            var second = greet.TargetKindred(ctx);
            Check(second == null, "greeted kindred skipped during cooldown",
                second != null ? "re-targeted " + second.Name : "null ok");

            // Cooldown expired: greetable again.
            kin.Behavior = "Wander";
            kin.StateTimer = 0f;
            var third = greet.TargetKindred(ctx);
            Check(third == kin, "kindred greetable again after cooldown", "still skipped");
        }

        // Bug 2b: a starving agent with food nearby must choose Eat over Rest.
        {
            var sim = Simulation.NewLife(31337);
            var agent = sim.State.Agent;
            PointOfInterest bush = null;
            foreach (var p in sim.State.World.Pois)
                if (p.Type == PoiType.GlowberryBush && p.Stock > 0) { bush = p; break; }
            Check(bush != null, "test setup: berry bush exists", "no bush placed");
            if (bush != null)
            {
                agent.KnownPoiIds.Add(bush.Id);
                agent.X = bush.X + 6f;
                agent.Z = bush.Z;
                agent.Hunger = 98f;   // critical, as in the playtest death
                agent.Energy = 22f;   // low enough that Rest is a live competitor
                agent.Thirst = 10f;
                agent.Health = 80f;
                agent.Influences.Clear();
                sim.State.Entities.RemoveAll(e => e.Kind != EntityKind.Kindred);
                foreach (var e in sim.State.Entities) { e.X += 1000f; e.Z += 1000f; }

                var ctx = new BrainContext { Sim = sim, Ai = sim.AiRng, Ev = sim.EventRng, Now = sim.Now };
                string choice = sim.Brain.Decide(ctx);
                Check(choice == "Eat", "starving agent near food chooses Eat over Rest (got " + choice + ")",
                    "trace: " + sim.Brain.LastDecisionTrace.Reason);
            }
        }

        // Bug 2a: starvation health drain must give a real recovery window.
        // 120 game-seconds of starvation from full health should cost well under
        // half the old rate's ~30 damage.
        {
            var sim = Simulation.NewLife(7777);
            var s = sim.State;
            s.Agent.Hunger = 98f;
            s.Agent.Thirst = 10f;
            s.Agent.Energy = 90f;
            s.Agent.Health = 100f;
            s.Agent.KnownPoiIds.Clear();
            s.Agent.Influences.Clear();
            sim.State.Inventory.Bread = 0;
            sim.State.Entities.RemoveAll(e => e.Kind != EntityKind.Kindred);

            // Exactly 120 game-seconds of starvation (2 real seconds x60 timescale).
            sim.Step(2f);

            float lost = 100f - s.Agent.Health;
            Check(s.Agent.IsAlive, "agent survives 2 game-minutes of starvation", "died");
            Check(lost < 12f, "starvation drain is gradual (" + lost.ToString("F1") + " dmg / 2 min)",
                "lost " + lost.ToString("F1"));
        }
    }


    // -- 22. Personality ------------------------------------------------------------

    private static void TestPersonality()
    {
        Console.WriteLine("[personality]");
        var rng = new SeededRandom(4242);

        // 1. Inherit blends both parents: child traits fall between the parents'.
        var father = Personality.Neutral(); father.Caution = 0.9f; father.Curiosity = 0.1f;
        var mother = Personality.Neutral(); mother.Caution = 0.1f; mother.Curiosity = 0.9f;
        var child = Personality.Inherit(father, mother, rng);
        Check(child.Caution > 0.2f && child.Caution < 0.8f,
            "inherited caution blends parents (" + child.Caution.ToString("F2") + ")", "out of range");
        Check(child.Curiosity > 0.2f && child.Curiosity < 0.8f,
            "inherited curiosity blends parents (" + child.Curiosity.ToString("F2") + ")", "out of range");
        Check(child.Sociability >= 0.05f && child.Sociability <= 0.95f,
            "inherited traits stay in bounds", "Sociability=" + child.Sociability);

        // 2. Inherit is deterministic for a given seed.
        var c2 = Personality.Inherit(father, mother, new SeededRandom(4242));
        Check(Math.Abs(c2.Caution - child.Caution) < 1e-6f, "inheritance deterministic", "differed");

        // 3. Epithets read from the strongest deviation.
        var bold = Personality.Neutral(); bold.Caution = 0.1f;
        Check(bold.Epithet() == "the Bold", "low caution -> the Bold", "got '" + bold.Epithet() + "'");
        var timid = Personality.Neutral(); timid.Caution = 0.9f;
        Check(timid.Epithet() == "the Timid", "high caution -> the Timid", "got '" + timid.Epithet() + "'");
        var curious = Personality.Neutral(); curious.Curiosity = 0.9f;
        Check(curious.Epithet() == "the Curious", "high curiosity -> the Curious", "got '" + curious.Epithet() + "'");
        var plain = Personality.Neutral();
        Check(plain.Epithet() == "", "neutral -> no epithet", "got '" + plain.Epithet() + "'");

        // 4. Kits inherit the father's temperament at birth (blended with mother's).
        var sim = Simulation.NewLife(777);
        sim.State.Agent.Traits.Caution = 0.85f; // a timid parent
        sim.State.Agent.Traits.Sociability = 0.9f;
        LineageSystem.BirthLitter(sim, 1, -1);
        var kit = sim.State.Kits[0];
        Check(kit.Traits.Caution > 0.3f, "kit inherits father's caution (" + kit.Traits.Caution.ToString("F2") + ")",
            "too low — inheritance broken");
        Check(kit.Traits.Sociability > 0.3f, "kit inherits father's sociability", "too low");

        // 5. Succession: the heir keeps the kit's temperament (continuation, not clone).
        kit.Age = 1.2f; kit.Name = "HeirKit";
        sim.KillAgent("test cause");
        var heir = sim.State.Agent;
        Check(Math.Abs(heir.Traits.Caution - kit.Traits.Caution) < 0.35f,
            "heir temperament continues kit's (" + heir.Traits.Caution.ToString("F2") + " vs " +
            kit.Traits.Caution.ToString("F2") + ")", "diverged too far");
        Check(heir.Traits.Sociability > 0.05f, "heir has nonzero sociability (was zero-bug)",
            "Sociability=" + heir.Traits.Sociability);

        // 6. The succession journal names the heir with her epithet.
        var sim2 = Simulation.NewLife(778);
        LineageSystem.BirthLitter(sim2, 1, -1);
        var kit2 = sim2.State.Kits[0];
        kit2.Age = 1.3f; kit2.Name = "BoldKit";
        kit2.Traits.Caution = 0.05f; kit2.Traits.Curiosity = 0.5f; // unmistakably bold
        kit2.Traits.Sociability = 0.5f; kit2.Traits.Patience = 0.5f;
        kit2.Traits.Pride = 0.5f; kit2.Traits.Compassion = 0.5f;
        sim2.KillAgent("test cause");
        bool epithetLine = false;
        foreach (var e in sim2.State.Journal.Entries)
            if (e.Category == JournalCategory.Chapter && e.Text.Contains("BoldKit the Bold"))
                epithetLine = true;
        Check(epithetLine, "succession journal: 'BoldKit the Bold'", "epithet missing from chapter line");
    }

    // -- deer herd behavior --------------------------------------------------------

    private static void TestHerdBehaviorTemp()
    {
        Console.WriteLine("[herd]");
        var sim = Simulation.NewLife(777);
        // 4 deer in a loose group.
        sim.State.Entities.Clear();
        for (int i = 0; i < 4; i++)
        {
            sim.State.Entities.Add(new EntityState
            {
                Id = 100 + i, Kind = EntityKind.Deer, Name = "deer",
                X = i * 8f, Z = 0f, Health = 100f, Behavior = "Graze",
                HomeX = i * 8f, HomeZ = 0f, TargetX = i * 8f, TargetZ = 0f,
                StateTimer = 30f
            });
        }
        sim.State.Agent.X = 500f; sim.State.Agent.Z = 500f; // don't spook them

        var ctx = new EntityContext
        {
            World = sim.State.World, Agent = sim.State.Agent,
            Entities = sim.State.Entities, Kits = sim.State.Kits,
            Now = 0f, Rng = new SeededRandom(999u)
        };
        float dt = 1f / 30f;

        // 1. Grazing cohesion: herd tightens over 60s.
        float startSpread = 0f;
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
                startSpread += V2.Distance(sim.State.Entities[i].Pos, sim.State.Entities[j].Pos);
        for (int s = 0; s < 1800; s++)
            foreach (var e in sim.State.Entities) EntityBehaviors.Step(e, ctx, dt);
        float endSpread = 0f;
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
                endSpread += V2.Distance(sim.State.Entities[i].Pos, sim.State.Entities[j].Pos);
        Check(endSpread < startSpread, "grazing deer cluster (spread " +
            startSpread.ToString("F0") + " -> " + endSpread.ToString("F0") + ")",
            "spread grew");

        // 2. Contagious flight: spook one, herd joins within 3s.
        sim.State.Entities[0].Behavior = "Flee";
        sim.State.Entities[0].StateTimer = 5f;
        sim.State.Entities[0].Facing = 0f;
        for (int s = 0; s < 90; s++)
            foreach (var e in sim.State.Entities) EntityBehaviors.Step(e, ctx, dt);
        int fleeing = 0;
        foreach (var e in sim.State.Entities)
            if (e.Behavior == "Flee") fleeing++;
        Check(fleeing >= 3, "contagious flight spreads (" + fleeing + "/4 fleeing)",
            "only " + fleeing + "/4");

        // 3. Group flight coherence.
        float hx = 0f, hz = 0f;
        foreach (var e in sim.State.Entities)
            if (e.Behavior == "Flee") { hx += MathF.Sin(e.Facing); hz += MathF.Cos(e.Facing); }
        float coherence = (float)Math.Sqrt(hx * hx + hz * hz) / Math.Max(1, fleeing);
        Check(coherence > 0.5f, "fleeing herd is coherent (" + coherence.ToString("F2") + ")",
            "scattered");
    }

    // -- seasons ---------------------------------------------------------------

    private static void TestSeasons()
    {
        Console.WriteLine("[seasons]");
        var probe = new GameState();

        // 1. Four 20-day seasons, spring first, then the year turns over.
        probe.ElapsedSeconds = 0f;
        Check(SeasonSystem.Current(probe) == Season.Spring, "t=0 is spring",
            "got " + SeasonSystem.Current(probe));
        probe.ElapsedSeconds = 20f * 86400f;
        Check(SeasonSystem.Current(probe) == Season.Summer, "day 20 is summer",
            "got " + SeasonSystem.Current(probe));
        probe.ElapsedSeconds = 40f * 86400f;
        Check(SeasonSystem.Current(probe) == Season.Autumn, "day 40 is autumn",
            "got " + SeasonSystem.Current(probe));
        probe.ElapsedSeconds = 60f * 86400f;
        Check(SeasonSystem.Current(probe) == Season.Winter, "day 60 is winter",
            "got " + SeasonSystem.Current(probe));
        probe.ElapsedSeconds = 80f * 86400f;
        Check(SeasonSystem.Current(probe) == Season.Spring, "day 80 is spring again",
            "got " + SeasonSystem.Current(probe));

        // 2. Winter nights are long, summer nights short. 19:00 is night in
        // winter (dusk 17:30) but day in summer (dusk 21:30).
        // TimeOfDay = (9 + elapsed/3600) % 24 → elapsed = N*86400 + 10*3600 gives 19:00.
        probe.ElapsedSeconds = 65f * 86400f + 36000f; // winter, 19:00
        Check(probe.IsNight, "19:00 in winter is night", "day at 19:00 in winter");
        probe.ElapsedSeconds = 25f * 86400f + 36000f; // summer, 19:00
        Check(!probe.IsNight, "19:00 in summer is day", "night at 19:00 in summer");
        probe.ElapsedSeconds = 65f * 86400f + 82800f; // winter, 08:00 (9+23=32%24=8)
        Check(!probe.IsNight, "08:00 in winter is day", "night at 08:00 in winter");

        // 3. Food abundance ordering: summer feasts, winter starves.
        Check(SeasonSystem.FoodAbundance(Season.Summer) > SeasonSystem.FoodAbundance(Season.Spring),
            "summer more abundant than spring", "ordering broken");
        Check(SeasonSystem.FoodAbundance(Season.Spring) > SeasonSystem.FoodAbundance(Season.Autumn),
            "spring more abundant than autumn", "ordering broken");
        Check(SeasonSystem.FoodAbundance(Season.Autumn) > SeasonSystem.FoodAbundance(Season.Winter),
            "autumn more abundant than winter", "ordering broken");
        Check(SeasonSystem.FoodAbundance(Season.Winter) < 1f,
            "winter is scarcity (< 1x regrowth)", "winter too generous");

        // 4. Energy pressure: winter taxes, spring vitalizes.
        Check(SeasonSystem.HourlyEnergyDelta(Season.Winter) < 0f,
            "winter levies a cold tax", "no winter tax");
        Check(SeasonSystem.HourlyEnergyDelta(Season.Spring) > 0f,
            "spring grants vitality", "no spring bonus");
        Check(SeasonSystem.HourlyEnergyDelta(Season.Summer) == 0f,
            "summer is neutral", "summer drifts energy");

        // 5. The turning year is journaled.
        var sim = Simulation.NewLife(999);
        sim.State.ElapsedSeconds = 20f * 86400f - 5f; // 5 game-seconds before summer
        sim.Step(30f); // crosses the boundary (TimeScale 2 → 60 game-seconds)
        Check(SeasonSystem.Current(sim.State) == Season.Summer, "sim crossed into summer",
            "got " + SeasonSystem.Current(sim.State));
        bool chronicled = false;
        foreach (var e in sim.State.Journal.Entries)
            if (e.Text.Contains("Summer has settled")) chronicled = true;
        Check(chronicled, "season turn is chronicled in the journal", "no season entry");

        // 6. Nature's timing: a spring newborn is blessed (+15 energy) vs winter.
        // Comparative, same seed, noon (day in both seasons), agent topped up
        // so the seasonal tax can't change AI decisions mid-test.
        var simSpring = Simulation.NewLife(4242);
        simSpring.State.ElapsedSeconds = 5f * 86400f + 3f * 3600f; // spring, 12:00
        var simWinter = Simulation.NewLife(4242);
        simWinter.State.ElapsedSeconds = 65f * 86400f + 3f * 3600f; // winter, 12:00
        var kitS = new KitState { Id = 9001, Name = "Testkit", Age = 0f, Energy = 50f,
                                  Health = 80f, Hunger = 25f, IsAlive = true,
                                  X = simSpring.State.Agent.X + 2f, Z = simSpring.State.Agent.Z };
        var kitW = new KitState { Id = 9001, Name = "Testkit", Age = 0f, Energy = 50f,
                                  Health = 80f, Hunger = 25f, IsAlive = true,
                                  X = simWinter.State.Agent.X + 2f, Z = simWinter.State.Agent.Z };
        simSpring.State.Kits.Add(kitS);
        simWinter.State.Kits.Add(kitW);
        simSpring.State.Agent.Energy = 100f;
        simWinter.State.Agent.Energy = 100f;
        simSpring.Step(2000f); // 4000 game-seconds: crosses one hour mark → blessing fires
        simWinter.Step(2000f);
        Check(kitS.Health > kitW.Health + 5f,
            "spring newborn blessed (spring health " + kitS.Health.ToString("F1") +
            " vs winter " + kitW.Health.ToString("F1") + ")",
            "no spring blessing applied");

        // 7. Determinism: same seed → same seasons, same chronicle.
        var simA = Simulation.NewLife(7777);
        var simB = Simulation.NewLife(7777);
        simA.State.ElapsedSeconds = 30f * 86400f;
        simB.State.ElapsedSeconds = 30f * 86400f;
        simA.Step(10f); simB.Step(10f);
        Check(SeasonSystem.Current(simA.State) == SeasonSystem.Current(simB.State),
            "seasons are deterministic", "diverged");
        Check(simA.State.Journal.Count == simB.State.Journal.Count,
            "season chronicle is deterministic", "journal diverged");
    }

    // -- new discoverable POIs ------------------------------------------------------

    private static void TestNewPois()
    {
        Console.WriteLine("[newpois]");
        var w1 = WorldGenerator.Generate(new WorldConfig { Seed = 20261006 });
        var w2 = WorldGenerator.Generate(new WorldConfig { Seed = 20261006 });

        // 1. The three new kinds are placed.
        int caves = 0, springs = 0, logs = 0;
        foreach (var p in w1.Pois)
        {
            if (p.Type == PoiType.CrystalCave) caves++;
            if (p.Type == PoiType.HotSpring) springs++;
            if (p.Type == PoiType.HollowLog) logs++;
            // New POIs must be on dry land.
            if (p.Type == PoiType.CrystalCave || p.Type == PoiType.HotSpring || p.Type == PoiType.HollowLog)
                Check(!w1.IsWater(p.X, p.Z), "new POI on dry land (" + p.Type + ")", "in water");
        }
        Check(caves >= 1, "crystal caves placed (" + caves + ")", "none placed");
        Check(springs >= 1, "hot springs placed (" + springs + ")", "none placed");
        Check(logs >= 1, "hollow logs placed (" + logs + ")", "none placed");

        // 2. Deterministic: same seed → same new POIs.
        bool same = w1.Pois.Count == w2.Pois.Count;
        if (same)
            for (int i = 0; i < w1.Pois.Count; i++)
            {
                var p = w1.Pois[i]; var q = w2.Pois[i];
                if (p.Type != q.Type || p.X != q.X || p.Z != q.Z || p.Name != q.Name) { same = false; break; }
            }
        Check(same, "new POIs are deterministic", "diverged across same seed");

        // 3. Display names and true names.
        foreach (var p in w1.Pois)
        {
            if (p.Type == PoiType.CrystalCave)
                Check(p.DisplayName == "a crystal cave" || p.LearnedName, "crystal cave display name", p.DisplayName);
            if (p.Type == PoiType.HotSpring)
                Check(p.DisplayName == "a hot spring" || p.LearnedName, "hot spring display name", p.DisplayName);
            if (p.Type == PoiType.HollowLog)
                Check(p.DisplayName == "a hollow log" || p.LearnedName, "hollow log display name", p.DisplayName);
        }

        // 4. Discovery writes evocative journal entries.
        var sim = Simulation.NewLife(31337);
        PointOfInterest cave = null, spring = null, log = null;
        foreach (var p in sim.State.World.Pois)
        {
            if (p.Type == PoiType.CrystalCave && cave == null) cave = p;
            if (p.Type == PoiType.HotSpring && spring == null) spring = p;
            if (p.Type == PoiType.HollowLog && log == null) log = p;
        }
        if (cave != null)
        {
            sim.DiscoverPoi(cave.Id);
            bool found = false;
            foreach (var e in sim.State.Journal.Entries)
                if (e.Text.Contains("living glass") || e.Text.Contains("crystal")) found = true;
            Check(found, "crystal cave discovery is journaled", "no journal entry");
            Check(cave.Discovered, "crystal cave marked discovered", "not marked");
        }
        if (spring != null)
        {
            sim.DiscoverPoi(spring.Id);
            bool found = false;
            foreach (var e in sim.State.Journal.Entries)
                if (e.Text.Contains("warm") && e.Text.Contains("steam")) found = true;
            Check(found, "hot spring discovery is journaled", "no journal entry");
        }
        if (log != null)
        {
            sim.DiscoverPoi(log.Id);
            bool found = false;
            foreach (var e in sim.State.Journal.Entries)
                if (e.Text.Contains("kits will love")) found = true;
            Check(found, "hollow log discovery is journaled", "no journal entry");
        }

        // 5. Rest system recognizes the new shelters.
        Check(RestAction.ShelterComfort(PoiType.HotSpring) > RestAction.ShelterComfort(PoiType.EmberHollow),
            "hot spring is the comfiest shelter", "comfort ordering wrong");
        Check(RestAction.ShelterRestQuality(PoiType.HotSpring) > RestAction.ShelterRestQuality(PoiType.EmberHollow),
            "hot spring gives best rest quality", "quality ordering wrong");
        Check(RestAction.ShelterComfort(PoiType.CrystalCave) > RestAction.ShelterComfort(PoiType.Den),
            "crystal cave beats the den for comfort", "comfort ordering wrong");
        Check(RestAction.ShelterRestQuality(PoiType.HollowLog) > 1f,
            "hollow log is a valid rest spot", "not recognized");

        // 6. Dream vocabulary covers the new places.
        var crystalFrags = DreamVocabulary.Fragments(DreamElement.Crystal);
        Check(crystalFrags != null && crystalFrags.Length > 0, "crystal dream fragments exist", "none");
        Check(DreamVocabulary.IsStructural(DreamElement.Crystal), "crystal dreams can seed places", "not structural");
        Check(DreamVocabulary.TypeForElement(DreamElement.Crystal) == PoiType.CrystalCave,
            "crystal dreams seed crystal caves", "wrong mapping");
        var caveElements = DreamVocabulary.ElementsFor(PoiType.CrystalCave);
        Check(caveElements.Contains(DreamElement.Crystal), "crystal cave carries crystal dreams", "missing");
        var springElements = DreamVocabulary.ElementsFor(PoiType.HotSpring);
        Check(springElements.Count > 0, "hot spring carries dream elements", "none");
        var logElements = DreamVocabulary.ElementsFor(PoiType.HollowLog);
        Check(logElements.Count > 0, "hollow log carries dream elements", "none");

        // 7. New POI types survive a save/load round-trip.
        var json = w1.ToJson();
        var w3 = WorldData.FromJson(json);
        int caves3 = 0, springs3 = 0, logs3 = 0;
        foreach (var p in w3.Pois)
        {
            if (p.Type == PoiType.CrystalCave) caves3++;
            if (p.Type == PoiType.HotSpring) springs3++;
            if (p.Type == PoiType.HollowLog) logs3++;
        }
        Check(caves3 == caves && springs3 == springs && logs3 == logs,
            "new POIs survive save/load", "lost in round-trip");
    }

    private static void TestMilestones()
    {
        Console.WriteLine("[milestones]");

        // 1. Definitions: 37 milestones (30 + 4 secrets + 3 daily), unique IDs.
        Check(MilestoneDefs.All.Count == 37, "37 milestone definitions",
            "got " + MilestoneDefs.All.Count);
        var ids = new HashSet<string>();
        foreach (var d in MilestoneDefs.All) ids.Add(d.Id);
        Check(ids.Count == 37, "all milestone IDs unique", "duplicates found");
        Check(MilestoneDefs.ById("first_dawn") != null, "ById finds first_dawn", "null");
        Check(MilestoneDefs.ById("nope") == null, "ById returns null for unknown", "found");

        // 2. Survival: time thresholds.
        var sim = Simulation.NewLife(4242);
        var newly = MilestoneSystem.Tick(sim);
        Check(!sim.State.Milestones.IsUnlocked("first_dawn"),
            "no first_dawn at t=0", "unlocked early");
        sim.State.ElapsedSeconds = 86400; // 1 day
        newly = MilestoneSystem.Tick(sim);
        Check(sim.State.Milestones.IsUnlocked("first_dawn"),
            "first_dawn unlocks at 1 day", "not unlocked");
        Check(newly.Contains("first_dawn"), "Tick returns newly unlocked",
            "missing from return");
        sim.State.ElapsedSeconds = 86400 * 7;
        MilestoneSystem.Tick(sim);
        Check(sim.State.Milestones.IsUnlocked("week_survivor"),
            "week_survivor at 7 days", "not unlocked");

        // 3. Discovery: POI counts.
        var sim2 = Simulation.NewLife(4243);
        int marked = 0;
        foreach (var p in sim2.State.World.Pois)
        {
            if (marked < 5) { p.Discovered = true; marked++; }
        }
        MilestoneSystem.Tick(sim2);
        Check(sim2.State.Milestones.IsUnlocked("wanderer"),
            "wanderer at 5 POIs", "not unlocked");
        Check(!sim2.State.Milestones.IsUnlocked("explorer"),
            "no explorer at 5 POIs", "unlocked early");

        // 4. Lineage: generation and tales.
        var sim3 = Simulation.NewLife(4244);
        sim3.State.Lineage.Generation = 5;
        MilestoneSystem.Tick(sim3);
        Check(sim3.State.Milestones.IsUnlocked("second_gen"),
            "second_gen at gen 5", "not unlocked");
        Check(sim3.State.Milestones.IsUnlocked("dynasty"),
            "dynasty at gen 5", "not unlocked");
        Check(!sim3.State.Milestones.IsUnlocked("immortal_line"),
            "no immortal_line at gen 5", "unlocked early");

        // 5. Combat: kill edge detection.
        var sim4 = Simulation.NewLife(4245);
        sim4.State.Entities.Add(new EntityState
        {
            Id = 900, Kind = EntityKind.Predator, Name = "gloom-maw",
            X = 0f, Z = 0f, Health = 0f, Behavior = "Dead"
        });
        MilestoneSystem.Tick(sim4);
        Check(sim4.State.Milestones.TotalKills == 1,
            "kill counted via dead predator", "got " + sim4.State.Milestones.TotalKills);
        Check(sim4.State.Milestones.IsUnlocked("first_blood"),
            "first_blood on kill", "not unlocked");
        // Same corpse twice must not double-count.
        MilestoneSystem.Tick(sim4);
        Check(sim4.State.Milestones.TotalKills == 1,
            "no double-count on re-tick", "got " + sim4.State.Milestones.TotalKills);

        // 6. Kit birth edge detection.
        var sim5 = Simulation.NewLife(4246);
        sim5.State.Kits.Add(new KitState { Id = 501, Name = "Kit" });
        MilestoneSystem.Tick(sim5);
        Check(sim5.State.Milestones.TotalKitsBorn == 1,
            "kit birth counted", "got " + sim5.State.Milestones.TotalKitsBorn);
        Check(sim5.State.Milestones.IsUnlocked("new_blood"),
            "new_blood on first kit", "not unlocked");

        // 7. Seasons: completion tracking.
        var sim6 = Simulation.NewLife(4247);
        sim6.State.ElapsedSeconds = 0; // spring
        MilestoneSystem.Tick(sim6);
        sim6.State.ElapsedSeconds = 21 * 86400; // summer now
        MilestoneSystem.Tick(sim6);
        Check(sim6.State.Milestones.SeasonsSurvived.Contains("Spring"),
            "spring marked survived on transition", "missing");
        Check(sim6.State.Milestones.IsUnlocked("spring_survivor"),
            "spring_survivor unlocked", "not unlocked");

        // 8. Dreams.
        var sim7 = Simulation.NewLife(4248);
        sim7.State.Dreams.Dreams.Add(new DreamState
        {
            Id = 1, Status = DreamStatus.Unresolved, Text = "a test dream"
        });
        MilestoneSystem.Tick(sim7);
        Check(sim7.State.Milestones.IsUnlocked("dreamer"),
            "dreamer on first dream", "not unlocked");

        // 9. Save/load round-trip preserves milestones.
        var sim8 = Simulation.NewLife(4249);
        sim8.State.ElapsedSeconds = 86400 * 8;
        MilestoneSystem.Tick(sim8);
        string json = SaveSystem.Save(sim8.State);
        var loaded = SaveSystem.Load(json);
        Check(loaded.Milestones.IsUnlocked("first_dawn"),
            "milestones survive save/load", "lost");
        Check(loaded.Milestones.IsUnlocked("week_survivor"),
            "week_survivor survives save/load", "lost");
        Check(loaded.Milestones.UnlockedCount == sim8.State.Milestones.UnlockedCount,
            "unlock count matches after load", "mismatch");

        // 10. Old saves (no milestones key) load with empty state.
        var sim9 = Simulation.NewLife(4250);
        var ms = new MilestoneState();
        Check(ms.UnlockedCount == 0, "fresh MilestoneState is empty", "not empty");
        Check(!ms.IsUnlocked("first_dawn"), "nothing unlocked initially", "unlocked");
    }

    // -- save slots ---------------------------------------------------------------

    private static void TestSaveSlots()
    {
        Console.WriteLine("[slots]");
        string dir = Path.Combine(Path.GetTempPath(), "solace-slots-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 1. Save a life into slot 0; the menu can list it.
            var sim = Simulation.NewLife(4242);
            sim.Step(2f);
            string foxName = sim.State.Agent.Name;
            SaveSlots.SaveSlot(dir, 0, sim.State);
            var infos = SaveSlots.ListSlots(dir);
            Check(infos.Length == SaveSlots.MaxSlots, "three slots listed", "got " + infos.Length);
            Check(infos[0].Exists, "slot 0 exists after save", "missing");
            Check(infos[0].Seed == 4242, "slot 0 seed matches", "got " + infos[0].Seed);
            Check(infos[0].FoxName == foxName && foxName.Length > 0, "slot 0 fox name matches", "got '" + infos[0].FoxName + "'");
            Check(infos[0].Generation == sim.State.Lineage.Generation, "slot 0 generation matches", "mismatch");
            Check(infos[0].PlaytimeSeconds >= 0, "slot 0 playtime recorded", "negative");
            Check(infos[0].LastSavedUtcTicks > 0, "slot 0 save timestamp recorded", "zero");
            Check(!infos[1].Exists && !infos[2].Exists, "slots 1-2 start empty", "not empty");

            // 2. Second save rotates the first into the backup.
            sim.Step(2f);
            SaveSlots.SaveSlot(dir, 0, sim.State);
            Check(File.Exists(Path.Combine(dir, "slot0.bak.json")), "backup written on re-save", "missing");
            infos = SaveSlots.ListSlots(dir);
            Check(infos[0].HasBackup, "slot info reports backup", "false");

            // 3. Slot JSON round-trips through the normal loader.
            string json = SaveSlots.LoadSlotJson(dir, 0);
            var loaded = SaveSystem.Load(json);
            Check(loaded.Seed == 4242, "slot json loads with right seed", "got " + loaded.Seed);

            // 4. Away time is ~0 right after saving.
            Check(SaveSlots.AwayTime(dir, 0).TotalSeconds < 120, "away time ~0 after save", "large");

            // 5. Independent slots + delete.
            var sim2 = Simulation.NewLife(7777);
            SaveSlots.SaveSlot(dir, 2, sim2.State);
            Check(SaveSlots.ListSlots(dir)[2].Seed == 7777, "slot 2 independent", "wrong seed");
            Check(SaveSlots.LoadSlotJson(dir, 1) == null, "empty slot loads null", "non-null");
            SaveSlots.DeleteSlot(dir, 2);
            Check(!SaveSlots.ListSlots(dir)[2].Exists, "deleted slot reads empty", "still exists");
            Check(!File.Exists(Path.Combine(dir, "slot2.json")), "deleted slot file gone", "still there");

            // 6. Legacy single-save migrates into slot 0 and keeps loading.
            string dir2 = Path.Combine(Path.GetTempPath(), "solace-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir2);
            try
            {
                var sim3 = Simulation.NewLife(31337);
                File.WriteAllText(Path.Combine(dir2, "save.json"), SaveSystem.Save(sim3.State));
                File.WriteAllText(Path.Combine(dir2, "save-meta.txt"), DateTime.UtcNow.Ticks.ToString());
                var linfos = SaveSlots.ListSlots(dir2);
                Check(linfos[0].Exists && linfos[0].Seed == 31337, "legacy save migrates to slot 0", "seed " + linfos[0].Seed);
                Check(!File.Exists(Path.Combine(dir2, "save.json")), "legacy file moved away", "still present");
                var llegacy = SaveSystem.Load(SaveSlots.LoadSlotJson(dir2, 0));
                Check(llegacy.Seed == 31337, "migrated save loads", "wrong seed");

                // 7. Even before migration, the legacy file loads as slot 0.
                string dir3 = Path.Combine(Path.GetTempPath(), "solace-legacyraw-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir3);
                try
                {
                    File.WriteAllText(Path.Combine(dir3, "save.json"), SaveSystem.Save(Simulation.NewLife(5150).State));
                    var raw = SaveSystem.Load(SaveSlots.LoadSlotJson(dir3, 0));
                    Check(raw.Seed == 5150, "unmigrated legacy loads as slot 0", "wrong seed");
                }
                finally { try { Directory.Delete(dir3, true); } catch { } }
            }
            finally { try { Directory.Delete(dir2, true); } catch { } }

            // 8. Bad slot index rejected.
            bool threw = false;
            try { SaveSlots.SaveSlot(dir, 9, sim.State); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "slot 9 rejected", "no throw");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // -- weather gameplay ----------------------------------------------------------

    private static void TestWeatherGameplay()
    {
        Console.WriteLine("[weather]");
        var sim = Simulation.NewLife(4242);
        var agent = sim.State.Agent;
        agent.X = 0f; agent.Z = 0f;
        agent.Hunger = 40f; agent.Thirst = 30f; agent.Energy = 80f; agent.Health = 100f;
        agent.Mood = 50f;
        // No predators, no distractions: clear the field.
        sim.State.Entities.Clear();
        sim.State.Kits.Clear();
        // Give the fox a known den nearby.
        PointOfInterest den = null;
        foreach (var p in sim.State.World.Pois)
            if (p.Type == PoiType.Den) { den = p; break; }
        Check(den != null, "test setup: den exists", "no den placed");
        if (den == null) return;
        agent.KnownPoiIds.Add(den.Id);
        agent.X = den.X + 30f; agent.Z = den.Z; // 30m from home

        var ctx = new BrainContext { Sim = sim, Ai = sim.AiRng, Ev = sim.EventRng, Now = sim.Now };

        // 1. Storm: the fox chooses shelter over everything routine.
        sim.State.Weather = Weather.Storm;
        string stormChoice = sim.Brain.Decide(ctx);
        Check(stormChoice == "SeekShelter", "storm -> SeekShelter (got " + stormChoice + ")",
            "trace: " + sim.Brain.LastDecisionTrace.Reason);

        // 2. Clear: no shelter-seeking.
        sim.State.Weather = Weather.Clear;
        var shelterAct = sim.Brain.GetAction("SeekShelter");
        Check(!shelterAct.CanScore(ctx), "clear weather -> SeekShelter cannot score", "scored in clear");

        // 3. Predator + storm: fleeing the gloom-maw wins over shelter.
        sim.State.Weather = Weather.Storm;
        sim.State.Entities.Add(new EntityState
        {
            Id = 9001, Kind = EntityKind.Predator, Name = "gloom-maw",
            X = agent.X + 8f, Z = agent.Z, Health = 90f, Behavior = "Hunt",
            HomeX = agent.X + 8f, HomeZ = agent.Z
        });
        string dangerChoice = sim.Brain.Decide(ctx);
        Check(dangerChoice == "Flee", "predator + storm -> Flee wins (got " + dangerChoice + ")",
            "shelter outranked flee");
        sim.State.Entities.Clear();

        // 4. Storm sense makes food urgent: Eat outranks Rest when foreboding.
        sim.State.Weather = Weather.Cloudy;
        sim.StormSensedUntil = sim.Now + 5000f;
        agent.Hunger = 55f; agent.Energy = 40f; // hungry AND tired
        agent.FleeUntil = 0f; // clear the flee lock from test 3
        // Known bush for Eat to target.
        PointOfInterest bush = null;
        foreach (var p in sim.State.World.Pois)
            if (p.Type == PoiType.GlowberryBush && p.Stock > 0) { bush = p; break; }
        if (bush != null)
        {
            agent.KnownPoiIds.Add(bush.Id);
            agent.X = bush.X + 10f; agent.Z = bush.Z;
            string senseChoice = sim.Brain.Decide(ctx);
            Check(senseChoice == "Eat", "storm sensed -> Eat beats Rest (got " + senseChoice + ")",
                "foreboding bonus missing");
        }
        else
            Check(false, "test setup: bush for sense test", "no bush");
        sim.StormSensedUntil = -1f;

        // 5. Kits hide in storms.
        sim.State.Weather = Weather.Storm;
        var kit = new KitState
        {
            Name = "TestKit", IsAlive = true, X = den.X + 40f, Z = den.Z,
            Health = 100f, Energy = 80f, Hunger = 20f, Mood = 60f, State = "Play"
        };
        sim.State.Kits.Add(kit);
        KitBrain.Tick(sim, kit, 1f);
        Check(kit.State == "Hide", "kits hide in storms (got " + kit.State + ")", "kit stayed out");
        sim.State.Kits.Clear();

        // 6. Kits play joyfully in rain: mood rises faster than in clear.
        sim.State.Weather = Weather.Rain;
        sim.State.Agent.CurrentGoal = ""; // don't let the parent's last decision interfere
        sim.State.Agent.X = 5f; sim.State.Agent.Z = 0f; // parent near but not eating
        var kit2 = new KitState
        {
            Name = "TestKit2", IsAlive = true, X = 0f, Z = 0f, FollowingParent = false,
            Health = 100f, Energy = 80f, Hunger = 20f, Mood = 50f, State = "Play"
        };
        float moodBefore = kit2.Mood;
        KitBrain.Tick(sim, kit2, 10f);
        float rainGain = kit2.Mood - moodBefore;
        Check(rainGain > 1.0f, "rain boosts kit play mood (+" + rainGain.ToString("F2") + ")", "no joy");

        // 7. Determinism: same seed, same weather decisions.
        var sim2 = Simulation.NewLife(4242);
        sim2.State.Weather = Weather.Storm;
        var agent2 = sim2.State.Agent;
        foreach (var p in sim2.State.World.Pois)
            if (p.Type == PoiType.Den) { agent2.KnownPoiIds.Add(p.Id); agent2.X = p.X + 30f; agent2.Z = p.Z; break; }
        agent2.Hunger = 40f; agent2.Thirst = 30f; agent2.Energy = 80f; agent2.Health = 100f;
        sim2.State.Entities.Clear(); sim2.State.Kits.Clear();
        var ctx2 = new BrainContext { Sim = sim2, Ai = sim2.AiRng, Ev = sim2.EventRng, Now = sim2.Now };
        string stormChoice2 = sim2.Brain.Decide(ctx2);
        Check(stormChoice2 == stormChoice, "storm decision deterministic (" + stormChoice2 + ")",
            "nondeterministic: " + stormChoice + " vs " + stormChoice2);
    }

    private static void TestEasterEggs()
    {
        Console.WriteLine("[easter-eggs]");

        // 1. Moon phase: t=0 is new moon, ~14.765 days is full.
        Check(Math.Abs(EasterEggSystem.MoonPhase(0.0)) < 0.001, "t=0 is new moon",
            "phase=" + EasterEggSystem.MoonPhase(0.0));
        double fullT = 14.765 * 86400.0;
        Check(Math.Abs(EasterEggSystem.MoonPhase(fullT) - 0.5) < 0.01, "day 14.765 is full moon",
            "phase=" + EasterEggSystem.MoonPhase(fullT));
        Check(EasterEggSystem.IsFullMoon(fullT), "IsFullMoon at full", "false at full");
        Check(!EasterEggSystem.IsFullMoon(7.0 * 86400.0), "not full at day 7", "true at quarter");
        Check(!EasterEggSystem.IsFullMoon(0.0), "not full at new moon", "true at new");

        // 2. Rainbow grove: seed 28 grows one (found by probe).
        var wg = WorldGenerator.Generate(new WorldConfig { Seed = 28 });
        PointOfInterest grove = null;
        foreach (var p in wg.Pois)
            if (p.Type == PoiType.RainbowGrove) grove = p;
        Check(grove != null, "seed 28 grows a rainbow grove", "none found");
        if (grove != null)
        {
            Check(!wg.IsWater(grove.X, grove.Z), "grove on dry land", "in water");
            Check(grove.Name.Length > 0, "grove has a true name (" + grove.Name + ")", "nameless");
            Check(grove.DisplayName == "a hidden grove", "grove hides its name (" + grove.DisplayName + ")",
                "name leaked: " + grove.DisplayName);
        }
        // Deterministic: same seed → same grove.
        var wg2 = WorldGenerator.Generate(new WorldConfig { Seed = 28 });
        int g1 = 0, g2 = 0;
        foreach (var p in wg.Pois) if (p.Type == PoiType.RainbowGrove) g1++;
        foreach (var p in wg2.Pois) if (p.Type == PoiType.RainbowGrove) g2++;
        Check(g1 == g2 && g1 == 1, "grove deterministic (1 per seed-28 world)", g1 + " vs " + g2);
        // Rarity: ~1% of worlds.
        int groveWorlds = 0;
        for (int s = 1000; s < 1200; s++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = s });
            foreach (var p in w.Pois)
                if (p.Type == PoiType.RainbowGrove) { groveWorlds++; break; }
        }
        Check(groveWorlds >= 0 && groveWorlds <= 8, "grove is rare (" + groveWorlds + "/200 worlds)",
            "rate off: " + groveWorlds + "/200");

        // 3. Grove discovery writes the magical journal entry.
        var sim = Simulation.NewLife(28);
        PointOfInterest gg = null;
        foreach (var p in sim.State.World.Pois)
            if (p.Type == PoiType.RainbowGrove) gg = p;
        Check(gg != null, "sim world has the grove", "missing in sim");
        if (gg != null)
        {
            int before = sim.State.Journal.Count;
            sim.State.Agent.X = gg.X; sim.State.Agent.Z = gg.Z;
            sim.DiscoverPoi(gg.Id);
            Check(sim.State.Journal.Count == before + 1, "grove discovery journals",
                "no entry written");
            JournalEntry last = null;
            foreach (var je in sim.State.Journal.Entries) last = je;
            Check(last != null && last.Salience >= 0.99f, "grove entry is maximum salience",
                last == null ? "no entry" : "salience=" + last.Salience);
            Check(last != null && (last.Text.Contains("rainbow") || last.Text.Contains("crystal") || last.Text.Contains("color")),
                "grove entry is magical", "text missing magic");
        }

        // 4. Secrets milestones: spirit + meteor + grove → curiosity.
        var sim2 = Simulation.NewLife(999);
        sim2.State.Eggs.SpiritSeen = true;
        sim2.State.Eggs.MeteorSeen = true;
        // Discover a grove: force one into this world for the test.
        var fakeGrove = new PointOfInterest();
        fakeGrove.Id = 9999; fakeGrove.Type = PoiType.RainbowGrove;
        fakeGrove.Name = "test grove"; fakeGrove.X = 0; fakeGrove.Z = 0;
        fakeGrove.Radius = 10f; fakeGrove.Discovered = true;
        sim2.State.World.Pois.Add(fakeGrove);
        var unlocked = MilestoneSystem.Tick(sim2);
        Check(unlocked.Contains("fox_spirit"), "spirit milestone unlocks", "missing");
        Check(unlocked.Contains("meteor_shower"), "meteor milestone unlocks", "missing");
        Check(unlocked.Contains("rainbow_grove"), "grove milestone unlocks", "missing");
        Check(unlocked.Contains("curiosity"), "CURIOSITY unlocks when all three found", "missing");

        // 5. Easter egg state survives save/load.
        var sim3 = Simulation.NewLife(4242);
        sim3.State.Eggs.SpiritSeen = true;
        sim3.State.Eggs.SpiritX = 12.5f; sim3.State.Eggs.SpiritZ = -3.25f;
        sim3.State.Eggs.SpiritVisibleUntil = 123456.0;
        string json = SaveSystem.Save(sim3.State);
        var loaded = SaveSystem.Load(json);
        Check(loaded.Eggs.SpiritSeen, "spiritSeen survives save/load", "lost");
        Check(Math.Abs(loaded.Eggs.SpiritX - 12.5f) < 0.001f, "spirit pos survives save/load", "lost");
        Check(Math.Abs(loaded.Eggs.SpiritVisibleUntil - 123456.0) < 1.0, "spirit timer survives", "lost");

        // 6. EasterEggSystem.Tick runs without crashing over a full moon night.
        var sim4 = Simulation.NewLife(31337);
        sim4.State.ElapsedSeconds = 14.765 * 86400.0 + 3600.0; // full moon, ~01:00
        sim4.State.Agent.Energy = 80f;
        for (int i = 0; i < 200; i++) sim4.Step(60f); // 200 game-minutes
        Check(true, "tick survives full-moon night", "crashed");
    }

    private static void TestDailyVale()
    {
        Console.WriteLine("[daily-vale]");

        // 1. Seed determinism: same date -> same seed, for DateTime and string forms.
        var d1 = new DateTime(2026, 10, 6);
        int s1 = DailyVale.SeedFor(d1);
        int s2 = DailyVale.SeedFor(d1);
        int s3 = DailyVale.SeedFor("2026-10-06");
        Check(s1 == s2 && s1 == s3, "daily seed deterministic", s1 + " vs " + s2 + " vs " + s3);
        Check(s1 >= 1 && s1 <= 999999999, "daily seed in valid range", "seed=" + s1);

        // 2. Different dates -> different seeds.
        int sOther = DailyVale.SeedFor(new DateTime(2026, 10, 7));
        Check(sOther != s1, "different dates give different seeds", "both " + s1);

        // 3. Date key format.
        Check(DailyVale.DateString(d1) == "2026-10-06", "date key format", DailyVale.DateString(d1));
        Check(DailyVale.PrettyDate("2026-10-06") == "Oct 6", "pretty date", DailyVale.PrettyDate("2026-10-06"));

        // 4. Streaks: consecutive visits build, gaps reset, same-day dedupes.
        string tmp = Path.Combine(Path.GetTempPath(), "solace-daily-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var oct4 = new DateTime(2026, 10, 4);
            var oct5 = new DateTime(2026, 10, 5);
            var oct6 = new DateTime(2026, 10, 6);
            Check(DailyVale.RecordVisit(tmp, oct4) == 1, "first visit starts streak", "streak!=1");
            Check(DailyVale.RecordVisit(tmp, oct4) == 1, "same-day visit deduped", "streak!=1");
            Check(DailyVale.RecordVisit(tmp, oct5) == 2, "consecutive day extends streak", "streak!=2");
            Check(DailyVale.RecordVisit(tmp, oct6) == 3, "three-day streak", "streak!=3");

            // Gap: skip Oct 7, visit Oct 8 -> streak resets to 1.
            var oct8 = new DateTime(2026, 10, 8);
            Check(DailyVale.RecordVisit(tmp, oct8) == 1, "gap resets streak", "streak!=1");

            // Streak data survives a reload (new process would re-read the file).
            var reloaded = DailyVale.LoadStreak(tmp);
            Check(DailyVale.CurrentStreak(reloaded, oct8) == 1, "streak persists", "lost");
            Check(reloaded.VisitedDates.Count == 4, "4 visit dates stored", "count=" + reloaded.VisitedDates.Count);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }

        // 5. Daily world file round-trip.
        string tmp2 = Path.Combine(Path.GetTempPath(), "solace-daily-test2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sim = Simulation.NewLife(DailyVale.SeedFor("2026-10-06"));
            var when = new DateTime(2026, 10, 6);
            Check(!DailyVale.DailyExists(tmp2, when), "no daily file before save", "exists");
            DailyVale.SaveDaily(tmp2, when, sim.State);
            Check(DailyVale.DailyExists(tmp2, when), "daily file exists after save", "missing");
            string json = DailyVale.LoadDailyJson(tmp2, when);
            Check(!string.IsNullOrEmpty(json), "daily JSON loads", "null");
            var loaded = SaveSystem.Load(json);
            Check(loaded.Seed == sim.State.Seed, "daily seed round-trips", "seed mismatch");
            Check(loaded.DailyInfo == null, "DailyInfo is transient (not serialized)", "leaked into save");
        }
        finally
        {
            try { Directory.Delete(tmp2, true); } catch { }
        }

        // 6. Daily milestone definitions.
        var dd = MilestoneDefs.ById("daily_first");
        Check(dd != null && dd.Category == MilestoneCategory.Daily, "daily_first def", "missing");
        Check(MilestoneDefs.ById("daily_streak_7") != null, "daily_streak_7 def", "missing");
        Check(MilestoneDefs.ById("daily_streak_30") != null, "daily_streak_30 def", "missing");

        // 7. Milestone detection: daily world unlocks daily_first; streak 7 unlocks the streak milestone.
        var simD = Simulation.NewLife(DailyVale.SeedFor("2026-10-06"));
        simD.State.DailyInfo = new DailyVisitInfo { IsDailyWorld = true, DateString = "2026-10-06", StreakDays = 7 };
        var newly = MilestoneSystem.Tick(simD);
        Check(newly.Contains("daily_first"), "daily_first unlocks in daily world", "missing");
        Check(newly.Contains("daily_streak_7"), "daily_streak_7 unlocks at streak 7", "missing");
        Check(!simD.State.Milestones.IsUnlocked("daily_streak_30"), "no 30-day at streak 7", "unlocked early");

        // 8. Slot worlds never unlock daily milestones.
        var simS = Simulation.NewLife(999);
        var newlyS = MilestoneSystem.Tick(simS);
        Check(!newlyS.Contains("daily_first"), "slot world stays non-daily", "leaked");
    }

    private static void TestStats()
    {
        Console.WriteLine("[stats]");

        // 1. Fresh life: zeroed stats, generations starts at 1.
        var sim = Simulation.NewLife(4242);
        StatSystem.Tick(sim);
        var st = sim.State.Stats;
        Check(st.DistanceTraveled == 0f, "distance starts at 0", "got " + st.DistanceTraveled);
        Check(st.KitsBorn == 0, "kits start at 0", "got " + st.KitsBorn);
        Check(st.Generations == 1, "generations starts at 1", "got " + st.Generations);

        // 2. Distance accumulates on movement.
        var a = sim.State.Agent;
        float x0 = a.X, z0 = a.Z;
        a.X += 3f; a.Z += 4f; // 5m move
        StatSystem.Tick(sim);
        Check(Math.Abs(st.DistanceTraveled - 5f) < 0.01f,
            "distance accumulates 5m move", "got " + st.DistanceTraveled);

        // 3. Teleport guard: huge jumps (succession) don't count.
        a.X += 1000f;
        StatSystem.Tick(sim);
        Check(Math.Abs(st.DistanceTraveled - 5f) < 0.01f,
            "teleport ignored by distance guard", "got " + st.DistanceTraveled);

        // 4. POI discovery count tracks Discovered flags.
        int before = st.PoisDiscovered;
        int marked = 0;
        foreach (var p in sim.State.World.Pois)
        {
            if (marked < 3 && !p.Discovered) { p.Discovered = true; marked++; }
        }
        StatSystem.Tick(sim);
        Check(st.PoisDiscovered == before + 3, "POI count tracks discoveries",
            "got " + st.PoisDiscovered + ", expected " + (before + 3));

        // 5. Kit birth edge detection (no double count).
        var sim2 = Simulation.NewLife(4243);
        var st2 = sim2.State.Stats;
        StatSystem.Tick(sim2);
        int kits0 = st2.KitsBorn;
        // Simulate a birth by adding a kit directly.
        sim2.State.Kits.Add(new KitState { Id = 9991, Name = "TestKit" });
        StatSystem.Tick(sim2);
        StatSystem.Tick(sim2); // second tick must not double-count
        Check(st2.KitsBorn == kits0 + 1, "kit birth counted once",
            "got " + st2.KitsBorn + ", expected " + (kits0 + 1));

        // 6. Predator kill edge detection.
        var sim3 = Simulation.NewLife(4244);
        var st3 = sim3.State.Stats;
        StatSystem.Tick(sim3);
        int kills0 = st3.PredatorsDefeated;
        var pred = new EntityState
        {
            Id = 7771, Kind = EntityKind.Predator,
            X = 10f, Z = 10f, Health = 0f, Behavior = "Dead"
        };
        sim3.State.Entities.Add(pred);
        StatSystem.Tick(sim3);
        StatSystem.Tick(sim3);
        Check(st3.PredatorsDefeated == kills0 + 1, "predator kill counted once",
            "got " + st3.PredatorsDefeated);

        // 7. Greetings summed from social memory.
        var sim4 = Simulation.NewLife(4245);
        var st4 = sim4.State.Stats;
        sim4.State.Social.RecordGreeting(101, 0f);
        sim4.State.Social.RecordGreeting(101, 1f);
        sim4.State.Social.RecordGreeting(102, 2f);
        StatSystem.Tick(sim4);
        Check(st4.Greetings == 3, "greetings summed across kindred",
            "got " + st4.Greetings);

        // 8. Tales / dreams / generations are direct reads.
        var sim5 = Simulation.NewLife(4246);
        var st5 = sim5.State.Stats;
        sim5.State.Lineage.Generation = 4;
        StatSystem.Tick(sim5);
        Check(st5.Generations == 4, "generations tracked", "got " + st5.Generations);

        // 9. MaxDistanceFromDen grows with roaming.
        var sim6 = Simulation.NewLife(4247);
        var st6 = sim6.State.Stats;
        StatSystem.Tick(sim6); // caches den
        Check(st6.HasDen, "den location cached", "no den found");
        var a6 = sim6.State.Agent;
        a6.X = st6.DenX + 100f; a6.Z = st6.DenZ;
        StatSystem.Tick(sim6);
        Check(st6.MaxDistanceFromDen >= 99f, "max den distance tracked",
            "got " + st6.MaxDistanceFromDen);
        // Moving back doesn't shrink the max.
        a6.X = st6.DenX; a6.Z = st6.DenZ;
        StatSystem.Tick(sim6);
        Check(st6.MaxDistanceFromDen >= 99f, "max den distance never shrinks",
            "got " + st6.MaxDistanceFromDen);

        // 10. Save/load round-trip preserves stats.
        var sim7 = Simulation.NewLife(4248);
        sim7.State.Agent.X += 10f;
        StatSystem.Tick(sim7);
        string json = SaveSystem.Save(sim7.State);
        var loaded = SaveSystem.Load(json);
        Check(Math.Abs(loaded.Stats.DistanceTraveled - sim7.State.Stats.DistanceTraveled) < 0.01f,
            "stats survive save/load", "distance diverged");
        Check(loaded.Stats.HasLastPos == sim7.State.Stats.HasLastPos,
            "position continuity survives save/load", "flag diverged");

        // 11. Determinism: identical stepping -> identical stats JSON.
        var simA = Simulation.NewLife(4249);
        simA.Step(2f);
        var simB = Simulation.NewLife(4249);
        simB.Step(2f);
        string ja = simA.State.Stats.ToJson().ToString();
        string jb = simB.State.Stats.ToJson().ToString();
        Check(ja == jb, "stats deterministic across identical runs", "diverged");

        // 12. Unity-owned fields default empty and round-trip.
        var st8 = new StatState();
        Check(st8.RealPlaySeconds == 0f && st8.DayDates.Count == 0,
            "playtime fields default empty", "not empty");
        st8.RealPlaySeconds = 123.5f;
        st8.DayDates.Add("2026-10-06"); st8.DaySeconds.Add(60f);
        var rt = StatState.FromJson(st8.ToJson());
        Check(Math.Abs(rt.RealPlaySeconds - 123.5f) < 0.01f &&
              rt.DayDates.Count == 1 && rt.DayDates[0] == "2026-10-06" &&
              Math.Abs(rt.DaySeconds[0] - 60f) < 0.01f,
            "playtime fields round-trip", "diverged");
    }

    private static void TestChallengeModes()
    {
        Console.WriteLine("[challenge-modes]");

        // 1. Metadata: every mode has a name and description.
        foreach (ChallengeMode m in new[] { ChallengeMode.Standard, ChallengeMode.Peaceful, ChallengeMode.Hardcore, ChallengeMode.Speedrun })
        {
            Check(!string.IsNullOrEmpty(ChallengeModes.DisplayName(m)), "name for " + m, "empty");
            Check(!string.IsNullOrEmpty(ChallengeModes.Description(m)), "description for " + m, "empty");
        }

        // 2. Tuning knobs.
        Check(ChallengeModes.PredatorCount(ChallengeMode.Standard) == 3, "standard: 3 predators", "wrong");
        Check(ChallengeModes.PredatorCount(ChallengeMode.Peaceful) == 0, "peaceful: 0 predators", "wrong");
        Check(ChallengeModes.PredatorCount(ChallengeMode.Hardcore) == 5, "hardcore: 5 predators", "wrong");
        Check(Math.Abs(ChallengeModes.PredatorAggression(ChallengeMode.Hardcore) - 1.6f) < 0.001f, "hardcore aggression 1.6x", "wrong");
        Check(Math.Abs(ChallengeModes.PredatorAggression(ChallengeMode.Standard) - 1.0f) < 0.001f, "standard aggression 1.0x", "wrong");
        Check(Math.Abs(ChallengeModes.FoodScarcity(ChallengeMode.Hardcore) - 0.55f) < 0.001f, "hardcore food 0.55x", "wrong");
        Check(ChallengeModes.SuccessionAllowed(ChallengeMode.Hardcore) == false, "hardcore: no succession", "allowed");
        Check(ChallengeModes.SuccessionAllowed(ChallengeMode.Peaceful), "peaceful: succession allowed", "denied");
        Check(ChallengeModes.SpeedrunTargetGeneration == 10, "speedrun target gen 10", "wrong");

        // 3. Peaceful life starts with zero predators.
        var peaceful = ChallengeModes.NewChallengeLife(777001, ChallengeMode.Peaceful);
        int pCount = 0;
        foreach (var e in peaceful.State.Entities)
            if (e.Kind == EntityKind.Predator) pCount++;
        Check(pCount == 0, "peaceful life has no predators", "found " + pCount);

        // 4. Standard life keeps its 3 predators.
        var standard = ChallengeModes.NewChallengeLife(777001, ChallengeMode.Standard);
        int sCount = 0;
        foreach (var e in standard.State.Entities)
            if (e.Kind == EntityKind.Predator) sCount++;
        Check(sCount == 3, "standard life has 3 predators", "found " + sCount);

        // 5. Hardcore life has 5 predators, placed deterministically.
        var hard1 = ChallengeModes.NewChallengeLife(777002, ChallengeMode.Hardcore);
        var hard2 = ChallengeModes.NewChallengeLife(777002, ChallengeMode.Hardcore);
        int hCount = 0;
        foreach (var e in hard1.State.Entities)
            if (e.Kind == EntityKind.Predator) hCount++;
        Check(hCount == 5, "hardcore life has 5 predators", "found " + hCount);
        bool sameSpots = true;
        var hp1 = new List<EntityState>();
        var hp2 = new List<EntityState>();
        foreach (var e in hard1.State.Entities) if (e.Kind == EntityKind.Predator) hp1.Add(e);
        foreach (var e in hard2.State.Entities) if (e.Kind == EntityKind.Predator) hp2.Add(e);
        if (hp1.Count == hp2.Count)
        {
            for (int i = 0; i < hp1.Count; i++)
                if (Math.Abs(hp1[i].X - hp2[i].X) > 0.001f || Math.Abs(hp1[i].Z - hp2[i].Z) > 0.001f) sameSpots = false;
        }
        else sameSpots = false;
        Check(sameSpots, "hardcore predator placement deterministic", "diverged");

        // 6. Peaceful director sweep kills any gloom-maw that appears.
        var simP = Simulation.NewLife(777003);
        var chP = ChallengeState.NewRun(ChallengeMode.Peaceful);
        ChallengeDirector.Tick(simP, chP); // baseline
        simP.State.Entities.Add(new EntityState { Id = 4242, Kind = EntityKind.Predator, Name = "gloom-maw", X = 5f, Z = 5f, Health = 100f, Behavior = "Hunt" });
        ChallengeDirector.Tick(simP, chP);
        bool anyAlive = false;
        foreach (var e in simP.State.Entities)
            if (e.Kind == EntityKind.Predator && e.IsAlive) anyAlive = true;
        Check(!anyAlive, "peaceful sweep kills spawned predators", "a predator survived");

        // 7. Standard mode: director is a no-op (predators untouched).
        var simS = Simulation.NewLife(777004);
        var chS = ChallengeState.NewRun(ChallengeMode.Standard);
        ChallengeDirector.Tick(simS, chS);
        int aliveS = 0;
        foreach (var e in simS.State.Entities)
            if (e.Kind == EntityKind.Predator && e.IsAlive) aliveS++;
        Check(aliveS == 3, "standard director leaves predators alone", "alive=" + aliveS);

        // 8. Speedrun completes on crossing generation 10.
        var simR = Simulation.NewLife(777005);
        var chR = ChallengeState.NewRun(ChallengeMode.Speedrun);
        Check(chR.SpeedrunStartRealTicks > 0, "speedrun stamps real start time", "ticks=0");
        int j0 = simR.State.Journal.Count;
        ChallengeDirector.Tick(simR, chR); // baseline at gen 1
        simR.State.Lineage.Generation = 5;
        ChallengeDirector.Tick(simR, chR);
        Check(!chR.SpeedrunComplete, "no completion at gen 5", "completed early");
        simR.State.Lineage.Generation = 10;
        ChallengeDirector.Tick(simR, chR);
        Check(chR.SpeedrunComplete, "speedrun completes at gen 10", "not complete");
        Check(chR.LastSpeedrunEntry != null && chR.LastSpeedrunEntry.Seed == simR.State.Seed, "completion entry recorded", "missing");
        Check(simR.State.Journal.Count > j0, "completion writes a journal entry", "no journal");
        // Second tick must not double-fire.
        bool stillComplete = chR.SpeedrunComplete;
        ChallengeDirector.Tick(simR, chR);
        Check(stillComplete && chR.SpeedrunComplete, "completion fires once", "re-fired");

        // 9. Speedrun loaded mid-run (already gen 12) does not instant-complete.
        var simR2 = Simulation.NewLife(777006);
        simR2.State.Lineage.Generation = 12;
        var chR2 = ChallengeState.NewRun(ChallengeMode.Speedrun);
        ChallengeDirector.Tick(simR2, chR2);
        Check(!chR2.SpeedrunComplete, "no instant completion when loaded past target", "completed");

        // 10. Hardcore: generation increase marks the run over.
        var simH = Simulation.NewLife(777007);
        var chH = ChallengeState.NewRun(ChallengeMode.Hardcore);
        int jh0 = simH.State.Journal.Count;
        ChallengeDirector.Tick(simH, chH); // baseline gen 1
        simH.State.Lineage.Generation = 2; // a death + succession happened
        ChallengeDirector.Tick(simH, chH);
        Check(chH.RunOver, "hardcore run ends on death", "not over");
        Check(chH.DeathGeneration == 1, "death generation recorded", "gen=" + chH.DeathGeneration);
        Check(simH.State.Journal.Count > jh0, "hardcore end writes a journal entry", "no journal");

        // 11. Standard mode never marks runs over.
        var simH2 = Simulation.NewLife(777008);
        var chH2 = ChallengeState.NewRun(ChallengeMode.Standard);
        ChallengeDirector.Tick(simH2, chH2);
        simH2.State.Lineage.Generation = 2;
        ChallengeDirector.Tick(simH2, chH2);
        Check(!chH2.RunOver, "standard never ends runs", "marked over");

        // 12. ChallengeState JSON round-trip.
        var cs = ChallengeState.NewRun(ChallengeMode.Hardcore);
        cs.RunOver = true; cs.DeathGeneration = 3; cs.DeathCause = "gloom-maw";
        cs.LastSeenGeneration = 4; cs.SpeedrunGameSeconds = 123.5;
        var cs2 = ChallengeState.FromJson(cs.ToJson());
        Check(cs2.Mode == ChallengeMode.Hardcore && cs2.RunOver && cs2.DeathGeneration == 3 &&
              cs2.DeathCause == "gloom-maw" && cs2.LastSeenGeneration == 4 &&
              Math.Abs(cs2.SpeedrunGameSeconds - 123.5) < 0.001 &&
              cs2.SpeedrunStartRealTicks == cs.SpeedrunStartRealTicks,
            "challenge state round-trips", "diverged");

        // 13. SpeedrunEntry JSON round-trip.
        var se = new SpeedrunEntry { Seed = 42, FoxName = "Ash", GameSeconds = 3600.5, RealSeconds = 7200.25, DateUtcTicks = 123456789L, Generations = 10 };
        var se2 = SpeedrunEntry.FromJson(se.ToJson());
        Check(se2.Seed == 42 && se2.FoxName == "Ash" &&
              Math.Abs(se2.GameSeconds - 3600.5) < 0.001 &&
              Math.Abs(se2.RealSeconds - 7200.25) < 0.001 &&
              se2.DateUtcTicks == 123456789L && se2.Generations == 10,
            "speedrun entry round-trips", "diverged");

        // 14. Sidecar save/load/delete in a temp dir.
        string tmp = Path.Combine(Path.GetTempPath(), "solace-challenge-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(ChallengeSave.Load(tmp, 0) == null, "no challenge file before save", "found one");
            var chSave = ChallengeState.NewRun(ChallengeMode.Speedrun);
            chSave.LastSeenGeneration = 7;
            ChallengeSave.Save(tmp, 0, chSave);
            var chLoaded = ChallengeSave.Load(tmp, 0);
            Check(chLoaded != null && chLoaded.Mode == ChallengeMode.Speedrun && chLoaded.LastSeenGeneration == 7 &&
                  chLoaded.SpeedrunStartRealTicks == chSave.SpeedrunStartRealTicks,
                "sidecar round-trips", "diverged");
            ChallengeSave.Delete(tmp, 0);
            Check(ChallengeSave.Load(tmp, 0) == null, "sidecar deleted", "still there");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }

        // 15. Leaderboard keeps the fastest 10, sorted, with ranks.
        string tmpL = Path.Combine(Path.GetTempPath(), "solace-challenge-lb-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(ChallengeSave.LoadLeaderboard(tmpL).Count == 0, "leaderboard starts empty", "not empty");
            for (int i = 12; i >= 1; i--)
            {
                int rank = ChallengeSave.RecordRun(tmpL, new SpeedrunEntry
                {
                    Seed = i, FoxName = "Fox" + i, GameSeconds = i * 100.0,
                    RealSeconds = i * 200.0, DateUtcTicks = i, Generations = 10
                });
                if (i == 12) Check(rank == 1, "first run ranks #1 on empty board", "rank=" + rank);
                if (i == 1) Check(rank == 1, "fastest of 12 ranks #1", "rank=" + rank);
            }
            var lb = ChallengeSave.LoadLeaderboard(tmpL);
            Check(lb.Count == 10, "leaderboard truncated to 10", "count=" + lb.Count);
            bool sorted = true;
            for (int i = 1; i < lb.Count; i++)
                if (lb[i].GameSeconds < lb[i - 1].GameSeconds) sorted = false;
            Check(sorted, "leaderboard sorted by game time", "unsorted");
            Check(Math.Abs(lb[0].GameSeconds - 100.0) < 0.001, "fastest entry first", "got " + lb[0].GameSeconds);
            bool slowestDropped = true;
            foreach (var e in lb)
                if (e.Seed == 11 || e.Seed == 12) slowestDropped = false;
            Check(slowestDropped, "two slowest runs dropped", "still present");
        }
        finally
        {
            try { Directory.Delete(tmpL, true); } catch { }
        }

        // 16. Duration formatting.
        Check(ChallengeModes.FormatDuration(3661) == "1:01:01", "format 3661s", ChallengeModes.FormatDuration(3661));
        Check(ChallengeModes.FormatDuration(90061) == "1d 1:01:01", "format 90061s", ChallengeModes.FormatDuration(90061));
        Check(ChallengeModes.FormatDuration(59) == "0:00:59", "format 59s", ChallengeModes.FormatDuration(59));
    }

    private static void TestChallengeWiring()
    {
        // 1. ActiveMode defaults to Standard: sim systems see no tuning change
        //    unless the Unity layer sets a challenge mode.
        Check(ChallengeModes.ActiveMode == ChallengeMode.Standard, "ActiveMode defaults to Standard", ChallengeModes.ActiveMode.ToString());
        Check(Math.Abs(ChallengeModes.FoodScarcity(ChallengeModes.ActiveMode) - 1.0f) < 0.001f,
            "default ActiveMode leaves berry regrowth untouched", "multiplier changed");

        // 2. Setting ActiveMode changes the food hook, and resetting restores it.
        //    (Must reset: the static is process-wide and other tests assume Standard.)
        ChallengeModes.ActiveMode = ChallengeMode.Hardcore;
        Check(Math.Abs(ChallengeModes.FoodScarcity(ChallengeModes.ActiveMode) - 0.55f) < 0.001f,
            "Hardcore ActiveMode applies food scarcity", "wrong multiplier");
        ChallengeModes.ActiveMode = ChallengeMode.Standard;
        Check(ChallengeModes.ActiveMode == ChallengeMode.Standard, "ActiveMode resets to Standard", "leaked");

        // 3. Director is null-safe: the bootstrap may Tick before a challenge exists.
        try
        {
            ChallengeDirector.Tick(null, null);
            var simN = Simulation.NewLife(888001);
            ChallengeDirector.Tick(simN, null);
            ChallengeDirector.Tick(null, ChallengeState.NewRun(ChallengeMode.Peaceful));
            Check(true, "director null-safe", "");
        }
        catch (Exception ex)
        {
            Check(false, "director null-safe", ex.GetType().Name);
        }

        // 4. NewChallengeLife predator counts per mode (what the bootstrap builds).
        var simP = ChallengeModes.NewChallengeLife(888002, ChallengeMode.Peaceful);
        int predP = 0;
        foreach (var e in simP.State.Entities) if (e.Kind == EntityKind.Predator) predP++;
        Check(predP == 0, "wired peaceful life has no predators", "count=" + predP);
        var simH = ChallengeModes.NewChallengeLife(888003, ChallengeMode.Hardcore);
        int predH = 0;
        foreach (var e in simH.State.Entities) if (e.Kind == EntityKind.Predator) predH++;
        Check(predH == 5, "wired hardcore life has 5 predators", "count=" + predH);

        // 5. New runs start un-over with generation baseline unset (director sets it).
        var ch = ChallengeState.NewRun(ChallengeMode.Speedrun);
        Check(!ch.RunOver && !ch.SpeedrunComplete && ch.LastSeenGeneration == 0,
            "new run starts clean", "dirty state");

        // 6. Deleting a slot clears its challenge sidecar too.
        string tmpD = Path.Combine(Path.GetTempPath(), "solace-challenge-del-" + Guid.NewGuid().ToString("N"));
        try
        {
            SaveSlots.SaveSlot(tmpD, 1, Simulation.NewLife(888004).State);
            ChallengeSave.Save(tmpD, 1, ChallengeState.NewRun(ChallengeMode.Hardcore));
            Check(ChallengeSave.Load(tmpD, 1) != null, "challenge sidecar exists before delete", "missing");
            SaveSlots.DeleteSlot(tmpD, 1);
            Check(ChallengeSave.Load(tmpD, 1) == null, "slot delete clears challenge sidecar", "stale file");
        }
        finally
        {
            try { Directory.Delete(tmpD, true); } catch { }
        }
    }

}
