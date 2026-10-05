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

        protected void PlaceAt(float x, float z, float facing, WorldData world, float lift)
        {
            transform.position = new Vector3(x, world.SampleHeight(x, z) + lift, z);
            transform.rotation = Quaternion.Euler(0f, facing * Mathf.Rad2Deg, 0f);
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
                _rig = FoxRig.Build(transform, 0.95f);
                if (_rig != null)
                {
                    _anim = _rig.Root.GetComponent<FoxAnimator>();
                    if (_anim == null) _anim = _rig.Root.gameObject.AddComponent<FoxAnimator>();
                    _rig.SetGlow(0.35f); // dimmer cores than the protagonist
                }
            }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive || _rig == null) return;
            PlaceAt(e.X, e.Z, e.Facing, world, 0f);
            _rig.Root.position = transform.position;
            _rig.Root.rotation = transform.rotation;
            if (_anim != null)
            {
                FoxAnimator.Clip want = FoxAnimator.Clip.Idle;
                if (e.Behavior == "Flee") want = FoxAnimator.Clip.Run;
                else if (e.Behavior == "Wander") want = FoxAnimator.Clip.Walk;
                else if (e.Behavior == "Greeted") want = FoxAnimator.Clip.Sit;
                if (want != _anim.Current) _anim.Play(want, 0.3f);
            }
        }
    }

    // -- kits ----------------------------------------------------------------------

    public class KitView : EntityViewBase
    {
        private FoxRig _rig;
        private FoxAnimator _anim;
        private bool _built;

        public void SyncKit(KitState k, WorldData world)
        {
            if (!_built)
            {
                _built = true;
                _rig = FoxRig.Build(transform, 0.55f);
                if (_rig != null)
                {
                    _anim = _rig.Root.GetComponent<FoxAnimator>();
                    if (_anim == null) _anim = _rig.Root.gameObject.AddComponent<FoxAnimator>();
                }
            }
            gameObject.SetActive(k.IsAlive);
            if (!k.IsAlive || _rig == null) return;
            PlaceAt(k.X, k.Z, k.Facing, world, 0f);
            _rig.Root.position = transform.position;
            _rig.Root.rotation = transform.rotation;
            _rig.SetGlow(0.25f + 0.55f * (k.Energy / 100f));
            if (_anim != null)
            {
                FoxAnimator.Clip want = FoxAnimator.Clip.Idle;
                if (k.State == "Play") want = FoxAnimator.Clip.PlayBow;
                else if (k.State == "Sleep") want = FoxAnimator.Clip.Sleep;
                else if (k.State == "Hide") want = FoxAnimator.Clip.Sit;
                else if (k.State == "Follow") want = FoxAnimator.Clip.Walk;
                if (want != _anim.Current) _anim.Play(want, 0.3f);
            }
        }

        public override void SyncEntity(EntityState e, WorldData world) { }
    }

    // -- gloom-maw (predator) ---------------------------------------------------------

    public class PredatorView : EntityViewBase
    {
        private GameObject _body;
        private bool _built;

        public override void SyncEntity(EntityState e, WorldData world)
        {
            if (!_built) { _built = true; BuildBody(); }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive) return;
            PlaceAt(e.X, e.Z, e.Facing, world, 0f);
            // Menace reads in motion: lunge-bob while hunting, slow breath else.
            float t = Time.time;
            float bob = e.Behavior == "Hunt" ? Mathf.Abs(Mathf.Sin(t * 7f)) * 0.22f
                                            : Mathf.Sin(t * 1.8f) * 0.05f;
            var p = _body.transform.localPosition;
            p.y = bob;
            _body.transform.localPosition = p;
        }

        private void BuildBody()
        {
            _body = new GameObject("Body");
            _body.transform.SetParent(transform, false);
            var hide = MaterialFactory.Lit(new Color(0.055f, 0.05f, 0.07f), 0.85f);
            var eyeMat = MaterialFactory.LitEmissive(new Color(0.5f, 0.1f, 0.05f),
                                                      new Color(1f, 0.25f, 0.08f), 0.4f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            var cube = MeshFactory.GetPrimitive(PrimitiveType.Cube);
            var cyl = MeshFactory.GetPrimitive(PrimitiveType.Cylinder);

            Part(_body, "Torso", sphere, hide, new Vector3(0f, 1.0f, 0f),
                 new Vector3(1.5f, 1.05f, 2.2f), Quaternion.identity);
            Part(_body, "Head", sphere, hide, new Vector3(0f, 1.35f, 1.85f),
                 new Vector3(0.95f, 0.85f, 1.05f), Quaternion.identity);
            Part(_body, "Snout", cube, hide, new Vector3(0f, 1.15f, 2.6f),
                 new Vector3(0.55f, 0.42f, 0.75f), Quaternion.identity);
            Part(_body, "Jaw", cube, hide, new Vector3(0f, 0.88f, 2.55f),
                 new Vector3(0.45f, 0.18f, 0.7f), Quaternion.Euler(8f, 0f, 0f));
            // Ember eyes.
            Part(_body, "EyeL", sphere, eyeMat, new Vector3(-0.30f, 1.55f, 2.62f),
                 Vector3.one * 0.24f, Quaternion.identity);
            Part(_body, "EyeR", sphere, eyeMat, new Vector3(0.30f, 1.55f, 2.62f),
                 Vector3.one * 0.24f, Quaternion.identity);
            // Legs.
            Part(_body, "LegFL", cyl, hide, new Vector3(-0.62f, 0.45f, 0.85f),
                 new Vector3(0.42f, 1.1f, 0.42f), Quaternion.identity);
            Part(_body, "LegFR", cyl, hide, new Vector3(0.62f, 0.45f, 0.85f),
                 new Vector3(0.42f, 1.1f, 0.42f), Quaternion.identity);
            Part(_body, "LegBL", cyl, hide, new Vector3(-0.62f, 0.45f, -0.85f),
                 new Vector3(0.48f, 1.1f, 0.48f), Quaternion.identity);
            Part(_body, "LegBR", cyl, hide, new Vector3(0.62f, 0.45f, -0.85f),
                 new Vector3(0.48f, 1.1f, 0.48f), Quaternion.identity);
            // Back spikes.
            for (int i = 0; i < 4; i++)
                Part(_body, "Spike" + i, MeshFactory.Cone(0.16f, 0.7f, 5), hide,
                     new Vector3(0f, 1.85f - i * 0.06f, 0.7f - i * 0.55f),
                     Vector3.one, Quaternion.Euler(-18f, 0f, 0f));
            // Tail.
            Part(_body, "Tail", MeshFactory.Cone(0.28f, 1.6f, 6), hide,
                 new Vector3(0f, 1.0f, -2.2f), Vector3.one, Quaternion.Euler(-95f, 0f, 0f));
        }
    }

    // -- deer --------------------------------------------------------------------------

    public class DeerView : EntityViewBase
    {
        private GameObject _headPivot;
        private bool _built;

        public override void SyncEntity(EntityState e, WorldData world)
        {
            if (!_built) { _built = true; BuildBody(); }
            gameObject.SetActive(e.IsAlive);
            if (!e.IsAlive) return;
            PlaceAt(e.X, e.Z, e.Facing, world, 0f);
            // Graze: head dips.
            float target = e.Behavior == "Graze" ? 0.85f : 0f;
            var r = _headPivot.transform.localRotation;
            float cur = r.x;
            float next = Mathf.Lerp(cur, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            _headPivot.transform.localRotation = Quaternion.Euler(next * 57.3f, 0f, 0f);
        }

        private void BuildBody()
        {
            var coat = MaterialFactory.Lit(new Color(0.55f, 0.42f, 0.28f), 0.9f);
            var dark = MaterialFactory.Lit(new Color(0.35f, 0.26f, 0.17f), 0.9f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);
            var cyl = MeshFactory.GetPrimitive(PrimitiveType.Cylinder);

            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            Part(body, "Torso", sphere, coat, new Vector3(0f, 1.25f, 0f),
                 new Vector3(0.85f, 0.95f, 1.55f), Quaternion.identity);
            Part(body, "Rump", sphere, dark, new Vector3(0f, 1.28f, -0.9f),
                 new Vector3(0.7f, 0.8f, 0.7f), Quaternion.identity);
            Part(body, "Tail", sphere, dark, new Vector3(0f, 1.45f, -1.55f),
                 Vector3.one * 0.28f, Quaternion.identity);
            // Legs.
            Part(body, "LegFL", cyl, dark, new Vector3(-0.30f, 0.55f, 0.62f),
                 new Vector3(0.20f, 1.15f, 0.20f), Quaternion.identity);
            Part(body, "LegFR", cyl, dark, new Vector3(0.30f, 0.55f, 0.62f),
                 new Vector3(0.20f, 1.15f, 0.20f), Quaternion.identity);
            Part(body, "LegBL", cyl, dark, new Vector3(-0.30f, 0.55f, -0.62f),
                 new Vector3(0.22f, 1.15f, 0.22f), Quaternion.identity);
            Part(body, "LegBR", cyl, dark, new Vector3(0.30f, 0.55f, -0.62f),
                 new Vector3(0.22f, 1.15f, 0.22f), Quaternion.identity);
            // Head on a pivot for grazing.
            _headPivot = new GameObject("HeadPivot");
            _headPivot.transform.SetParent(body.transform, false);
            _headPivot.transform.localPosition = new Vector3(0f, 1.55f, 0.95f);
            Part(_headPivot, "Neck", cyl, coat, new Vector3(0f, 0.35f, 0.15f),
                 new Vector3(0.34f, 0.9f, 0.34f), Quaternion.Euler(28f, 0f, 0f));
            Part(_headPivot, "Head", sphere, coat, new Vector3(0f, 0.78f, 0.42f),
                 new Vector3(0.42f, 0.5f, 0.72f), Quaternion.identity);
            Part(_headPivot, "EarL", MeshFactory.Cone(0.10f, 0.34f, 5), dark,
                 new Vector3(-0.20f, 1.02f, 0.30f), Vector3.one, Quaternion.Euler(-14f, 0f, -18f));
            Part(_headPivot, "EarR", MeshFactory.Cone(0.10f, 0.34f, 5), dark,
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
            var fur = MaterialFactory.Lit(new Color(0.48f, 0.40f, 0.31f), 0.95f);
            var inner = MaterialFactory.Lit(new Color(0.70f, 0.60f, 0.48f), 0.95f);
            var sphere = MeshFactory.GetPrimitive(PrimitiveType.Sphere);

            Part(_body, "Torso", sphere, fur, new Vector3(0f, 0.32f, 0f),
                 new Vector3(0.52f, 0.48f, 0.68f), Quaternion.identity);
            Part(_body, "Head", sphere, fur, new Vector3(0f, 0.58f, 0.52f),
                 Vector3.one * 0.42f, Quaternion.identity);
            Part(_body, "EarL", MeshFactory.Cone(0.09f, 0.5f, 5), inner,
                 new Vector3(-0.13f, 0.86f, 0.46f), Vector3.one, Quaternion.Euler(-8f, 0f, -10f));
            Part(_body, "EarR", MeshFactory.Cone(0.09f, 0.5f, 5), inner,
                 new Vector3(0.13f, 0.86f, 0.46f), Vector3.one, Quaternion.Euler(-8f, 0f, 10f));
            Part(_body, "Tail", sphere, inner, new Vector3(0f, 0.38f, -0.62f),
                 Vector3.one * 0.20f, Quaternion.identity);
        }
    }
}
