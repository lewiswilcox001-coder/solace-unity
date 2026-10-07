// Solace.Unity.Verification — headless screenshot verification for player builds.
//
// Dormant unless the process is launched with --verify (optional --seed=NNNN).
// When active, it starts a fixed-seed life and drives a scripted ~90s pass:
//   t=5s   wide establishing shot (valley + Solace mid-frame)
//   t=20s  close follow shot (character readability)
//   t=40s  POI vista (nearest POI to the agent)
//   t=60s  night shot (time-of-day forced to 23:00)
//   t=80s  HUD close-up (all panels visible)
// Frames land in <exeDir>/verification/shots/shot-<pose>-<seed>.png.
// Assertions: no logged errors, sim time advanced, journal grew, the agent
// changed goals at least once. The result is written to
// <exeDir>/verification-result.txt and logged; the process then quits.
// (Application.Quit has no reliable exit-code overload across Unity versions,
// so CI should read the result file: first line is PASS or FAIL.)
//
// This runs inside a real player, so the render loop is alive — unlike
// -batchmode, which cannot render. See also the editor-side
// Solace.Unity.Editor.VerificationHarness, which performs the same pass under
// xvfb-run WITHOUT -batchmode.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity.Verification
{
    public class VerificationDirector : MonoBehaviour
    {
        private bool _active;
        private int _seed = 12345;
        private GameBootstrap _boot;
        private float _t;
        private int _phase;
        private string _shotDir;
        private int _journal0;
        private double _elapsed0;
        private readonly HashSet<string> _goals = new HashSet<string>();
        private int _errors;
        private bool _done;

        private static readonly string[] PoseNames = { "wide", "close", "poi", "night", "hud" };
        private static readonly float[] PoseTimes = { 5f, 20f, 40f, 60f, 80f };

        private void Start()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "--verify") _active = true;
                if (arg.StartsWith("--seed=")) int.TryParse(arg.Substring(7), out _seed);
            }
            if (!_active) { enabled = false; return; }

            _boot = GameBootstrap.Ensure();
            _boot.BuildShellEditor();
            _boot.StartNewLife(_seed);

            string exeDir = Directory.GetParent(Application.dataPath).FullName;
            _shotDir = Path.Combine(Path.Combine(exeDir, "verification"), "shots");
            Directory.CreateDirectory(_shotDir);

            _journal0 = _boot.Sim.State.Journal.Count;
            _elapsed0 = _boot.Sim.State.ElapsedSeconds;
            Application.logMessageReceived += OnLog;
            Debug.Log("[Verification] Director active, seed " + _seed + ", shots -> " + _shotDir);
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                Debug.LogWarning("[Verification] captured error: " + condition);
            }
        }

        private void Update()
        {
            if (!_active || _done || _boot == null || _boot.Sim == null) return;
            _t += Time.deltaTime;

            string goal = _boot.Sim.State.Agent.CurrentGoal;
            if (!string.IsNullOrEmpty(goal)) _goals.Add(goal);

            while (_phase < PoseTimes.Length && _t >= PoseTimes[_phase])
            {
                RunPose(_phase);
                _phase++;
            }

            if (_t >= 88f) Finish();
        }

        private void RunPose(int phase)
        {
            var sim = _boot.Sim;
            AgentState a = sim.State.Agent;
            var cam = _boot.Camera;
            var here = new Vector3(a.X, sim.State.World.SampleHeight(a.X, a.Z), a.Z);
            switch (phase)
            {
                case 0: // wide establishing
                    cam.SetPose(here + new Vector3(-26f, 16f, -30f), here + new Vector3(10f, 4f, 10f), 12f);
                    break;
                case 1: // close follow
                    cam.SetPose(here + new Vector3(3.2f, 2.2f, -4.2f), here + new Vector3(0f, 1f, 0f), 12f);
                    break;
                case 2: // POI vista: nearest POI to the agent
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
                            cam.SetPose(mid + new Vector3(-18f, 12f, -18f), poiPos + new Vector3(0f, 3f, 0f), 12f);
                        }
                        break;
                    }
                case 3: // night, forced 23:00
                    _boot.DebugSetTimeOfDay(23f);
                    _boot.SyncAllViews();
                    cam.SetPose(here + new Vector3(-14f, 8f, -16f), here + new Vector3(0f, 2f, 0f), 12f);
                    break;
                case 4: // HUD close-up
                    cam.SetPose(here + new Vector3(4f, 2.6f, -5f), here + new Vector3(0f, 1f, 0f), 10f);
                    break;
            }
            // Let the pose render a few frames before capturing.
            // (Capture happens on the next pose boundary / finish; the final
            // capture for the hud pose happens in Finish.)
            string path = Path.Combine(_shotDir, "shot-" + PoseNames[phase] + "-" + _seed + ".png");
            // Capture at end of frame via coroutine-less delay: store and shoot.
            _pendingShot = path;
            _pendingShotAt = _t + 1.5f;
        }

        private string _pendingShot;
        private float _pendingShotAt = -1f;

        private void LateUpdate()
        {
            if (!_active || _done) return;
            if (_pendingShot != null && _t >= _pendingShotAt)
            {
                ScreenCapture.CaptureScreenshot(_pendingShot);
                Debug.Log("[Verification] shot -> " + _pendingShot);
                _pendingShot = null;
            }
        }

        private void Finish()
        {
            _done = true;
            var fails = new List<string>();
            if (_errors > 0) fails.Add(_errors + " errors logged");
            double advanced = _boot.Sim.State.ElapsedSeconds - _elapsed0;
            if (advanced < 30f) fails.Add("sim time advanced only " + advanced.ToString("F1") + "s");
            int grown = _boot.Sim.State.Journal.Count - _journal0;
            if (grown <= 0) fails.Add("journal did not grow");
            if (_goals.Count < 2) fails.Add("agent changed goals only " + _goals.Count + " time(s)");

            string result = fails.Count == 0 ? "PASS" : "FAIL";
            string exeDir = Directory.GetParent(Application.dataPath).FullName;
            string resultPath = Path.Combine(exeDir, "verification-result.txt");
            string body = result + " seed=" + _seed +
                          " elapsed+" + advanced.ToString("F1") +
                          " journal+" + grown +
                          " goals=" + _goals.Count +
                          " errors=" + _errors +
                          (fails.Count > 0 ? " :: " + string.Join("; ", fails.ToArray()) : "");
            try { File.WriteAllText(resultPath, body + "\n"); } catch { }
            Debug.Log("[Verification] " + body);
            Application.logMessageReceived -= OnLog;
            Application.Quit();
        }
    }
}
