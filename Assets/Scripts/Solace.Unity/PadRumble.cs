// Solace.Unity — gamepad rumble.
//
// Three haptic voices, presentation only (never touches the sim):
//   - Thunder: a sharp decaying burst on each lightning strike.
//   - Predator: a low uneasy tremor while a gloom-maw is near.
//   - Heartbeat: a lub-dub pulse when the fox's health runs low.
// Loudest voice wins each frame; motors are silenced when nothing calls.
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    /// <summary>Static rumble mixer. Pumped by GamepadInputDriver.</summary>
    public static class RumbleManager
    {
        private static bool _subscribed;
        private static float _thunderUntil;
        private static float _thunderAt;

        // Tunables.
        private const float PredatorRadius = 30f;
        private const float HeartbeatHealth = 30f;

        /// <summary>Fire-and-forget thunder burst (call on lightning strike).</summary>
        public static void Thunder()
        {
            _thunderAt = Time.time;
            _thunderUntil = _thunderAt + 0.9f;
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            DayNightCycle.OnLightningStrike += Thunder;
        }

        internal static void InternalUpdate()
        {
            EnsureSubscribed();
            if (!GamepadInput.IsConnected)
            {
                XInputPad.SetVibration(0f, 0f);
                return;
            }

            float left = 0f, right = 0f;

            // Thunder: strong, fast decay.
            float now = Time.time;
            if (now < _thunderUntil)
            {
                float t = 1f - (now - _thunderAt) / 0.9f;
                float e = t * t;
                left = Mathf.Max(left, e * 0.9f);
                right = Mathf.Max(right, e * 0.7f);
            }

            var boot = GameBootstrap.Instance;
            var sim = boot != null ? boot.Sim : null;
            if (sim != null && sim.State != null && sim.State.Agent != null)
            {
                AgentState a = sim.State.Agent;

                // Predator tremor: low motor, scales with closeness.
                float nearest = float.MaxValue;
                var entities = sim.State.Entities;
                if (entities != null)
                {
                    for (int i = 0; i < entities.Count; i++)
                    {
                        var e = entities[i];
                        if (e == null || e.Kind != EntityKind.Predator) continue;
                        if (e.Health <= 0f) continue;
                        float dx = e.X - a.X, dz = e.Z - a.Z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d < nearest) nearest = d;
                    }
                }
                if (nearest < PredatorRadius)
                {
                    float closeness = 1f - nearest / PredatorRadius;
                    // Uneasy tremor: slow pulse on the low motor.
                    float pulse = 0.5f + 0.5f * Mathf.Sin(now * 9f);
                    left = Mathf.Max(left, closeness * (0.10f + 0.22f * pulse));
                }

                // Heartbeat: lub-dub when health is critical.
                if (a.IsAlive && a.Health < HeartbeatHealth)
                {
                    float urgency = 1f - Mathf.Clamp01(a.Health / HeartbeatHealth);
                    float period = Mathf.Lerp(1.1f, 0.55f, urgency);
                    float ph = (now % period) / period;
                    float thump = 0f;
                    if (ph < 0.10f) thump = 1f - ph / 0.10f;          // lub
                    else if (ph > 0.22f && ph < 0.32f) thump = 1f - (ph - 0.22f) / 0.10f; // dub
                    float amp = 0.25f + 0.55f * urgency;
                    left = Mathf.Max(left, thump * amp);
                    right = Mathf.Max(right, thump * amp * 0.6f);
                }
            }

            XInputPad.SetVibration(left, right);
        }
    }
}
