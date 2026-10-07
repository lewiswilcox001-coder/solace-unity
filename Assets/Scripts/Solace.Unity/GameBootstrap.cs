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

        /// <summary>Which save slot is active. Chosen on the slot menu; 0 by default.</summary>
        public int ActiveSlot { get; private set; }

        /// <summary>The active challenge run (mode, run-over state, speedrun timing).
        /// Standard for plain lives; loaded from the slot sidecar when present.</summary>
        public ChallengeState Challenge { get; private set; }

        /// <summary>True while the shared daily vale is open (not a save slot).</summary>
        public bool InDailyWorld { get; private set; }

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
        private double _lastSaveGameTime = -9999.0;
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
            // The slot menu drives the rest: pick a world to continue, or begin anew.
            UI.SaveSlotMenu.Show(this);
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
            StartNewLife(seed, ChallengeMode.Standard);
        }

        /// <summary>Starts a fresh life on the given seed under a challenge mode.</summary>
        public void StartNewLife(int seed, ChallengeMode mode)
        {
            TeardownWorld();
            Sim = ChallengeModes.NewChallengeLife(seed, mode);
            Challenge = ChallengeState.NewRun(mode);
            ChallengeModes.ActiveMode = mode;
            Seed = seed;
            // Golden hour opening: new lives begin at dusk for the cinematic.
            // (Loaded saves keep their own time.)
            Sim.State.StartHour = 17.5f;
            _seenGeneration = Sim.State.Lineage.Generation;
            _lastSaveGameTime = Sim.State.ElapsedSeconds;
            BuildWorld();
            SyncAllViews();
            if (Camera != null) Camera.SnapToAgent();
            // Cinematic opening for first-time experience.
            OpeningSequence.PlayForNewLife(this);
            Debug.Log("[Solace] New life started, seed " + seed);
        }

        /// <summary>Loads a save slot and starts watching it. Returns false when the slot is empty.</summary>
        public bool LoadSlot(int slot)
        {
            string json = SaveSlots.LoadSlotJson(SaveDir, slot);
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                InDailyWorld = false;
                ActiveSlot = slot;
                var state = SaveSystem.Load(json);
                int journalBefore = state.Journal.Count;
                TimeSpan away = SaveSlots.AwayTime(SaveDir, slot);
                if (away.TotalSeconds > 5.0)
                    SaveSystem.ApplyOfflineProgress(state, away, OfflineMode.LivingWorld);
                Sim = new Simulation(state);
                Seed = state.Seed;
                // Challenge state lives in the slot sidecar; standard when absent.
                Challenge = ChallengeSave.Load(SaveDir, slot) ?? ChallengeState.NewRun(ChallengeMode.Standard);
                ChallengeModes.ActiveMode = Challenge.Mode;
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
                Debug.LogWarning("[Solace] Save load failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Starts a fresh life on a random seed inside the given slot.</summary>
        public void NewLifeInSlot(int slot)
        {
            NewLifeInSlot(slot, ChallengeMode.Standard);
        }

        /// <summary>Starts a fresh life on a random seed inside the given slot, under a challenge mode.</summary>
        public void NewLifeInSlot(int slot, ChallengeMode mode)
        {
            InDailyWorld = false;
            ActiveSlot = slot;
            StartNewLife(UnityEngine.Random.Range(1, 1000000000), mode);
        }

        /// <summary>
        /// Opens today's shared vale: loads today's daily save if the player
        /// already wandered it, otherwise starts a fresh life on the daily
        /// seed (identical for every player). Records the streak visit.
        /// </summary>
        public void LoadDailyWorld()
        {
            DateTime today = DateTime.UtcNow.Date;
            string ds = DailyVale.DateString(today);
            int streak = DailyVale.RecordVisit(SaveDir, today);
            InDailyWorld = true;
            ActiveSlot = -1;

            string json = DailyVale.LoadDailyJson(SaveDir, today);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var state = SaveSystem.Load(json);
                    int journalBefore = state.Journal.Count;
                    TimeSpan away = DailyVale.DailyAwayTime(SaveDir, today);
                    if (away.TotalSeconds > 5.0)
                        SaveSystem.ApplyOfflineProgress(state, away, OfflineMode.LivingWorld);
                    Sim = new Simulation(state);
                    Seed = state.Seed;
                    state.DailyInfo = new DailyVisitInfo
                    {
                        IsDailyWorld = true,
                        DateString = ds,
                        StreakDays = streak
                    };
                    _seenGeneration = state.Lineage.Generation;
                    _lastSaveGameTime = state.ElapsedSeconds;
                    BuildWorld();
                    SyncAllViews();
                    if (Camera != null) Camera.SnapToAgent();
                    int gained = state.Journal.Count - journalBefore;
                    ShowToast("◆ Today's Vale · " + DailyVale.PrettyDate(ds) +
                              " · " + StreakText(streak) +
                              (gained > 0 ? " · " + gained + " new while away" : ""),
                              6f);
                    Debug.Log("[Solace] Daily vale loaded (" + ds + ", streak " + streak + ").");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Solace] Daily load failed, starting fresh: " + ex.Message);
                }
            }

            StartNewLife(DailyVale.SeedFor(today));
            Sim.State.DailyInfo = new DailyVisitInfo
            {
                IsDailyWorld = true,
                DateString = ds,
                StreakDays = streak
            };
            ShowToast("◆ Today's Vale · " + DailyVale.PrettyDate(ds) +
                      " · " + StreakText(streak), 6f);
            Debug.Log("[Solace] Daily vale begun (" + ds + ", streak " + streak + ").");
        }

        private static string StreakText(int streak)
        {
            if (streak <= 1) return "day 1 — come back tomorrow";
            return streak + "-day streak";
        }

        // -- per-frame ----------------------------------------------------------

        private void Update()
        {
            if (Sim == null) return;
            Sim.Step(Time.deltaTime);

            // Challenge director: peaceful sweeps, hardcore run-over, speedrun timing.
            bool wasOver = Challenge != null && Challenge.RunOver;
            ChallengeDirector.Tick(Sim, Challenge);
            if (Challenge != null)
            {
                if (!wasOver && Challenge.RunOver)
                    ShowToast("The hardcore run has ended — the vale keeps the tales.", 8f);
                if (Challenge.LastSpeedrunEntry != null)
                {
                    var entry = Challenge.LastSpeedrunEntry;
                    Challenge.LastSpeedrunEntry = null;
                    int rank = ChallengeSave.RecordRun(SaveDir, entry);
                    ShowToast(rank > 0
                        ? "◆ Speedrun complete — rank #" + rank + " (" +
                          ChallengeModes.FormatDuration(entry.GameSeconds) + " of vale-time)"
                        : "◆ Speedrun complete (" +
                          ChallengeModes.FormatDuration(entry.GameSeconds) + " of vale-time)", 10f);
                }
            }

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
                InDailyWorld = false;
                // A fresh life keeps the current slot's challenge mode.
                StartNewLife(UnityEngine.Random.Range(1, 1000000000),
                    Challenge != null ? Challenge.Mode : ChallengeMode.Standard);
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

        /// <summary>Save directory, shared by all slots.</summary>
        public static string SaveDir
        {
            get { return Path.Combine(Application.persistentDataPath, "Solace"); }
        }

        /// <summary>Atomic-ish save: slots rotate backups; daily worlds write to their date file.</summary>
        public void SaveNow()
        {
            if (Sim == null) return;
            try
            {
                if (InDailyWorld)
                {
                    DailyVale.SaveDaily(SaveDir, DateTime.UtcNow.Date, Sim.State);
                }
                else
                {
                    SaveSlots.SaveSlot(SaveDir, ActiveSlot, Sim.State);
                    SlotThumbnail.WriteSlotThumbnail(SaveDir, ActiveSlot, Sim.State.Seed);
                    ChallengeSave.Save(SaveDir, ActiveSlot, Challenge);
                }
                _lastSaveGameTime = Sim.State.ElapsedSeconds;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Solace] Save failed: " + ex.Message);
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
            var seasons = _worldRoot.AddComponent<SeasonView>();
            seasons.Build(world, terrain);
            var spirit = _worldRoot.AddComponent<SpiritFox>();
            spirit.Build(world);

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
