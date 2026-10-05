# Solace.Core

The pure-C# simulation heart of SOLACE. **No UnityEngine references, no external
packages.** Compiles as plain .NET (netstandard2.1-compatible, C# ≤ 9). All game
truth lives here; presentation (Unity), language, and platform shells only read
`GameState` and call the entry points below.

**Setting (client direction, Oct 2026):** a misty highland vale where no humans
ever existed. The protagonist is a lantern-fox-like creature — slender,
long-limbed, with a luminous core in his chest; **light is life**: the Energy
need *is* light, and sickness reads as a dimming glow. The vale holds mistmoor,
foxpine woods, fell crags, a winding river (which glows at night) widening into
a still loch, a den-site of his kind with warm ember-hollows, and on the high
fell a hollow hive — chitin-arch ruins of the insectile ones, gone long ago.
Kilometer-tall tree-walkers cross the valley slow as weather; seed-isles drift
the loch. And the game is a **lineage, not a life**: bond, kits, aging,
sickness, death — and on death the chapter closes and the player follows the
eldest kit. Death is a chapter ending, never game over.

## Files

| File | Contents |
|---|---|
| `SeededRandom.cs` | Mulberry32 PRNG; domain-partitioned streams (`Derive`); state save/restore |
| `Math2D.cs` | `V2` struct (X/Z plane) + `MathX` scalar helpers |
| `Noise.cs` | Integer-hashed deterministic 2D value noise + `Fbm` / `Ridged` |
| `Json.cs` | Hand-rolled JSON DOM, writer, recursive-descent parser (no external lib) |
| `WorldGen.cs` | `WorldConfig`, `WorldData`, `PointOfInterest`, `WorldGenerator.Generate` |
| `AgentBrain.cs` | `AgentState`, `Personality`, `PlayerInfluence`, 9 `AgentAction`s, `AgentBrain` |
| `Entities.cs` | `EntityState`, `EntityBehaviors`, `Combat` |
| `Journal.cs` | `JournalEntry`, `Journal` (cap 300, salience eviction) |
| `Memory.cs` | `BeliefStore` (revision history), `SocialMemory` (trust, presence) |
| `Inventory.cs` | Sword, bread, potions, keepsakes |
| `GameState.cs` | `GameState`, `Weather`, `OfflineMode`, `RngStates`, `ToolBudgetState` |
| `SaveSystem.cs` | `Save`/`Load`, `ApplyOfflineProgress`, `KillAgent`, `RollWeather` |
| `Companion.cs` | `Companion.Respond` — template dialogue over live state (truth contract) |
| `CompanionTools.cs` | `ICompanionTool`, `ToolRegistry` + 4 built-in tools |
| `Simulation.cs` | Owns `GameState`; fixed-step loop; `NewLife(seed)` factory |
| `Lineage.cs` | `LifeStage`, `SicknessKind`, `Bond`, `Tale`, `ChapterRecord`, `LineageState`, `KitState` + `KitBrain`, `LineageSystem` (aging, sickness, tales, bonding, succession) |
| `Colossi.cs` | `ColossusKind`, `ColossusState`, `ColossusSystem` (tree-walkers, seed-isles, night-river glow flag) |

## Entry points for Unity glue

```csharp
// Start a life
var sim = Simulation.NewLife(seed: 12345);

// Per frame (call from MonoBehaviour.Update)
sim.Step(Time.deltaTime);           // fixed 1/30s game-steps, TimeScale = 60 game-sec per real-sec

// Read state for rendering / UI
GameState s = sim.State;
s.Agent.X / s.Agent.Z / s.Agent.Facing      // Solace's transform
s.Agent.CurrentActivity                     // human-readable, e.g. "picking berries"
s.TimeOfDay                                 // 0..24
s.World.SampleHeight(x, z) / s.World.IsWater(x, z) / s.World.GetBiome(x, z)

// Save / load (strings; store wherever the platform shell wants)
string json = SaveSystem.Save(sim.State);
GameState loaded = SaveSystem.Load(json);
var sim2 = new Simulation(loaded);          // continues deterministically

// Away continuity (call on resume; mode is player-chosen)
SaveSystem.ApplyOfflineProgress(state, TimeSpan away, OfflineMode.LivingWorld);

// Conversation (truth contract: facts come from the state, unknown = "I don't know")
// Status now reports glow, age, and generation; night-river glow is mentioned.
CompanionReply r = Companion.Respond(state, playerText);
// r.Intent: greeting|status|reason|suggestion|recap|memory|help|farewell|whoareyou|reflect
// r.Influence != null when the player suggested something (already logged on the agent)

// Validated tools (spawn_encounter, set_weather, add_journal, reveal_poi)
var tools = ToolRegistry.CreateDefault();
string result = tools.Invoke(state, "set_weather", "weather=Rain"); // "OK: ..." / "ERROR: ..."
```

## Key types

- `Biome`: `Mistmoor, Foxpine, FellCrag, SnowPeak, Riverbank, DenGrounds`
  (renamed from `Moorland, Pinewood, …, HamletGrounds`; int order preserved, so
  biome ints are save-compatible)
- `PoiType`: `Den, InsectileRuin, Cairn, EmberHollow, GlowberryBush, RuinSite, Overlook`
  (renamed from `Hamlet, BrochRuin, Campfire, BerryBush, …`; saved type *names*
  map compatibly: `Hamlet→Den`, `BrochRuin→InsectileRuin`, `Campfire→EmberHollow`,
  `BerryBush→GlowberryBush`)
- `EntityKind`: `Deer, Predator, Kindred, Rabbit`
  (renamed from `Wolf→Predator` — the gloom-maw — and `Villager→Kindred`; int
  order preserved; saved kind names map compatibly)
- `PointOfInterest`: `Id, Type, Name, X, Z, Radius, Discovered, LearnedName, Stock, Looted`
  (`DisplayName` hides the true name until `LearnedName`.)
- `Weather`: `Clear, Cloudy, Rain, Storm`
- `JournalCategory`: `Discovery, Combat, Social, Survival, Weather, Travel, Reflection, System, Chapter`
  — every entry carries `Generation`; `Chapter` marks chapter boundaries.
- `LifeStage`: `Kit (<1y), Juvenile (<3y), Adult, Elder (≥65% of lifespan)`
- `SicknessKind`: `None, DimCough, GutTwist, LightFever` + `SicknessSeverity` 0..1
- `LineageState` (on `GameState.Lineage`): `Generation, Chapters (ChapterRecord list),
  ProtagonistId, Tales (Tale list), KinEntityIds, ChapterStartTime, NextTaleId, NextAgentId`
- `Tale`: `Id, Title, LessonTrait, LessonAmount, OriginGeneration, OriginEvent`
  (cap 12 distilled per generation; told at rest near kits/kindred)
- `Bond`: `PartnerId, Strength (0..1), SinceStrongAt, LitterBorn`
- `KitState`: `Id, Name, Age (game-years), ParentId, MotherId, FatherId, LightShade,
  Forage/Notice/Hide (learned skills 0..1), FollowingParent, IsAlive, State
  (Follow|Play|Eat|Hide|Sleep)` — needs are Energy/Hunger/Health/Mood only.
- `ColossusKind`: `TreeWalker, SeedIsle`; `ColossusState`: `Id, Kind, X, Z,
  Heading, SpeedMPerDay (25–45 / 8–18), RadiusMeters, LastNotedAt`
- `RelationshipLevel`: `Stranger → Acquaintance → Familiar → Friend → Confidant`
- Actions (brain): `Flee, Fight, Eat, Drink, Rest, Explore, Observe, Greet, SeekBond, Loot`
  — `sim.Brain.LastDecisionTrace` holds `{ ActionName, Components[], TotalScore, Reason }`
  for legibility; `state.LastDecision` persists it across saves.
- `AgentState` lineage additions: `Name, Age (game-years), LifespanYears (~14–20),
  IsProtagonist, Generation, MotherId/FatherId, TalesKnown (ids), Bond, LightShade (0..1 hue),
  Sickness/SicknessSeverity, VigorForeshadowed, LastTaleToldAt`;
  computed: `Glow` 0..1 = 0.5·Energy/100 + 0.3·Health/100 + 0.2·(1−SicknessSeverity)
  (the readable signal — sickness/dimming reads in the glow),
  `MaxEnergy` (declines in Elder), `MaxSpeedFactor` (elders slower),
  `EffectiveCuriosity` (kits more curious), `EffectiveCaution` (elders warier).
  `Energy` is kept as the field name (API stability) but documented as LIGHT.

## Determinism contract

- RNG streams are partitioned: `"world"` (generation only), `"ai"` (decisions),
  `"event"` (world events, weather, encounters), `"lineage"` (aging, sickness,
  kits, bonding, succession), `"colossus"` (colossi drift). Stream positions
  persist in `GameState.Rng` (+ `GameState.StepRemainder` for the fixed-step
  accumulator). `Simulation` exposes `AiRng`, `EventRng`, `LineageRng`,
  `ColossusRng`.
- Same seed + same `Step` calls + same inputs → same consequential state,
  including across save/load. Save schema is v2 (v1 loads; pre-reframe enum
  names map compatibly).
- No `Math.Sin`-style hashing, no `string.GetHashCode`, no wall-clock or GUID
  use anywhere in simulation code. Float math is IEEE-754 only.

## Design notes / deliberate simplifications vs. the bible

- **Death = chapter ending, never game over.** `SaveSystem.KillAgent` journals the
  chapter-close (`JournalCategory.Chapter`) and `LineageSystem.SucceedOnDeath`
  passes the protagonist flag to the eldest living kit (tales →
  `Personality.Nudge`, `KnownPoiIds` persist — POI discovery is never reset,
  `LightShade` inherited ±0.06); with no living kit, a young distant kin arrives
  at the den, journaled honestly. Old-age death is always foreshadowed
  (`VigorForeshadowed` at 92% of lifespan) then a rising daily chance — never
  sudden. 1 game-year = 8 game-days (`LineageSystem.DaysPerYear`); lifespans
  seed 14–20 years.
- **Kits** (`KitBrain`: Follow/Play/Eat/Hide/Sleep) learn Forage/Notice/Hide by
  proximity to the parent's activity; they are fed when the parent eats nearby
  or by kindred, and can starve if unfed (live sim only — the away-sim bounds
  them). At age 3 a kit becomes a kindred entity and leaves the den.
- **Bonding:** repeated greetings with a trusted adult kindred deepen `Bond`
  (+0.08); crossing 0.7 journals "The Bonding" and distills a tale; after
  2 game-days with both adults, `LineageSystem.TickBonding` births 1–3 kits at
  the den. `SeekBondAction` (topic `social`) keeps the pair close.
- **Sickness:** rest quality ×(1−0.55·severity) cures; eating lowers severity;
  severity 1 drains Health 0.15/s. Contracted in bad weather while weak, near
  sick kindred (<8m), or by gorging while starving (gut-twist).
- **Colossi** are sim-light: a handful of entities, coarse per-step drift
  (25–45 m/day tree-walkers, 8–18 m/day seed-isles), journaled when within
  160m (max once per 2 days). `WorldData.NightRiverGlow` is a presentation flag.
- **Naming (no humans, ever):** species unnamed in code (the lantern-fox);
  kindred names are things that gather around light (Vesper, Tallow, Ember,
  Moth, Sable, Lumen, Ash, Wick); dens (Emberden, Stillden, …); the hive
  (the Hollow Hive, Vessaril, …); vales (Glen Vane … plus Vesper Vale, Mothmere).
  Human traces were reframed, not deleted: crofts→dens, broch→chitin-arch hive,
  bothy fires→ember-hollows, oatcakes→seedcakes, cloak→(pulled light in close).
- **The away-sim is lineage-fair:** aging clamps at lifespan (never an unfair
  old-age death offline), sickness eases and never kills, kits are bounded
  (hunger ≤85, health floor), colossi still drift.
- **Needs model:** Energy (=light)/Hunger/Thirst/Health/Mood/Curiosity at narrative
  timescales (a full hunger cycle ≈ 35 game-minutes).
- **Belief revision** keeps superseded claims in history; contradiction lowers
  confidence rather than deleting.
- **Companion** is template-driven (Tier 0 in bible terms). The semantic-act
  structure (`CompanionReply.Intent`) is the seam where a local/connected model
  can later paraphrase — the auditable payload stays here.
- **Offline LivingWorld** simulates max 4 in-game hours in 5-minute abstract
  ticks, then quiet routine; danger/novelty budgets bound drama; real away time
  capped at 7 days.
