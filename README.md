# Solace — Unity 6 Project

The real build of **Solace**: an autonomous open-world RPG where an AI protagonist
lives an unscripted life. Watch-only, ambient, Tamagotchi-like — every journey is
unique. (Prototype era is over; this is the actual game.)

## Required Unity version

**Unity 6.3 LTS — `6000.3.25f1`** (exact patch, pinned in
`ProjectSettings/ProjectVersion.txt`).

Install via Unity Hub: *Installs → Install Editor →* pick **Unity 6.3 LTS
(6000.3.25f1)** from the archive, then add the required platform modules
(Windows / macOS / your target). Do not open this project with any other editor
version — Unity will offer to upgrade the project and that breaks the pin.

## Opening the project

1. Unity Hub → **Add** → select this folder (`solace-unity`) from disk.
2. Open with **6000.3.25f1**.
3. On first open, `Assets/Editor/ProjectSetup.cs` runs automatically: it creates a
   URP pipeline asset at `Assets/Settings/SolaceURPPipeline.asset` (plus its
   renderer) and assigns it in Graphics Settings. Check the Console for the
   `[Solace] ProjectSetup` log line.
4. Open **Assets/Scenes/Main.unity**.
5. Press **Play**.

## Controls (planned)

| Input | Action |
|---|---|
| `H` | Talk |
| `J` | Journal |
| `I` | Inventory |
| `M` | Map |
| `N` | New journey |
| Mouse drag | Orbit camera |
| Mouse wheel | Zoom |

## Architecture

```
Assets/Scripts/Solace.Core/    Pure simulation — plain .NET, zero UnityEngine
                               references (enforced by the asmdef). The world,
                               the AI protagonist, the rules of the journey.
                               Fully unit-testable.

Assets/Scripts/Solace.Unity/   Presentation glue — MonoBehaviours, bootstrap,
  Character/                   camera, input, and rendering adapters that
  UI/                          translate the simulation into a living scene.
                               References Solace.Core, never the reverse.

Assets/Editor/                 Editor-only tooling (first-open project setup).

tests/                         Test suites for the simulation (see tests/ when added).
```

Data flows one way: **Core simulates → Unity presents**. The Unity layer never
contains game logic; the Core layer never touches the engine.

## Art policy

**All art is code-generated at runtime.** There are zero external art assets in
this repository and no asset-store dependencies — by design there are no missing
meshes, no missing textures, and no missing-dependency errors. The world, the
character, and every prop are built procedurally in code. (The earlier web
prototype was a placeholder look; this build targets real game feel.)

## Packages

- `com.unity.render-pipelines.universal` **17.3.0** — the URP release paired
  with the Unity 6.3 line.
- `com.unity.test-framework` **1.4.6** — for the simulation test suites.

Nothing else. Keep it that way unless a package earns its place.
