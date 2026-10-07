// Solace.Unity — views for the other lives in the vale.
//
// EntityViewManager pools one view per entity id; KitViewManager does the
// same for kits. Kindred and kits reuse the FoxRig contract (dimmer cores);
// the gloom-maw, deer and hare are code-built primitive quadrupeds with real
// volume — no stickmen. All views mirror X/Z/Facing/Health from state.
using System.Collections.Generic;
using UnityEngine;
using Solace.Core;
using Solace.Unity.Character;

namespace Solace.Unity
{
    // -- managers ---------------------------------------------------------------

    public class EntityViewManager : MonoBehaviour, ISimView
    {
        private readonly Dictionary<int, EntityViewBase> _views = new Dictionary<int, EntityViewBase>();
        private readonly List<int> _sweep = new List<int>();
        private int _frame;

        private void Awake() { GameBootstrap.Instance.RegisterView(this); }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }

        public void SyncFromState(GameState state)
        {
            _frame++;
            for (int i = 0; i < state.Entities.Count; i++)
            {
                EntityState e = state.Entities[i];
                EntityViewBase v;
                if (!_views.TryGetValue(e.Id, out v))
                {
                    v = CreateView(e.Kind);
                    if (v == null) continue;
                    v.BoundId = e.Id;
                    _views[e.Id] = v;
                }
                v.LastSeenFrame = _frame;
                v.SyncEntity(e, state.World);
            }
            _sweep.Clear();
            foreach (var kv in _views)
                if (kv.Value.LastSeenFrame != _frame) _sweep.Add(kv.Key);
            for (int i = 0; i < _sweep.Count; i++)
            {
                Object.Destroy(_views[_sweep[i]].gameObject);
                _views.Remove(_sweep[i]);
            }
        }

