// Solace.Unity — cinematic auto-director.
//
// A BBC-nature-documentary camera brain for a watch-only game. Every few
// seconds it scores what's happening in the sim, picks the most interesting
// subject, and frames it with the right shot: wide vistas for calm, medium
// follow for life, close-ups for emotion (birth, death), low angles for
// danger. Every move is smooth — the camera glides, never whips.
//
// Press T to toggle. Default ON. Yields to OpeningSequence while it runs,
// and hands back to ObserverCamera when toggled off.
//
// Presentation only: never touches sim state, only drives the camera
// transform while ObserverCamera is disabled.
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class AutoDirector : MonoBehaviour
    {
        /// <summary>True while the director is driving the camera.</summary>
        public bool DirectorActive { get; private set; }

        private ObserverCamera _follow;
        private Camera _cam;
        private bool _initialized;

        // -- shot state -------------------------------------------------------
        private enum ShotKind { Wide, Medium, CloseUp, LowAngle }

        private struct Framing
        {
            public Vector3 Subject;   // world-space look target
            public ShotKind Shot;
            public float Interest;    // winning score
            public string Label;      // debug / future HUD
        }

        private Framing _current;
        private Framing _target;
        private float _shotAge;          // seconds in current shot
        private float _minShotTime = 6f; // don't cut faster than this (unless urgent)
        private float _scoreTimer;       // interest re-score clock

        // -- camera smoothing ---------------------------------------------------
        private Vector3 _smoothLook;
        private bool _hasLook;
        private float _cutBoost;       // temporary faster damping after a subject cut
        private float _pushT;          // close-up slow push-in clock
        private float _orbitT;         // wide-shot slow drift clock

        // -- event memory ---------------------------------------------------------
        private int _lastKitCount;
        private bool _lastAgentAlive = true;
        private float _birthEventUntil = -1f;
        private float _deathEventUntil = -1f;
        private OpeningSequence _opening; // cached; null once its GameObject is destroyed
        private float _openingRecheck;
        private Simulation _lastSim; // detect new lives (Sim object is recreated)

        // Accessibility: fade-cut state for reduced motion (fades instead of whip-pans).
        private UnityEngine.UI.Image _fade;
        private int _fadePhase; // 0 = idle, 1 = fading out, 2 = fading in
        private Framing _pendingFraming;
        private bool _hasPending;

        // Self-bootstrap: late-init once GameBootstrap's camera exists.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            var go = new GameObject("AutoDirector");
            go.AddComponent<AutoDirector>();
        }

        private void Update()
        {
            if (!_initialized)
            {
                var boot = GameBootstrap.Instance;
                if (boot == null || boot.Camera == null) return;
                _follow = boot.Camera;
                _cam = _follow.GetComponent<Camera>();
                if (_cam == null) return;
                _initialized = true;
                SetActive(true); // default ON for new players
            }

            // T toggles (not while typing in HUD). Gamepad L3 also toggles.
            var hud = GameBootstrap.Instance != null ? GameBootstrap.Instance.Hud : null;
            if (Input.GetKeyDown(KeyCode.T) && (hud == null || !hud.IsTyping))
                SetActive(!DirectorActive);
            if (GamepadInput.IsConnected && GamepadInput.GetButtonDown(PadButton.L3) &&
                (hud == null || !hud.IsTyping))
                SetActive(!DirectorActive);

            if (!DirectorActive) return;

            UpdateFadeCut();

            // Yield to the opening cinematic while it runs.
            // Cached lookup, re-checked every 5s (new lives spawn a new one).
            if (_opening == null && Time.time >= _openingRecheck)
            {
                _openingRecheck = Time.time + 5f;
                _opening = FindObjectOfType<OpeningSequence>();
            }
            if (_opening != null) return;

            var sim = GameBootstrap.Instance != null ? GameBootstrap.Instance.Sim : null;
            if (sim == null || sim.State.Agent == null) return;

            // New life (Sim recreated): reset event memory, re-check for opening.
            if (sim != _lastSim)
            {
                _lastSim = sim;
                _openingRecheck = 0f;
                _lastKitCount = CountLiveKits(sim);
                _lastAgentAlive = sim.State.Agent.IsAlive;
                _shotAge = 999f;
            }

            float now = Time.time;
            _scoreTimer -= Time.deltaTime;
            if (_scoreTimer <= 0f)
            {
                _scoreTimer = 0.4f;
                ScoreScene(sim, now);
            }

            DriveCamera(sim, now);
        }

        /// <summary>Enable/disable the director. Disabling hands back to ObserverCamera.</summary>
        public void SetActive(bool on)
        {
            if (!_initialized) return;
            if (on == DirectorActive) return;
            DirectorActive = on;
            if (_follow == null) return;
            if (on)
            {
                _follow.enabled = false;
                _hasLook = false;
                _shotAge = 999f; // force immediate framing
                _scoreTimer = 0f;
                // Seed event memory so we don't fire stale birth/death events.
                var sim = GameBootstrap.Instance != null ? GameBootstrap.Instance.Sim : null;
                if (sim != null)
                {
                    _lastKitCount = CountLiveKits(sim);
                    _lastAgentAlive = sim.State.Agent != null && sim.State.Agent.IsAlive;
                }
            }
            else
            {
                _follow.enabled = true;
                _follow.SnapToAgent();
            }
        }

        // -- interest scoring -----------------------------------------------------

        private void ScoreScene(Simulation sim, float now)
        {
            GameState state = sim.State;
            AgentState a = state.Agent;
            WorldData world = state.World;

            // Detect births / deaths.
            int kits = CountLiveKits(sim);
            if (kits > _lastKitCount) _birthEventUntil = now + 12f;
            _lastKitCount = kits;
            bool alive = a.IsAlive;
            if (_lastAgentAlive && !alive) _deathEventUntil = now + 18f;
            _lastAgentAlive = alive;

            // Candidate subjects.
            Framing best = new Framing();
            best.Interest = -1f;

            // 1. Death — the most important moment. Close-up, hold it.
            if (now < _deathEventUntil && !alive)
                Consider(ref best, AgentPos(a, world, 0.8f), ShotKind.CloseUp, 100f, "death");

            // 2. Birth — new kits. Close-up on the den/kits.
            if (now < _birthEventUntil)
            {
                Vector3 kp = KitCenter(sim, world);
                Consider(ref best, kp, ShotKind.CloseUp, 95f, "birth");
            }

            // 3. Predator danger — nearest predator to the agent.
            EntityState threat = NearestPredator(state, a, 30f);
            if (threat != null)
            {
                float d = Dist2D(a.X, a.Z, threat.X, threat.Z);
                float score = 92f - d; // closer = more urgent
                Vector3 mid = Midpoint(a, threat, world);
                Consider(ref best, mid, ShotKind.LowAngle, score, "danger");
            }

            // 4. Agent fleeing / fighting — high drama on the fox.
            if (a.IsAlive)
            {
                string goal = a.CurrentGoal;
                if (goal == "Flee")
                    Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.Medium, 90f, "flee");
                else if (goal == "Fight")
                    Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.LowAngle, 88f, "fight");
                else if (goal == "Hunt")
                    Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.Medium, 80f, "hunt");
            }

            // 5. Kits playing — joy. Frame the kits.
            if (AnyKitState(sim, "Play"))
                Consider(ref best, KitCenter(sim, world), ShotKind.Medium, 75f, "play");

            // 6. Social moment — greeting / bonding.
            if (a.IsAlive && (a.CurrentGoal == "Greet" || a.CurrentGoal == "SeekBond"))
                Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.Medium, 65f, "social");

            // 7. Golden-hour vista — when nothing urgent, show the world.
            if (IsGoldenHour(state.TimeOfDay))
                Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.Wide, 58f, "vista");

            // 8. Baseline: follow the fox.
            if (a.IsAlive)
                Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.Medium, 50f, "follow");

            // 9. Sleeping / resting — low interest, wide shot.
            if (a.IsAlive && (a.CurrentGoal == "Sleep" || a.CurrentGoal == "Rest"))
                Consider(ref best, AgentPos(a, world, 1.0f), ShotKind.Wide, 35f, "rest");

            // Hysteresis: switch only if the new winner beats the current
            // shot by a margin, or the shot has run long, or it's urgent.
            _shotAge += 0.4f; // scored every 0.4s
            bool urgent = best.Interest >= 88f;
            bool beaten = best.Interest > _current.Interest + 12f;
            bool expired = _shotAge > 14f && best.Interest >= _current.Interest;
            bool sameSubjectNewShot = best.Label == _current.Label && best.Shot != _current.Shot;

            if (best.Interest < 0f)
            {
                // No candidate (e.g. enabled mid-death): hold a wide on the
                // agent's last known position rather than framing the origin.
                best.Subject = AgentPos(a, world, 1.0f);
                best.Shot = ShotKind.Wide;
                best.Interest = 20f;
                best.Label = "fallback";
            }
            if (_shotAge < _minShotTime && !urgent) return;

            if (urgent || beaten || expired || sameSubjectNewShot || _shotAge > 999f)
            {
                bool newSubject = (_target.Label != best.Label) || _shotAge > 999f;
                if (newSubject && AccessibilitySettings.ReduceMotion)
                {
                    // Fade through black instead of a whip-pan cut.
                    BeginFadeCut(best);
                }
                else
                {
                    ApplyCut(best, newSubject);
                }
            }
        }

        private void ApplyCut(Framing best, bool newSubject)
        {
            _current = best;
            _target = best;
            _shotAge = 0f;
            _pushT = 0f;
            // Whip-pan to a new subject: faster damping, still smooth.
            _cutBoost = newSubject ? 2.2f : 0f;
            // Vary the minimum hold a little so cuts don't feel metronomic.
            _minShotTime = 6f + (best.Label.GetHashCode() & 7);
        }

        // -- accessibility: fade cuts ----------------------------------------------

        private void EnsureFade()
        {
            if (_fade != null) return;
            var canvas = UiKit.CreateCanvas("DirectorFade", 90);
            var rt = UiKit.Rect(canvas.transform, "Fade", 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            _fade = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            _fade.color = new Color(0f, 0f, 0f, 0f);
            _fade.raycastTarget = false;
        }

        private void BeginFadeCut(Framing best)
        {
            if (_hasPending) return; // already mid-fade; don't stack cuts
            EnsureFade();
            _pendingFraming = best;
            _hasPending = true;
            _fadePhase = 1; // fading out
        }

        private void UpdateFadeCut()
        {
            if (_fadePhase == 0 || _fade == null) return;
            var c = _fade.color;
            if (_fadePhase == 1)
            {
                // Fading out to black.
                c.a = Mathf.MoveTowards(c.a, 1f, 2.5f * Time.deltaTime);
                _fade.color = c;
                if (c.a >= 0.99f)
                {
                    // At black: apply the cut (no whip-pan boost), snap look.
                    ApplyCut(_pendingFraming, false);
                    _hasLook = false; // re-seed look target to avoid a glide from old subject
                    _fadePhase = 2; // fading back in
                }
            }
            else
            {
                // Fading back in.
                c.a = Mathf.MoveTowards(c.a, 0f, 1.5f * Time.deltaTime);
                _fade.color = c;
                if (c.a <= 0.01f)
                {
                    c.a = 0f; _fade.color = c;
                    _fadePhase = 0;
                    _hasPending = false;
                }
            }
        }

        private static void Consider(ref Framing best, Vector3 subject, ShotKind shot, float interest, string label)
        {
            if (interest > best.Interest)
            {
                best.Subject = subject;
                best.Shot = shot;
                best.Interest = interest;
                best.Label = label;
            }
        }

        // -- camera driving --------------------------------------------------------

        private void DriveCamera(Simulation sim, float now)
        {
            WorldData world = sim.State.World;
            Vector3 subject = _target.Subject;

            // Desired framing from the shot kind.
            float dist, pitchDeg;
            switch (_target.Shot)
            {
                case ShotKind.Wide:    dist = 24f; pitchDeg = 42f; break;
                case ShotKind.CloseUp: dist = 4.2f; pitchDeg = 10f; break;
                case ShotKind.LowAngle: dist = 6f; pitchDeg = -8f; break;
                default:               dist = 8f;  pitchDeg = 20f; break; // Medium
            }

            // Close-up slow push-in: the camera breathes closer over the shot.
            if (_target.Shot == ShotKind.CloseUp)
            {
                _pushT += Time.deltaTime;
                dist = Mathf.Max(2.8f, dist - _pushT * 0.12f);
            }

            // Wide vista slow drift: the frame wanders like a crane shot.
            float yaw = 180f;
            if (_target.Shot == ShotKind.Wide)
            {
                _orbitT += Time.deltaTime;
                yaw += Mathf.Sin(_orbitT * 0.08f) * 25f;
            }
            else
            {
                // Face the subject from behind its facing (documentary default).
                AgentState a = sim.State.Agent;
                if (a != null && _target.Label != "play" && _target.Label != "birth")
                    yaw = a.Facing * Mathf.Rad2Deg + 180f;
            }

            float p = pitchDeg * Mathf.Deg2Rad;
            float yw = yaw * Mathf.Deg2Rad;
            Vector3 offDir = new Vector3(Mathf.Sin(yw) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(yw) * Mathf.Cos(p));
            Vector3 desired = subject + offDir * dist;

            // Terrain clearance — never clip through the ground.
            float groundY = world.SampleHeight(desired.x, desired.z) + 0.6f;
            if (desired.y < groundY) desired.y = groundY;

            // Smooth glide. Cuts get a temporary boost (whip-pan, still smooth).
            float kPos = 1f - Mathf.Exp(-(2.6f + _cutBoost) * Time.deltaTime);
            _cutBoost = Mathf.Max(0f, _cutBoost - Time.deltaTime * 1.5f);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, desired, kPos);

            // Look target glides too.
            if (!_hasLook) { _smoothLook = subject; _hasLook = true; }
            float kLook = 1f - Mathf.Exp(-(4.5f + _cutBoost) * Time.deltaTime);
            _smoothLook = Vector3.Lerp(_smoothLook, subject, kLook);

            Vector3 lookDir = _smoothLook - _cam.transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion want = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                float kRot = 1f - Mathf.Exp(-6f * Time.deltaTime);
                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, kRot);
            }

            // Gentle FOV: wider for vistas, tighter for close-ups.
            float wantFov = _target.Shot == ShotKind.Wide ? 60f :
                            _target.Shot == ShotKind.CloseUp ? 48f : 55f;
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, wantFov,
                1f - Mathf.Exp(-2f * Time.deltaTime));
        }

        // -- helpers -----------------------------------------------------------------

        private static int CountLiveKits(Simulation sim)
        {
            int n = 0;
            var kits = sim.State.Kits;
            for (int i = 0; i < kits.Count; i++)
                if (kits[i].IsAlive) n++;
            return n;
        }

        private static bool AnyKitState(Simulation sim, string state)
        {
            var kits = sim.State.Kits;
            for (int i = 0; i < kits.Count; i++)
                if (kits[i].IsAlive && kits[i].State == state) return true;
            return false;
        }

        private static Vector3 KitCenter(Simulation sim, WorldData world)
        {
            var kits = sim.State.Kits;
            float sx = 0f, sz = 0f; int n = 0;
            for (int i = 0; i < kits.Count; i++)
            {
                if (!kits[i].IsAlive) continue;
                sx += kits[i].X; sz += kits[i].Z; n++;
            }
            if (n == 0) return AgentPos(sim.State.Agent, world, 0.6f);
            float x = sx / n, z = sz / n;
            return new Vector3(x, world.SampleHeight(x, z) + 0.6f, z);
        }

        private static EntityState NearestPredator(GameState state, AgentState a, float radius)
        {
            EntityState best = null;
            float bestD = radius;
            var ents = state.Entities;
            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e.Kind != EntityKind.Predator) continue;
                if (e.Behavior == "Dead") continue;
                float d = Dist2D(a.X, a.Z, e.X, e.Z);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        private static Vector3 Midpoint(AgentState a, EntityState e, WorldData world)
        {
            float x = (a.X + e.X) * 0.5f, z = (a.Z + e.Z) * 0.5f;
            return new Vector3(x, world.SampleHeight(x, z) + 1.0f, z);
        }

        private static Vector3 AgentPos(AgentState a, WorldData world, float lift)
        {
            return new Vector3(a.X, world.SampleHeight(a.X, a.Z) + lift, a.Z);
        }

        private static float Dist2D(float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1, dz = z2 - z1;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static bool IsGoldenHour(float timeOfDay)
        {
            // Dawn ~6-8h, dusk ~17-19h.
            return (timeOfDay >= 5.5f && timeOfDay <= 8.5f) ||
                   (timeOfDay >= 16.5f && timeOfDay <= 19.5f);
        }
    }
}
