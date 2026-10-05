// Solace.Unity — the protagonist's view.
//
// Builds the lantern-fox rig via the FoxRig contract (Solace.Unity.Character,
// implemented by a sibling agent — referenced here, never reimplemented).
// Per frame: position/facing from AgentState, SetGlow(agent.Glow) driving the
// chest-core light, and animation-clip mapping from speed/activity.
//
// Clip mapping: Speed>6 → Run, >2.5 → Trot, >0.2 → Walk; activity contains
// "Fight" → Pounce; "Rest" → Sit (Sleep at night); recently damaged → Hurt;
// kit-age PlayBow when playing; else Idle.
using UnityEngine;
using Solace.Core;
using Solace.Unity.Character;

namespace Solace.Unity
{
    public class AgentView : MonoBehaviour, ISimView
    {
        /// <summary>Rig root (camera framing / verification).</summary>
        public Transform Root { get; private set; }

        private FoxRig _rig;
        private FoxAnimator _anim;
        private Light _coreLight;
        private float _lastDamageAt = -999f;
        private float _prevHealth = 100f;
        private FoxAnimator.Clip _currentClip = FoxAnimator.Clip.Idle;

        public void Build()
        {
            GameBootstrap.Instance.RegisterView(this);
            _rig = FoxRig.Build(transform, 1f);
            if (_rig == null)
            {
                Debug.LogError("[Solace] FoxRig.Build returned null — character rig missing.");
                return;
            }
            Root = _rig.Root;

            _anim = _rig.Root.GetComponent<FoxAnimator>();
            if (_anim == null) _anim = _rig.Root.gameObject.AddComponent<FoxAnimator>();

            // The luminous core: a real point light parented to the chest.
            // (One of only two point lights in the scene; ≤4 pixel lights total.)
            if (_rig.ChestCore != null)
            {
                var lightGO = new GameObject("CoreLight");
                lightGO.transform.SetParent(_rig.ChestCore, false);
                lightGO.transform.localPosition = Vector3.zero;
                _coreLight = lightGO.AddComponent<Light>();
                _coreLight.type = LightType.Point;
                _coreLight.range = 14f;
                _coreLight.shadows = LightShadows.None;
            }
        }

        public void SyncFromState(GameState state)
        {
            if (_rig == null || Root == null) return;
            AgentState a = state.Agent;
            if (a == null) return;

            // Succession replaces the agent object; a dead one is transient.
            Root.gameObject.SetActive(a.IsAlive);
            if (!a.IsAlive) return;

            float y = state.World.SampleHeight(a.X, a.Z);
            Root.position = new Vector3(a.X, y, a.Z);
            Root.rotation = Quaternion.Euler(0f, a.Facing * Mathf.Rad2Deg, 0f);

            // Light is life: the readable signal.
            float glow = a.Glow;
            _rig.SetGlow(glow);
            if (_coreLight != null)
            {
                _coreLight.intensity = 0.5f + glow * 2.4f;
                _coreLight.color = Color.HSVToRGB(a.LightShade * 0.16f + 0.02f, 0.65f, 1f);
            }

            if (_prevHealth - a.Health > 1f) _lastDamageAt = Time.time;
            _prevHealth = a.Health;

            FoxAnimator.Clip want = PickClip(a, state.IsNight);
            if (want != _currentClip)
            {
                _currentClip = want;
                _anim.Play(want, 0.25f);
            }
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Sim != null) SyncFromState(boot.Sim.State);
        }

        private FoxAnimator.Clip PickClip(AgentState a, bool isNight)
        {
            string activity = a.CurrentActivity ?? "";
            if (Time.time - _lastDamageAt < 2f) return FoxAnimator.Clip.Hurt;
            if (a.InCombat || activity.Contains("Fight") || activity.Contains("fight"))
                return FoxAnimator.Clip.Pounce;
            if (activity.Contains("Rest") || activity.Contains("rest") ||
                activity.Contains("Sleep") || activity.Contains("sleep"))
                return isNight ? FoxAnimator.Clip.Sleep : FoxAnimator.Clip.Sit;
            if (a.Speed > 6f) return FoxAnimator.Clip.Run;
            if (a.Speed > 2.5f) return FoxAnimator.Clip.Trot;
            if (a.Speed > 0.2f) return FoxAnimator.Clip.Walk;
            if (a.Stage == LifeStage.Kit &&
                (activity.Contains("Play") || activity.Contains("play")))
                return FoxAnimator.Clip.PlayBow;
            return FoxAnimator.Clip.Idle;
        }
    }
}