        private EntityViewBase CreateView(EntityKind kind)
        {
            var go = new GameObject("Entity_" + kind);
            go.transform.SetParent(transform, false);
            switch (kind)
            {
                case EntityKind.Kindred: return go.AddComponent<KindredView>();
                case EntityKind.Predator: return go.AddComponent<PredatorView>();
                case EntityKind.Deer: return go.AddComponent<DeerView>();
                case EntityKind.Rabbit: return go.AddComponent<RabbitView>();
                default: return go.AddComponent<DeerView>();
            }
        }
    }

    public class KitViewManager : MonoBehaviour, ISimView
    {
        private readonly Dictionary<int, KitView> _views = new Dictionary<int, KitView>();
        private readonly List<int> _sweep = new List<int>();
        private int _frame;

        private void Awake() { GameBootstrap.Instance.RegisterView(this); }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }

        public void SyncFromState(GameState state)
        {
            _frame++;
            for (int i = 0; i < state.Kits.Count; i++)
            {
                KitState k = state.Kits[i];
                KitView v;
                if (!_views.TryGetValue(k.Id, out v))
                {
                    var go = new GameObject("Kit");
                    go.transform.SetParent(transform, false);
                    v = go.AddComponent<KitView>();
                    v.BoundId = k.Id;
                    _views[k.Id] = v;
                }
                v.LastSeenFrame = _frame;
                v.SyncKit(k, state.World);
            }
            _sweep.Clear();
            foreach (var kv in _views)
                if (kv.Value.LastSeenFrame != _frame) _sweep.Add(kv.Key);
            for (int i = 0; i < _sweep.Count; i++)
            {
                Object.Destroy(_views[_sweep[i]].gameObject);
                _views.Remove(_sweep[i]);
            }
        }
    }

    // -- base --------------------------------------------------------------------

    public abstract class EntityViewBase : MonoBehaviour
    {
        public int BoundId;
        public int LastSeenFrame;

        public abstract void SyncEntity(EntityState e, WorldData world);

        protected static GameObject Part(GameObject parent, string name, Mesh mesh, Material mat,
                                         Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = rot;
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// Bakes multiple static parts into ONE GameObject with a multi-submesh
        /// mesh (one submesh per material). The parts must be rigid relative to
        /// <paramref name="parent"/> — i.e. parts the animation never moves
        /// individually. Visual result is identical; draw calls drop from N to
        /// materials.Length.
        /// </summary>
        protected static GameObject MergedStatic(GameObject parent, string name,
                                                Material[] materials,
                                                List<Mesh> meshes, List<Vector3> positions,
                                                List<Vector3> scales, List<Quaternion> rotations,
                                                List<int> materialSlots)
        {
            var matrices = new List<Matrix4x4>(meshes.Count);
            for (int i = 0; i < meshes.Count; i++)
                matrices.Add(Matrix4x4.TRS(positions[i], rotations[i], scales[i]));
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = MeshFactory.MergeWithSubmeshes(meshes, matrices, materialSlots,
                                                          materials.Length);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        protected void PlaceAt(float x, float z, float facing, WorldData world, float lift)
        {
            transform.position = new Vector3(x, world.SampleHeight(x, z) + lift, z);
            transform.rotation = Quaternion.Euler(0f, facing * Mathf.Rad2Deg, 0f);
        }
    }

    // -- presentation motion -----------------------------------------------------------
    //
    // Wave-1 philosophy for every creature: the sim writes a target, the view
    // eases toward it. Critically-damped-ish position follow (accel/decel feel),
    // rate-limited yaw so turns carve instead of pivoting, teleport snap for
    // succession/spawns, and tracked visual speed + turn velocity so each view
    // can pick gaits, banking, and idle-life. Presentation only.

    public sealed class ViewMotion
    {
        public Vector3 SmoothPos;
        public float SmoothYaw;   // degrees
        public float VisualSpeed; // world units per real second
        public float TurnVel;     // smoothed signed yaw velocity, deg/s

        private Vector3 _prevPos;
        private bool _init;

        public void Snap(Vector3 pos, float yawDeg)
        {
            SmoothPos = pos; _prevPos = pos;
            SmoothYaw = yawDeg; TurnVel = 0f; VisualSpeed = 0f;
            _init = true;
        }

        /// <summary>
        /// Ease toward the sim target. followK: position eagerness; turnK: yaw
        /// eagerness; maxTurnDeg: degrees-per-second cap on turning.
        /// </summary>
        public void Update(Vector3 targetPos, float targetYawDeg, float dt,
                           float followK, float turnK, float maxTurnDeg)
        {
            dt = Mathf.Max(dt, 1e-4f);
            if (!_init || (targetPos - SmoothPos).sqrMagnitude > 16f)
            {
                Snap(targetPos, targetYawDeg);
                return;
            }
            SmoothPos = Vector3.Lerp(SmoothPos, targetPos, 1f - Mathf.Exp(-followK * dt));

            float want = Mathf.DeltaAngle(SmoothYaw, targetYawDeg);
            float applied = Mathf.Clamp(want, -maxTurnDeg * dt, maxTurnDeg * dt);
            float newYaw = SmoothYaw + applied * (1f - Mathf.Exp(-turnK * dt));
            float rawVel = Mathf.DeltaAngle(SmoothYaw, newYaw) / dt;
            TurnVel = Mathf.Lerp(TurnVel, rawVel, 1f - Mathf.Exp(-6f * dt));
            SmoothYaw = newYaw;

            float inst = (SmoothPos - _prevPos).magnitude / dt;
            _prevPos = SmoothPos;
            VisualSpeed = Mathf.Lerp(VisualSpeed, inst, 1f - Mathf.Exp(-5f * dt));
        }

        /// <summary>Bank roll in degrees: lean into the turn, scaled by speed.</summary>
        public float BankRoll(float gain, float maxDeg)
        {
            float speedK = Mathf.Clamp01(VisualSpeed / 6f);
            return Mathf.Clamp(-TurnVel * (0.35f + speedK) * gain, -maxDeg, maxDeg);
        }

        /// <summary>Swing a leg pivot fore-aft around its hip/shoulder.</summary>
        public static void SwingLeg(GameObject leg, float phase, float ampDeg)
        {
            if (leg == null) return;
            leg.transform.localRotation = Quaternion.Euler(Mathf.Sin(phase) * ampDeg, 0f, 0f);
        }

        public static float Frac(float x) { return x - Mathf.Floor(x); }

        public static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }

    // -- kindred (dimmer lantern-foxes) --------------------------------------------

    public class KindredView : EntityViewBase
    {
        private FoxRig _rig;
        private FoxAnimator _anim;
        private bool _built;

        public override void SyncEntity(EntityState e, WorldData world)
        {
            if (!_built)
            {
                _built = true;
                // Per-kindred personality: deterministic size/glow variation so
                // they read as individuals, not clones of the protagonist.
                float h1 = ViewMotion.Frac(BoundId * 0.6180339f);
                float h2 = ViewMotion.Frac(BoundId * 0.3819660f + 0.5f);
                _rig = FoxRig.Build(transform, 0.92f + 0.07f * h1,
                                        HatchVariant.Kindred, (h2 - 0.5f) * 1.2f);
                if (_rig != null)
                {
                    _anim = _rig.Root.GetComponent<FoxAnimator>();
                    if (_anim == null) _anim = _rig.Root.gameObject.AddComponent<FoxAnimator>();
                    _rig.SetGlow(0.30f + 0.12f * h2); // dimmer cores than the protagonist
                }
            }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive || _rig == null) return;
            PlaceAt(e.X, e.Z, e.Facing, world, 0f);
            _rig.Root.position = transform.position;
            _rig.Root.rotation = transform.rotation;
            // FoxRig.LateUpdate supplies the Wave-1 treatment: smoothed follow,
            // carving turns, banking, gait-rate sync, breathing core glow.
            if (_anim != null)
            {
                FoxAnimator.Clip want = PickClip(e);
                if (want != _anim.Current) _anim.Play(want, 0.3f);
            }
        }

        private FoxAnimator.Clip PickClip(EntityState e)
        {
            // Sickness reads as stillness: a dim fox sitting apart.
            if (e.Sickness != SicknessKind.None) return FoxAnimator.Clip.Sit;
            if (e.Behavior == "Greeted") return FoxAnimator.Clip.Sit;
            if (e.Behavior == "Flee") return FoxAnimator.Clip.Run;
            // Gait from real visual speed, same thresholds as the protagonist.
            float s = _rig != null ? _rig.VisualSpeed : 0f;
            if (s > 6f) return FoxAnimator.Clip.Run;
            if (s > 2.5f) return FoxAnimator.Clip.Trot;
            if (s > 0.2f) return FoxAnimator.Clip.Walk;
            return FoxAnimator.Clip.Idle;
        }
    }

    // -- kits ----------------------------------------------------------------------

    public class KitView : EntityViewBase
    {
        private FoxRig _rig;
        private FoxAnimator _anim;
        private bool _built;

        // Playful layer (parent transform; the rig smooths underneath).
        private float _playT = 2f;      // countdown to the next pounce
        private float _pounceAt = -99f; // when the current pounce started
        private float _spinT = 6f;      // countdown to the next tail-chase spin
        private float _spinAge = 99f;   // progress through the spin
        private float _bouncePhase;     // eager-follow hop phase
        private System.Random _rng;

        public void SyncKit(KitState k, WorldData world)
        {
            if (!_built)
            {
                _built = true;
                float h = ViewMotion.Frac(BoundId * 0.7548777f + 0.25f);
                _rig = FoxRig.Build(transform, 0.52f + 0.07f * h, HatchVariant.Kit); // littermates vary
                _rng = new System.Random(BoundId * 7919 + 13);
                if (_rig != null)
                {
                    _anim = _rig.Root.GetComponent<FoxAnimator>();
                    if (_anim == null) _anim = _rig.Root.gameObject.AddComponent<FoxAnimator>();
                }
                _playT = 1.5f + (float)_rng.NextDouble() * 2f;
                _spinT = 5f + (float)_rng.NextDouble() * 6f;
            }
            gameObject.SetActive(k.IsAlive);
            if (!k.IsAlive || _rig == null) return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            PlaceAt(k.X, k.Z, k.Facing, world, 0f);

            bool playing = k.State == "Play";
            bool following = k.State == "Follow";
            float yawDeg = k.Facing * Mathf.Rad2Deg;
            float lift = 0f, roll = 0f;

            // Eager-follow bounce: little hops while trotting after the parent.
            if (following && _rig.VisualSpeed > 0.4f)
            {
                _bouncePhase += dt * (4f + _rig.VisualSpeed * 2.2f);
                lift += Mathf.Abs(Mathf.Sin(_bouncePhase)) * 0.09f *
                        Mathf.Clamp01(_rig.VisualSpeed / 3f);
            }

            // Tail-chase spin: a full 360° of pure joy, every so often at play.
            _spinAge += dt;
            if (playing)
            {
                _spinT -= dt;
                if (_spinT <= 0f && _spinAge > 1f)
                {
                    _spinT = 6f + (float)_rng.NextDouble() * 9f;
                    _spinAge = 0f;
                }
            }
            const float spinDur = 0.75f;
            if (_spinAge < spinDur)
                yawDeg += 360f * ViewMotion.Smooth01(_spinAge / spinDur);

            // Tumble: as the pounce lands, a quick sideways roll reads as a tumble.
            bool pouncing = _anim != null && _anim.Current == FoxAnimator.Clip.Pounce;
            float pounceAge = Time.time - _pounceAt;
            if (pouncing && pounceAge > 0.42f && pounceAge < 0.72f)
                roll = 55f * Mathf.Sin((pounceAge - 0.42f) / 0.30f * Mathf.PI);

            transform.position += new Vector3(0f, lift, 0f);
            transform.rotation = Quaternion.Euler(0f, yawDeg, roll);

            _rig.Root.position = transform.position;
            _rig.Root.rotation = transform.rotation;
            _rig.SetGlow(0.25f + 0.55f * (k.Energy / 100f));

            if (_anim != null)
            {
                // Let a triggered pounce play out before state mapping resumes.
                bool pounceActive = pouncing && pounceAge < 1.15f;
                if (!pounceActive)
                {
                    FoxAnimator.Clip want = FoxAnimator.Clip.Idle;
                    if (k.State == "Play") want = FoxAnimator.Clip.PlayBow;
                    else if (k.State == "Sleep") want = FoxAnimator.Clip.Sleep;
                    else if (k.State == "Hide") want = FoxAnimator.Clip.Sit;
                    else if (k.State == "Eat") want = FoxAnimator.Clip.Sit;
                    else if (k.State == "Follow") want = FoxAnimator.Clip.Walk;
                    if (want != _anim.Current) _anim.Play(want, 0.3f);

                    // Playful pounce: spring at imaginary butterflies.
                    if (playing && (want == FoxAnimator.Clip.PlayBow || want == FoxAnimator.Clip.Idle))
                    {
                        _playT -= dt;
                        if (_playT <= 0f)
                        {
                            _playT = 2.5f + (float)_rng.NextDouble() * 4f;
                            _pounceAt = Time.time;
                            _anim.Play(FoxAnimator.Clip.Pounce, 0.12f);
                        }
                    }
                }
            }
        }

        public override void SyncEntity(EntityState e, WorldData world) { }
    }

    // -- gloom-maw (predator) ---------------------------------------------------------

    public class PredatorView : EntityViewBase
    {
        private GameObject _body;
        private GameObject _head;
        private GameObject _jaw;
        private GameObject _tail;
        private GameObject _legFL, _legFR, _legBL, _legBR;
        private Material _eyeMat; // owned instance: we pulse the ember glow
        private bool _built;

        private readonly ViewMotion _motion = new ViewMotion();
        private float _hunt;      // 0..1 smoothed hunting blend
        private float _stepPhase;

        public override void SyncEntity(EntityState e, WorldData world)
        {
            if (!_built) { _built = true; BuildBody(); }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive) return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float y = world.SampleHeight(e.X, e.Z);
            // Deliberate and weighty: heavy smoothing, slow carving turns.
            // A gloom-maw never hurries — that's what makes it scary.
            _motion.Update(new Vector3(e.X, y, e.Z), e.Facing * Mathf.Rad2Deg,
                           dt, followK: 5.5f, turnK: 3.5f, maxTurnDeg: 150f);
            transform.position = _motion.SmoothPos;
            // No banking: the maw stays level. Menace is in the stillness.
            transform.rotation = Quaternion.Euler(0f, _motion.SmoothYaw, 0f);

            bool hunting = e.Behavior == "Hunt";
            _hunt = Mathf.Lerp(_hunt, hunting ? 1f : 0f, 1f - Mathf.Exp(-3f * dt));

            float t = Time.time;
            float speedK = Mathf.Clamp01(_motion.VisualSpeed / 5f);

            // Stalk: body drops low, shoulders roll forward. Idle: slow breath.
            float crouch = _hunt * 0.30f;
            float bob = hunting
                ? Mathf.Abs(Mathf.Sin(t * 5.2f)) * 0.06f * (0.3f + speedK)
                : Mathf.Sin(t * 1.6f) * 0.045f;
            _body.transform.localPosition = new Vector3(0f, bob - crouch, 0f);
            _body.transform.localRotation = Quaternion.Euler(_hunt * 7f, 0f, 0f);

            // Footfalls: slow, heavy, diagonal pairs — each step lands with intent.
            _stepPhase += dt * (1.2f + _motion.VisualSpeed * 1.6f);
            float amp = 14f * Mathf.Clamp01(_motion.VisualSpeed / 2f) + 2f * _hunt;
            ViewMotion.SwingLeg(_legFL, _stepPhase, amp);
            ViewMotion.SwingLeg(_legBR, _stepPhase, amp);
            ViewMotion.SwingLeg(_legFR, _stepPhase + Mathf.PI, amp);
            ViewMotion.SwingLeg(_legBL, _stepPhase + Mathf.PI, amp);

            // Head: scans side to side on the hunt, steadies otherwise.
            float scan = hunting ? Mathf.Sin(t * 0.9f) * 14f : Mathf.Sin(t * 0.35f) * 6f;
            _head.transform.localRotation = Quaternion.Euler(_hunt * 10f, scan, 0f);

            // Jaw hangs open on the hunt.
            _jaw.transform.localRotation = Quaternion.Euler(8f + _hunt * 16f, 0f, 0f);

            // Tail: low with a slow lash while hunting; near-still otherwise.
            _tail.transform.localRotation = Quaternion.Euler(-95f + _hunt * 18f, 0f,
                hunting ? Mathf.Sin(t * 2.2f) * 10f : Mathf.Sin(t * 0.8f) * 3f);

            // Ember eyes: pulse brighter as the hunt sharpens.
            float pulse = 1f + 0.25f * Mathf.Sin(t * (2f + 4f * _hunt));
            float glow = (0.9f + 1.6f * _hunt) * pulse;
            MaterialFactory.SetEmission(_eyeMat, new Color(1f, 0.25f, 0.08f) * glow);
        }

        private void BuildBody()
        {
            _body = new GameObject("Body");
            _body.transform.SetParent(transform, false);
            var hide = MaterialFactory.Lit(new Color(0.055f, 0.05f, 0.07f), 0.3f);
            // Owned eye material so each maw can pulse its own ember glow.
            _eyeMat = MaterialFactory.NewLitEmissiveInstance(new Color(0.5f, 0.1f, 0.05f),
                                                             new Color(1f, 0.25f, 0.08f), 0.4f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            var cube = MeshFactory.GetPrimitive(PrimitiveType.Cube);
            var cyl = MeshFactory.GetPrimitive(PrimitiveType.Cylinder);

            // --- merged static shell: torso, snout, eyes, back spikes ---
            // All rigid relative to _body (which bobs/crouches as a unit).
            // Head stays separate: it scans side-to-side (animated).
            // Slot 0 = hide, slot 1 = emissive eyes. 8 parts -> 1 draw call x2.
            {
                var meshes = new List<Mesh>();
                var poss = new List<Vector3>();
                var scls = new List<Vector3>();
                var rots = new List<Quaternion>();
                var slots = new List<int>();
                // Torso
                meshes.Add(sphere); poss.Add(new Vector3(0f, 1.0f, 0f));
                scls.Add(new Vector3(1.5f, 1.05f, 2.2f)); rots.Add(Quaternion.identity); slots.Add(0);
                // Snout
                meshes.Add(cube); poss.Add(new Vector3(0f, 1.15f, 2.6f));
                scls.Add(new Vector3(0.55f, 0.42f, 0.75f)); rots.Add(Quaternion.identity); slots.Add(0);
                // Ember eyes
                meshes.Add(sphere); poss.Add(new Vector3(-0.30f, 1.55f, 2.62f));
                scls.Add(Vector3.one * 0.24f); rots.Add(Quaternion.identity); slots.Add(1);
                meshes.Add(sphere); poss.Add(new Vector3(0.30f, 1.55f, 2.62f));
                scls.Add(Vector3.one * 0.24f); rots.Add(Quaternion.identity); slots.Add(1);
                // Back spikes
                var spikeRot = Quaternion.Euler(-18f, 0f, 0f);
                for (int i = 0; i < 4; i++)
                {
                    meshes.Add(MeshFactory.Cone(0.16f, 0.7f, 5));
                    poss.Add(new Vector3(0f, 1.85f - i * 0.06f, 0.7f - i * 0.55f));
                    scls.Add(Vector3.one); rots.Add(spikeRot); slots.Add(0);
                }
                MergedStatic(_body, "Shell", new[] { hide, _eyeMat },
                             meshes, poss, scls, rots, slots);
            }

            // Head stays a separate node: it scans while hunting (animated).
            _head = Part(_body, "Head", sphere, hide, new Vector3(0f, 1.35f, 1.85f),
                 new Vector3(0.95f, 0.85f, 1.05f), Quaternion.identity);
            _jaw = Part(_body, "Jaw", cube, hide, new Vector3(0f, 0.88f, 2.55f),
                 new Vector3(0.45f, 0.18f, 0.7f), Quaternion.Euler(8f, 0f, 0f));
            // Legs (kept for the stalk gait).
            _legFL = Part(_body, "LegFL", cyl, hide, new Vector3(-0.62f, 0.45f, 0.85f),
                 new Vector3(0.42f, 1.1f, 0.42f), Quaternion.identity);
            _legFR = Part(_body, "LegFR", cyl, hide, new Vector3(0.62f, 0.45f, 0.85f),
                 new Vector3(0.42f, 1.1f, 0.42f), Quaternion.identity);
            _legBL = Part(_body, "LegBL", cyl, hide, new Vector3(-0.62f, 0.45f, -0.85f),
                 new Vector3(0.48f, 1.1f, 0.48f), Quaternion.identity);
            _legBR = Part(_body, "LegBR", cyl, hide, new Vector3(0.62f, 0.45f, -0.85f),
                 new Vector3(0.48f, 1.1f, 0.48f), Quaternion.identity);
            // Tail.
            _tail = Part(_body, "Tail", MeshFactory.Cone(0.28f, 1.6f, 6), hide,
                 new Vector3(0f, 1.0f, -2.2f), Vector3.one, Quaternion.Euler(-95f, 0f, 0f));
        }
    }

    // -- deer --------------------------------------------------------------------------

    public class DeerView : EntityViewBase
    {
        private GameObject _body;
        private GameObject _headPivot;
        private GameObject _legFL, _legFR, _legBL, _legBR;
        private GameObject _earL, _earR;
        private GameObject _tail;
        private bool _built;

        private readonly ViewMotion _motion = new ViewMotion();
        private float _stepPhase;
        private float _bank;
        // Idle life: ear twitches, tail flicks, curious look-arounds.
        private float _twitchT = 2f, _twitchAge = 99f;
        private int _twitchEar;
        private float _tailT = 3f, _tailAge = 99f;
        private float _lookT = 4f, _lookYaw;

        public override void SyncEntity(EntityState e, WorldData world)
        {
            if (!_built) { _built = true; BuildBody(); }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive) return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float y = world.SampleHeight(e.X, e.Z);
            _motion.Update(new Vector3(e.X, y, e.Z), e.Facing * Mathf.Rad2Deg,
                           dt, followK: 9f, turnK: 6f, maxTurnDeg: 260f);
            transform.position = _motion.SmoothPos;
            // Gentle banking into turns, like the fox.
            _bank = Mathf.Lerp(_bank, _motion.BankRoll(0.05f, 10f), 1f - Mathf.Exp(-8f * dt));
            transform.rotation = Quaternion.Euler(0f, _motion.SmoothYaw, _bank);

            float t = Time.time;
            bool fleeing = e.Behavior == "Flee";
            bool grazing = e.Behavior == "Graze";
            float speedK = Mathf.Clamp01(_motion.VisualSpeed / 6f);

            // --- legs: stotting bound in flight, diagonal walk otherwise ---
            _stepPhase += dt * (2.5f + _motion.VisualSpeed * 2.0f);
            if (fleeing)
            {
                // Stot: all four legs drive together, body rocks, real airtime.
                float p = Mathf.Sin(_stepPhase * 1.4f);
                float lift = Mathf.Max(0f, p);
                _body.transform.localPosition = new Vector3(0f, lift * 0.35f * (0.4f + speedK), 0f);
                _body.transform.localRotation = Quaternion.Euler(p * 9f, 0f, 0f);
                float amp = 34f * (0.4f + 0.6f * speedK);
                ViewMotion.SwingLeg(_legFL, _stepPhase * 1.4f, amp);
                ViewMotion.SwingLeg(_legFR, _stepPhase * 1.4f, amp);
                ViewMotion.SwingLeg(_legBL, _stepPhase * 1.4f, amp);
                ViewMotion.SwingLeg(_legBR, _stepPhase * 1.4f, amp);
            }
            else
            {
                _body.transform.localPosition =
                    new Vector3(0f, Mathf.Abs(Mathf.Sin(_stepPhase)) * 0.03f * speedK, 0f);
                _body.transform.localRotation =
                    Quaternion.Euler(0f, 0f, Mathf.Sin(_stepPhase) * 1.6f * speedK);
                float amp = 26f * speedK;
                ViewMotion.SwingLeg(_legFL, _stepPhase, amp);
                ViewMotion.SwingLeg(_legBR, _stepPhase, amp);
                ViewMotion.SwingLeg(_legFR, _stepPhase + Mathf.PI, amp);
                ViewMotion.SwingLeg(_legBL, _stepPhase + Mathf.PI, amp);
            }

            // --- head: graze dip + nibble, else curious look-arounds ---
            float targetDip = grazing ? 48f : 0f; // degrees of downward pitch
            float nibble = grazing ? Mathf.Sin(t * 9f) * 3f : 0f;
            _lookT -= dt;
            if (_lookT <= 0f && !grazing && !fleeing)
            {
                _lookT = 3f + UnityEngine.Random.value * 4f;
                _lookYaw = (UnityEngine.Random.value - 0.5f) * 70f;
            }
            float lookYaw = (grazing || fleeing) ? 0f : _lookYaw;
            float curDip = _headPivot.transform.localEulerAngles.x;
            if (curDip > 180f) curDip -= 360f;
            float dip = Mathf.Lerp(curDip, targetDip + nibble, 1f - Mathf.Exp(-4f * dt));
            _headPivot.transform.localRotation = Quaternion.Euler(dip, lookYaw, 0f);

            // --- ears: idle twitches, pinned back in flight ---
            _twitchT -= dt; _twitchAge += dt;
            if (_twitchT <= 0f && !fleeing)
            {
                _twitchT = 2f + UnityEngine.Random.value * 4f;
                _twitchAge = 0f;
                _twitchEar = UnityEngine.Random.value < 0.5f ? 0 : 1;
            }
            float flick = 0f;
            if (_twitchAge < 0.5f)
                flick = Mathf.Exp(-6f * _twitchAge) * 22f * Mathf.Sin(_twitchAge * 30f);
            float pin = fleeing ? -26f : 0f;
            _earL.transform.localRotation =
                Quaternion.Euler(-14f + pin + (_twitchEar == 0 ? flick : 0f), 0f, -18f);
            _earR.transform.localRotation =
                Quaternion.Euler(-14f + pin + (_twitchEar == 1 ? flick : 0f), 0f, 18f);

            // --- tail: white flag up in flight, idle flicks otherwise ---
            // (The scut is round, so the flick reads through lift + pulse, not spin.)
            _tailT -= dt; _tailAge += dt;
            if (_tailT <= 0f)
            {
                _tailT = 2.5f + UnityEngine.Random.value * 5f;
                _tailAge = 0f;
            }
            float pulse = 1f;
            if (_tailAge < 0.6f)
                pulse = 1f + 0.35f * Mathf.Exp(-5f * _tailAge) * Mathf.Abs(Mathf.Sin(_tailAge * 26f));
            _tail.transform.localPosition =
                new Vector3(0f, 1.45f + (fleeing ? 0.30f : 0f), -1.55f);
            _tail.transform.localScale = Vector3.one * (0.28f * pulse);
        }

        private void BuildBody()
        {
            // Per-deer Hatch tint: warm cream ↔ soft gray, deterministic per deer.
            float th = ViewMotion.Frac(BoundId * 0.372081f + 0.11f) * 2f - 1f;
            Color coatC = th > 0f
                ? Color.Lerp(new Color(0.93f, 0.87f, 0.75f), new Color(0.95f, 0.82f, 0.63f), th)
                : Color.Lerp(new Color(0.93f, 0.87f, 0.75f), new Color(0.80f, 0.79f, 0.77f), -th);
            var coat = MaterialFactory.Lit(coatC, 0.35f);
            var dark = MaterialFactory.Lit(coatC * 0.82f, 0.35f);
            var plate = MaterialFactory.Lit(new Color(0.98f, 0.945f, 0.855f), 0.35f);
            var eyeGloss = MaterialFactory.Lit(new Color(0.035f, 0.030f, 0.040f), 0.9f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            var cyl = MeshFactory.GetPrimitive(PrimitiveType.Cylinder);

            var body = new GameObject("Body");
            _body = body;
            body.transform.SetParent(transform, false);
            // --- merged body shell: two round plush blobs ---
            // Slot 0 = coat, slot 1 = dark. 2 parts -> 1 draw call x2.
            {
                var meshes = new List<Mesh> { sphere, sphere };
                var poss = new List<Vector3>
                {
                    new Vector3(0f, 1.25f, 0f),
                    new Vector3(0f, 1.28f, -0.9f),
                };
                var scls = new List<Vector3>
                {
                    new Vector3(0.95f, 1.0f, 1.6f),
                    new Vector3(0.78f, 0.85f, 0.75f),
                };
                var rots = new List<Quaternion> { Quaternion.identity, Quaternion.identity };
                var slots = new List<int> { 0, 1 };
                MergedStatic(body, "BodyShell", new[] { coat, dark },
                             meshes, poss, scls, rots, slots);
            }
            _tail = Part(body, "Tail", sphere, plate, new Vector3(0f, 1.45f, -1.55f),
                 Vector3.one * 0.28f, Quaternion.identity);
            // Legs (kept for walk / stot gaits).
            _legFL = Part(body, "LegFL", cyl, dark, new Vector3(-0.30f, 0.55f, 0.62f),
                 new Vector3(0.22f, 1.15f, 0.22f), Quaternion.identity);
            _legFR = Part(body, "LegFR", cyl, dark, new Vector3(0.30f, 0.55f, 0.62f),
                 new Vector3(0.22f, 1.15f, 0.22f), Quaternion.identity);
            _legBL = Part(body, "LegBL", cyl, dark, new Vector3(-0.30f, 0.55f, -0.62f),
                 new Vector3(0.24f, 1.15f, 0.24f), Quaternion.identity);
            _legBR = Part(body, "LegBR", cyl, dark, new Vector3(0.30f, 0.55f, -0.62f),
                 new Vector3(0.24f, 1.15f, 0.24f), Quaternion.identity);
            // Head on a pivot for grazing.
            _headPivot = new GameObject("HeadPivot");
            _headPivot.transform.SetParent(body.transform, false);
            _headPivot.transform.localPosition = new Vector3(0f, 1.55f, 0.95f);
            // --- merged head: neck + round skull (rigid relative to the pivot) ---
            // Both coat. 2 parts -> 1 draw call.
            {
                var meshes = new List<Mesh> { cyl, sphere };
                var poss = new List<Vector3>
                {
                    new Vector3(0f, 0.35f, 0.15f),
                    new Vector3(0f, 0.78f, 0.42f),
                };
                var scls = new List<Vector3>
                {
                    new Vector3(0.36f, 0.9f, 0.36f),
                    new Vector3(0.48f, 0.52f, 0.55f),
                };
                var rots = new List<Quaternion>
                {
                    Quaternion.Euler(28f, 0f, 0f),
                    Quaternion.identity,
                };
                var slots = new List<int> { 0, 0 };
                MergedStatic(_headPivot, "HeadMerged", new[] { coat },
                             meshes, poss, scls, rots, slots);
            }
            // Hatch face on the head pivot (follows graze dips + look-arounds).
            Part(_headPivot, "FacePlate", sphere, plate, new Vector3(0f, 0.72f, 0.78f),
                 new Vector3(0.34f, 0.28f, 0.18f), Quaternion.identity);
            Part(_headPivot, "EyeL", sphere, eyeGloss, new Vector3(-0.17f, 0.86f, 0.82f),
                 Vector3.one * 0.11f, Quaternion.identity);
            Part(_headPivot, "EyeR", sphere, eyeGloss, new Vector3(0.17f, 0.86f, 0.82f),
                 Vector3.one * 0.11f, Quaternion.identity);
            _earL = Part(_headPivot, "EarL", MeshFactory.Cone(0.10f, 0.34f, 5), coat,
                 new Vector3(-0.20f, 1.02f, 0.30f), Vector3.one, Quaternion.Euler(-14f, 0f, -18f));
            _earR = Part(_headPivot, "EarR", MeshFactory.Cone(0.10f, 0.34f, 5), coat,
                 new Vector3(0.20f, 1.02f, 0.30f), Vector3.one, Quaternion.Euler(-14f, 0f, 18f));
        }
    }

    // -- hare ----------------------------------------------------------------------------

    public class RabbitView : EntityViewBase
    {
        private GameObject _body;
        private bool _built;

        public override void SyncEntity(EntityState e, WorldData world)
        {
            if (!_built) { _built = true; BuildBody(); }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive) return;
            PlaceAt(e.X, e.Z, e.Facing, world, 0f);
            float hop = e.Behavior == "Hop" ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.22f : 0f;
            var p = _body.transform.localPosition;
            p.y = hop;
            _body.transform.localPosition = p;
        }

        private void BuildBody()
        {
            _body = new GameObject("Body");
            _body.transform.SetParent(transform, false);
            // Hatch-bunny: round cream plush, long ears, glossy Hatch eyes.
            var fur = MaterialFactory.Lit(new Color(0.94f, 0.88f, 0.76f), 0.35f);
            var inner = MaterialFactory.Lit(new Color(0.97f, 0.70f, 0.68f), 0.35f);
            var plate = MaterialFactory.Lit(new Color(0.98f, 0.945f, 0.855f), 0.35f);
            var eyeGloss = MaterialFactory.Lit(new Color(0.035f, 0.030f, 0.040f), 0.9f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);

            // --- merged whole rabbit: every part is rigid relative to _body ---
            // (Hop animates _body itself.) Slot 0 = fur, slot 1 = inner.
            // 5 parts -> 1 draw call x2.
            {
                var earRotL = Quaternion.Euler(-8f, 0f, -10f);
                var earRotR = Quaternion.Euler(-8f, 0f, 10f);
                var meshes = new List<Mesh>
                {
                    sphere, sphere,
                    MeshFactory.Cone(0.09f, 0.5f, 5), MeshFactory.Cone(0.09f, 0.5f, 5),
                    sphere,
                };
                var poss = new List<Vector3>
                {
                    new Vector3(0f, 0.32f, 0f),
                    new Vector3(0f, 0.58f, 0.52f),
                    new Vector3(-0.13f, 0.86f, 0.46f),
                    new Vector3(0.13f, 0.86f, 0.46f),
                    new Vector3(0f, 0.38f, -0.62f),
                };
                var scls = new List<Vector3>
                {
                    new Vector3(0.56f, 0.52f, 0.72f),
                    Vector3.one * 0.46f,
                    Vector3.one,
                    Vector3.one,
                    Vector3.one * 0.22f,
                };
                var rots = new List<Quaternion>
                {
                    Quaternion.identity, Quaternion.identity,
                    earRotL, earRotR,
                    Quaternion.identity,
                };
                var slots = new List<int> { 0, 0, 1, 1, 1 };
                MergedStatic(_body, "RabbitMerged", new[] { fur, inner },
                             meshes, poss, scls, rots, slots);
            }
            // Hatch face on the head (front of the head sphere at z≈0.52).
            var faceParent = _body;
            Part(faceParent, "FacePlate", sphere, plate, new Vector3(0f, 0.55f, 0.88f),
                 new Vector3(0.30f, 0.24f, 0.15f), Quaternion.identity);
            Part(faceParent, "EyeL", sphere, eyeGloss, new Vector3(-0.13f, 0.64f, 0.90f),
                 Vector3.one * 0.09f, Quaternion.identity);
            Part(faceParent, "EyeR", sphere, eyeGloss, new Vector3(0.13f, 0.64f, 0.90f),
                 Vector3.one * 0.09f, Quaternion.identity);
        }
    }
}
