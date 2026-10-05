// Solace.Unity.Editor — screenshot verification harness.
//
// Runs via:  xvfb-run -a <editor>/Unity -executeMethod Solace.Unity.Editor.VerificationHarness.Run
//            -projectPath ~/workspace/solace-unity -logFile verification/logs/harness.log
//
// TWO SUPPORTED PATHS (batchmode can NEVER render — ScreenCapture returns
// blank frames without a render loop):
//
//  Path A — editor under Xvfb (this file). The editor is launched WITHOUT
//  -batchmode so the Game view renders under the virtual display. Run()
//  opens Main.unity, builds the bootstrap shell in edit mode, starts a
//  fixed-seed life (12345), then drives the sim manually on
//  EditorApplication.update while forcing repaints and capturing screenshots:
//    t=5s   shot-wide-12345    establishing shot (valley + Solace mid-frame)
//    t=20s  shot-close-12345   close follow shot (character readability)
//    t=40s  shot-poi-12345     POI vista (nearest POI to the agent)
//    t=60s  shot-night-12345   night shot (time-of-day forced to 23:00)
//    t=80s  shot-hud-12345     HUD close-up (all panels visible)
//  Frames land in <project>/verification/shots/. Assertions: zero logged
//  errors, sim time advanced, journal grew, agent changed goals ≥ once.
//  PASS/FAIL is logged and the editor exits non-zero on failure.
//
//  Path B — headless player build. Build a Linux player including
//  Solace.Unity.Verification.VerificationDirector (added automatically by
//  GameBootstrap; dormant unless --verify is passed), then run under Xvfb:
//    xvfb-run -a ./Solace --verify --seed=12345
//  The director performs the same scripted pass inside the real render loop
//  and writes <exeDir>/verification/shots/shot-<pose>-<seed>.png plus
//  <exeDir>/verification-result.txt (first line PASS/FAIL —
//  Application.Quit has no reliable exit-code overload, so CI reads the file).
//
// NOTE: edit-mode rendering (Path A) depends on the Game view repainting;
// the harness forces repaints via InternalEditorUtility.RepaintAllViews().
// If shots come back blank in some environment, Path B is the fallback.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity.Editor
{
    public static class VerificationHarness
    {
        private const int Seed = 12345;
        private static readonly float[] PoseTimes = { 5f, 20f, 40f, 60f, 80f };
        private static readonly string[] PoseNames = { "wide", "close", "poi", "night", "hud" };

        public static void Run()
        {
            try
            {
                new Runner().Begin();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Verification] FAIL during setup: " + ex);
                try { EditorApplication.Exit(1); } catch { }
            }
        }

        private class Runner
        {
            private GameBootstrap _boot;
            private double _t0;
            private double _lastT;
            private int _phase;
            private string _shotDir;
            private int _journal0;
            private float _elapsed0;
            private readonly HashSet<string> _goals = new HashSet<string>();
            private int _errors;
            private string _pendingShot;
            private double _pendingShotAt = -1;
            private bool _done;

            public void Begin()
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                _boot = GameBootstrap.Ensure();
                _boot.BuildShellEditor();
                _boot.StartNewLife(Seed);

                string project = Directory.GetParent(Application.dataPath).FullName;
                _shotDir = Path.Combine(project, "verification", "shots");
                Directory.CreateDirectory(_shotDir);

                _journal0 = _boot.Sim.State.Journal.Count;
                _elapsed0 = _boot.Sim.State.ElapsedSeconds;
                Application.logMessageReceived += OnLog;
                _t0 = EditorApplication.timeSinceStartup;
                _lastT = _t0;
                EditorApplication.update += Tick;
                Debug.Log("[Verification] harness started, seed " + Seed + ", shots -> " + _shotDir);
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    _errors++;
                    Debug.LogWarning("[Verification] captured error: " + condition);
                }
            }

            private void Tick()
            {
                if (_done) return;
                try
                {
                    double now = EditorApplication.timeSinceStartup;
                    double dt = Math.Min(now - _lastT, 0.25);
                    _lastT = now;
                    double t = now - _t0;

                    _boot.Sim.Step((float)dt);
                    _boot.SyncAllViews();

                    string goal = _boot.Sim.State.Agent.CurrentGoal;
                    if (!string.IsNullOrEmpty(goal)) _goals.Add(goal);

                    // Fire the pending screenshot (after a repaint delay).
                    if (_pendingShot != null && t >= _pendingShotAt)
                    {
                        Repaint();
                        ScreenCapture.CaptureScreenshot(_pendingShot);
                        Debug.Log("[Verification] shot -> " + _pendingShot);
                        _pendingShot = null;
                    }

                    while (_phase < PoseTimes.Length && t >= PoseTimes[_phase])
                    {
                        RunPose(_phase);
                        _phase++;
                    }

                    if (t >= 88.0) Finish();
                }
                catch (Exception ex)
                {
                    _done = true;
                    Debug.LogError("[Verification] FAIL during run: " + ex);
                    Cleanup();
                    EditorApplication.Exit(1);
                }
            }

            private void RunPose(int phase)
            {
                var sim = _boot.Sim;
                AgentState a = sim.State.Agent;
                var cam = _boot.Camera;
                var here = new Vector3(a.X, sim.State.World.SampleHeight(a.X, a.Z), a.Z);
                switch (phase)
                {
                    case 0:
                        cam.SetPose(here + new Vector3(-26f, 16f, -30f), here + new Vector3(10f, 4f, 10f), 30f);
                        break;
                    case 1:
                        cam.SetPose(here + new Vector3(3.2f, 2.2f, -4.2f), here + new Vector3(0f, 1f, 0f), 30f);
                        break;
                    case 2:
                        {
                            PointOfInterest best = null;
                            float bestD = float.MaxValue;
                            foreach (var p in sim.State.World.Pois)
                            {
                                float dx = p.X - a.X, dz = p.Z - a.Z;
                                float d = dx * dx + dz * dz;
                                if (d < bestD) { bestD = d; best = p; }
                            }
                            if (best != null)
                            {
                                var poiPos = new Vector3(best.X, sim.State.World.SampleHeight(best.X, best.Z), best.Z);
                                var mid = (here + poiPos) * 0.5f;
                                cam.SetPose(mid + new Vector3(-18f, 12f, -18f), poiPos + new Vector3(0f, 3f, 0f), 30f);
                            }
                            break;
                        }
                    case 3:
                        _boot.DebugSetTimeOfDay(23f);
                        _boot.SyncAllViews();
                        cam.SetPose(here + new Vector3(-14f, 8f, -16f), here + new Vector3(0f, 2f, 0f), 30f);
                        break;
                    case 4:
                        cam.SetPose(here + new Vector3(4f, 2.6f, -5f), here + new Vector3(0f, 1f, 0f), 30f);
                        break;
                }
                _pendingShot = Path.Combine(_shotDir, "shot-" + PoseNames[phase] + "-" + Seed + ".png");
                _pendingShotAt = EditorApplication.timeSinceStartup - _t0 + 1.5;
                Repaint();
            }

            private static void Repaint()
            {
                try { UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
                catch (Exception ex) { Debug.LogWarning("[Verification] repaint failed: " + ex.Message); }
            }

            private void Finish()
            {
                _done = true;
                // Final pending shot first (hud pose).
                if (_pendingShot != null)
                {
                    Repaint();
                    ScreenCapture.CaptureScreenshot(_pendingShot);
                    Debug.Log("[Verification] shot -> " + _pendingShot);
                    _pendingShot = null;
                }

                var fails = new List<string>();
                if (_errors > 0) fails.Add(_errors + " errors logged");
                float advanced = _boot.Sim.State.ElapsedSeconds - _elapsed0;
                if (advanced < 30f) fails.Add("sim time advanced only " + advanced.ToString("F1") + "s");
                int grown = _boot.Sim.State.Journal.Count - _journal0;
                if (grown <= 0) fails.Add("journal did not grow");
                if (_goals.Count < 2) fails.Add("agent changed goals only " + _goals.Count + " time(s)");

                if (fails.Count == 0)
                    Debug.Log("[Verification] PASS seed=" + Seed +
                              " elapsed+" + advanced.ToString("F1") +
                              " journal+" + grown + " goals=" + _goals.Count);
                else
                    Debug.LogError("[Verification] FAIL: " + string.Join("; ", fails.ToArray()));

                Cleanup();
                EditorApplication.Exit(fails.Count == 0 ? 0 : 1);
            }

            private void Cleanup()
            {
                EditorApplication.update -= Tick;
                Application.logMessageReceived -= OnLog;
            }
        }
    }
}
