# Solace.Core

The pure-C# simulation heart of SOLACE. **No UnityEngine references, no external
packages.** Compiles as plain .NET (netstandard2.1-compatible, C# ≤ 9). All game
truth lives here; presentation (Unity), language, and platform shells only read
`GameState` and call the entry points below.

**Setting (client direction, Oct 2026):** a misty highland glen — heather
moorland, pinewoods, fell crags, a winding river widening into a still loch, a
crofting hamlet with peat fires, and an ancient broch (stone tower) on the high
fell. The reference photo was inspiration only; this identity was chosen to serve
the core fantasy: *an autonomous AI living a life worth watching* — a warm hearth
to return to, a wild horizon to long for, weather with character, and a mystery
with real historical texture.

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
CompanionReply r = Companion.Respond(state, playerText);
// r.Intent: greeting|status|reason|suggestion|recap|memory|help|farewell|whoareyou|reflect
// r.Influence != null when the player suggested something (already logged on the agent)

// Validated tools (spawn_encounter, set_weather, add_journal, reveal_poi)
var tools = ToolRegistry.CreateDefault();
string result = tools.Invoke(state, "set_weather", "weather=Rain"); // "OK: ..." / "ERROR: ..."
```

## Key types

- `Biome`: `Moorland, Pinewood, FellCrag, SnowPeak, Riverbank, HamletGrounds`
- `PoiType`: `Hamlet, BrochRuin, Cairn, Campfire, BerryBush, RuinSite, Overlook`
- `PointOfInterest`: `Id, Type, Name, X, Z, Radius, Discovered, LearnedName, Stock, Looted`
  (`DisplayName` hides the true name until `LearnedName`.)
- `Weather`: `Clear, Cloudy, Rain, Storm`
- `JournalCategory`: `Discovery, Combat, Social, Survival, Weather, Travel, Reflection, System`
- `RelationshipLevel`: `Stranger → Acquaintance → Familiar → Friend → Confidant`
- Actions (brain): `Flee, Fight, Eat, Drink, Rest, Explore, Observe, Greet, Loot`
  — `sim.Brain.LastDecisionTrace` holds `{ ActionName, Components[], TotalScore, Reason }`
  for legibility; `state.LastDecision` persists it across saves.

## Determinism contract

- RNG streams are partitioned: `"world"` (generation only), `"ai"` (decisions),
  `"event"` (world events, weather, encounters). Stream positions persist in
  `GameState.Rng` (+ `GameState.StepRemainder` for the fixed-step accumulator).
- Same seed + same `Step` calls + same inputs → same consequential state,
  including across save/load.
- No `Math.Sin`-style hashing, no `string.GetHashCode`, no wall-clock or GUID
  use anywhere in simulation code. Float math is IEEE-754 only.

## Design notes / deliberate simplifications vs. the bible

- **Death:** implemented as permanent (`IsAlive=false` + journaled cause). The
  bible leaves the death model open; the core supports it and the away-sim will
  not kill while away without a logged severe cause.
- **Needs model:** Energy/Hunger/Thirst/Health/Mood/Curiosity at narrative
  timescales (a full hunger cycle ≈ 35 game-minutes). Warmth/illness are not
  separately modeled yet — hooks exist (`Weather`, `Health`).
- **Belief revision** keeps superseded claims in history; contradiction lowers
  confidence rather than deleting.
- **Companion** is template-driven (Tier 0 in bible terms). The semantic-act
  structure (`CompanionReply.Intent`) is the seam where a local/connected model
  can later paraphrase — the auditable payload stays here.
- **Offline LivingWorld** simulates max 4 in-game hours in 5-minute abstract
  ticks, then quiet routine; danger/novelty budgets bound drama; real away time
  capped at 7 days.
