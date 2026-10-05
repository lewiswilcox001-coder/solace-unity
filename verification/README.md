# Solace verification

How we prove the Unity project works without ever opening the editor GUI.
Lewis asked for visual proof, not just code. This directory is the answer.

## The ladder

### Level 1 — Real-API compile check (no Unity license needed) ✅ implementable now
`compile-check.sh` compiles every C# file in the project against the REAL
`UnityEngine.dll` / `UnityEditor.dll` extracted from the pinned Unity 6 LTS
editor tarball, using Roslyn. This proves **zero compile errors against the
actual Unity 6 API** — not stubs, not guesses.

- `Solace.Core` compiles against .NET ref assemblies only (no Unity refs by design).
- `Solace.Unity` compiles against the real `UnityEngine.dll` (+ minimal stubs
  ONLY for UPM package APIs unavailable without a package-manager run; each
  stub is listed in the script output so it stays honest).
- `Assets/Editor/**` compiles against real `UnityEngine.dll` + `UnityEditor.dll`.

Run: `./verification/compile-check.sh /path/to/extracted/editor`

### Level 2 — Batchmode import & script compile (Unity license REQUIRED)
Proves the project opens and imports cleanly in the real editor:
```
xvfb-run -a <editor>/Unity -batchmode -nographics -quit \
  -projectPath ~/workspace/solace-unity \
  -logFile verification/logs/batchmode-import.log \
  -executeMethod Solace.Unity.Editor.VerificationHarness.CompileCheck
```
Exit code != 0 or any `error` in the log = fail. **Blocked until a Unity
license is available** (see below).

### Level 3 — Screenshot verification pass (Unity license REQUIRED)
The photo-review loop. A scripted, deterministic run captured as frames:
1. `VerificationHarness` (Editor script, `-executeMethod`, run under
   `xvfb-run` WITHOUT `-batchmode` so the renderer is alive) OR a headless
   Linux player build containing `VerificationDirector`.
2. Fixed seed (12345), fixed camera poses at fixed sim-times:
   - t=5s   wide establishing shot (valley + Solace mid-frame)
   - t=20s  close follow shot (character readability)
   - t=40s  POI vista (nearest undiscovered point of interest)
   - t=60s  night shot (time-of-day forced to 23:00, fireflies/lighting)
   - t=80s  HUD close-up (all panels visible)
3. Frames land in `verification/shots/` as `shot-<pose>-<seed>.png`.
4. The run also asserts: no exceptions logged, sim time advanced, journal
   gained entries, agent changed goals at least once.

**Why not batchmode for screenshots:** Unity's batchmode has no render loop —
`ScreenCapture.CaptureScreenshot` returns blank frames. Screenshots require a
real render context: editor-under-Xvfb or a player build under Xvfb with
software GL (Mesa llvmpipe is present on this machine).

## The license situation (read this before asking "why not just run it")

Running the Unity editor in ANY mode — including `-batchmode` — requires an
activated Unity license. This machine has no Unity ID and no license file,
and per policy we do not collect credentials. The manual activation path,
for when Lewis (or CI) wants to unlock Level 2/3 here:

1. Generate an activation file: `<editor>/Unity -batchmode -createManualActivationFile -logFile /dev/stdout -quit`
   → produces `Unity_v6000.3.25f1.alf`
2. Upload the `.alf` at https://license.unity3d.com/manual (Unity ID sign-in),
   download the `.ulf` license file.
3. Activate: `<editor>/Unity -batchmode -manualLicenseFile ./Unity_v6000.3.25f1.ulf -logFile /dev/stdout -quit`
4. Then Levels 2 and 3 work. Licenses are machine-bound; repeat per machine.

Until then, Level 1 is the gate: nothing merges that doesn't compile clean
against the real Unity 6 assemblies.

## Layout
- `compile-check.sh` — Level 1 script
- `logs/` — batchmode/build logs (gitignored)
- `shots/` — captured frames (gitignored except `.gitkeep`)
- `README.md` — this file
