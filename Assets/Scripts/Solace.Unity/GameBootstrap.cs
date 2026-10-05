// Solace.Unity — root of the presentation layer.
//
// Owns the Simulation, builds every view, steps the sim each frame, wires the
// HUD, detects lineage chapter changes, and handles save/load with offline
// progress. Thin glue: all truth lives in Solace.Core.
//
// Bootstrapping: a RuntimeInitializeOnLoadMethod creates the bootstrap after
// scene load (Main.unity is intentionally empty — everything is built in
// code). The editor VerificationHarness calls BuildShellEditor() + StartNewLife
// directly instead (edit mode has no Start/Awake-driven flow).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        /// <summary>The authoritative simulation. Null until a life is started.</summary>
        public Simulation Sim { get; private set; }

        public int Seed { get; private set; }
        public ObserverCamera Camera { get; private set; }
        public HudController Hud { get; private set; }

        /// <summary>Protagonist's rig root (for camera framing / verification).</summary>
        public Transform AgentRoot
        {
            get { return _agentView != null ? _agentView.Root : null; }
        }

        private readonly List<ISimView> _views = new List<ISimView>();
        private GameObject _worldRoot;
        private GameObject _actorRoot;
        private GameObject _agentRootGO;
        private AgentView _agentView;
        private int _seenGeneration = 1;
        private float _lastSaveGameTime = -9999f;
        private bool _started;

        private const float AutosaveGameInterval = 3600f; // one game-hour

        // -- lifecycle ---------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            Ensure();
        }

        public static GameBootstrap Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GameBootstrap");
            return go.AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (_started) return;
            _started = true;
            BuildShell();
            if (!TryBootFromSave())
                StartNewLife(UnityEngine.Random.Range(1, 1000000000));
            Application.quitting += SaveNow;
        }

        /// <summary>Edit-mode entry for the verification harness: builds the
        /// shell (camera, HUD) without touching saves or starting a life.</summary>
        public void BuildShellEditor()
        {
            if (_started) return;
            _started = true;
            BuildShell();
        }

        public void RegisterView(ISimView view)
        {
            if (view != null && !_views.Contains(view)) _views.Add(view);
        }

        public void UnregisterView(ISimView view)
        {
            _views.Remove(view);
        }

        // -- life management ----------------------------------------------------

        /// <summary>Starts a fresh life on the given seed, rebuilding all views.</summary>
        public void StartNewLife(int seed)
        {
            TeardownWorld();
            Sim = Simulation.NewLife(seed);
            Seed = seed;
            _seenGeneration = Sim.State.Lineage.Generation;
            _lastSaveGameTime = Sim.State.ElapsedSeconds;
            BuildWorld();
            SyncAllViews();
            if (Camera != null) Camera.SnapToAgent();
            ShowToast("A new life begins — seed " + seed, 5f);
            Debug.Log("[Solace] New life started, seed " + seed);
        }

        private bool TryBootFromSave()
        {
            string path = SavePath;
            if (!File.Exists(path)) return false;
            try
            {
                var state = SaveSystem.Load(File.ReadAllText(path));
                int journalBefore = state.Journal.Count;
                TimeSpan away = ReadAwayTime();
                if (away.TotalSeconds > 5.0)
                    SaveSystem.ApplyOfflineProgress(state, away, OfflineMode.LivingWorld);
                Sim = new Simulation(state);
                Seed = state.Seed;
                _seenGeneration = state.Lineage.Generation;
                _lastSaveGameTime = state.ElapsedSeconds;
                BuildWorld();
                SyncAllViews();
                if (Camera != null) Camera.SnapToAgent();
                int gained = state.Journal.Count - journalBefore;
                if (gained > 0)
                {
                    var recent = state.Journal.Recent(Math.Min(3, gained));
                    var sb = new System.Text.StringBuilder();
                    sb.Append("While you were away… (").Append(gained).Append(" new)");
                    for (int i = recent.Count - 1; i >= 0; i--)
                    {
                        sb.Append("\n• ");
                        string t = recent[i].Text;
                        sb.Append(t.Length > 110 ? t.Substring(0, 110) + "…" : t);
                    }
                    ShowToast(sb.ToString(), 9f);
                }
                else
                {
                    ShowToast("Welcome back. The vale kept your place.", 5f);
                }
                Debug.Log("[Solace] Save loaded (seed " + Seed + ", +" + gained + " away entries).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] Save load failed, starting fresh: " + ex.Message);
                return false;
            }
        }

        // -- per-frame ----------------------------------------------------------

        private void Update()
        {
            if (Sim == null) return;
            Sim.Step(Time.deltaTime);

            int gen = Sim.State.Lineage.Generation;
            if (gen != _seenGeneration)
            {
                _seenGeneration = gen;
                OnChapterChanged(gen);
            }

            if (Sim.State.ElapsedSeconds - _lastSaveGameTime > AutosaveGameInterval)
                SaveNow();

            if (Input.GetKeyDown(KeyCode.N) && !Hud.IsTyping)
            {
                SaveNow();
                StartNewLife(UnityEngine.Random.Range(1, 1000000000));
            }
            if (Input.GetKeyDown(KeyCode.Escape)) Hud.CloseAllPanels();
        }

        private void OnChapterChanged(int gen)
        {
            string title = "Chapter " + UiKit.Roman(gen);
            string name = Sim.State.Agent != null ? Sim.State.Agent.Name : "";
            ShowChapterCard(title, name);
            if (Camera != null) Camera.SnapToAgent();
            Debug.Log("[Solace] " + title + " — " + name);
        }

        /// <summary>Drives every registered view once (used by the verification harness).</summary>
        public void SyncAllViews()
        {
            if (Sim == null) return;
            GameState state = Sim.State;
            for (int i = 0; i < _views.Count; i++)
            {
                try { _views[i].SyncFromState(state); }
                catch (Exception ex) { Debug.LogError("[Solace] view sync failed: " + ex); }
            }
        }

        /// <summary>Verification-only: shifts elapsed time so TimeOfDay == hour.</summary>
        public void DebugSetTimeOfDay(float hour)
        {
            if (Sim == null) return;
            float cur = Sim.State.TimeOfDay;
            float delta = (hour - cur + 24f) % 24f;
            Sim.State.ElapsedSeconds += delta * 3600f;
        }

        // -- save / load --------------------------------------------------------

        private static string SaveDir
        {
            get { return Path.Combine(Application.persistentDataPath, "Solace"); }
        }

        private static string SavePath
        {
            get { return Path.Combine(SaveDir, "save.json"); }
        }

        /// <summary>Atomic-ish save: write temp, then move over the old file.</summary>
        public void SaveNow()
        {
            if (Sim == null) return;
            try
            {
                Directory.CreateDirectory(SaveDir);
                string json = SaveSystem.Save(Sim.State);
                string tmp = Path.Combine(SaveDir, "save.json.tmp");
                string dst = SavePath;
                File.WriteAllText(tmp, json);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
                File.WriteAllText(Path.Combine(SaveDir, "save-meta.txt"),
                                  DateTime.UtcNow.Ticks.ToString());
                _lastSaveGameTime = Sim.State.ElapsedSeconds;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] Save failed: " + ex.Message);
            }
        }

        private static TimeSpan ReadAwayTime()
        {
            try
            {
                string meta = Path.Combine(SaveDir, "save-meta.txt");
                if (!File.Exists(meta)) return TimeSpan.Zero;
                long ticks = long.Parse(File.ReadAllText(meta).Trim());
                TimeSpan away = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                return away.TotalSeconds > 0 ? away : TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        // -- construction --------------------------------------------------------

        private void BuildShell()
        {
            var camGO = new GameObject("ObserverCamera");
            Camera = camGO.AddComponent<ObserverCamera>();

            var hudGO = new GameObject("HUD");
            Hud = hudGO.AddComponent<HudController>();
            Hud.Build(this);

            // Headless-verification driver; dormant unless --verify is passed.
            gameObject.AddComponent<Verification.VerificationDirector>();
        }

        private void BuildWorld()
        {
            WorldData world = Sim.State.World;

            _worldRoot = new GameObject("World");
            var terrain = _worldRoot.AddComponent<TerrainBuilder>();
            terrain.Build(world);
            var scatter = _worldRoot.AddComponent<ScatterSystem>();
            scatter.Build(world);
            var dens = _worldRoot.AddComponent<DenBuilder>();
            dens.Build(world);
            var colossi = _worldRoot.AddComponent<ColossusView>();
            colossi.Build(world);
            var daynight = _worldRoot.AddComponent<DayNightCycle>();
            daynight.Build(world);

            _actorRoot = new GameObject("Actors");
            _actorRoot.AddComponent<EntityViewManager>();
            _actorRoot.AddComponent<KitViewManager>();

            _agentRootGO = new GameObject("Agent");
            _agentView = _agentRootGO.AddComponent<AgentView>();
            _agentView.Build();
        }

        private void TeardownWorld()
        {
            if (_worldRoot != null) Destroy(_worldRoot);
            if (_actorRoot != null) Destroy(_actorRoot);
            if (_agentRootGO != null) Destroy(_agentRootGO);
            _views.Clear();
            _agentView = null;
        }

        // -- HUD helpers ----------------------------------------------------------

        public void ShowToast(string text, float duration)
        {
            if (Hud != null) Hud.ShowToast(text, duration);
        }

        public void ShowChapterCard(string title, string subtitle)
        {
            if (Hud != null) Hud.ShowChapterCard(title, subtitle);
        }
    }
}
