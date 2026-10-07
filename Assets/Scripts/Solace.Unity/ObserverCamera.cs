// Solace.Unity — the observer camera.
//
// A follow camera that NEVER moves the character: it only frames him.
// Default: 7m behind, 3m boom. Drag to orbit, wheel to zoom (4–14m).
// Terrain clearance via WorldData.SampleHeight. SnapToAgent() reframes
// instantly (chapter changes, verification poses).
using UnityEngine;
using Solace.Core;

namespace Solace.Unity
{
    public class ObserverCamera : MonoBehaviour
    {
        private Camera _cam;
        private float _yaw = 180f;
        private float _pitch = 22f;
        private float _dist = 7f;
        private bool _dragging;
        private Vector2 _lastMouse;
        private float _holdUntil = -1f;

        // ---- documentary smoothing state
        private Vector3 _smoothLook;   // damped look target (never snaps)
        private bool _hasLook;
        private float _fov = 55f;      // smoothed field of view
        private float _speedSm;        // smoothed agent speed for FOV/lag feel
        private float _driftT;         // free clock for handheld drift

        private const float MinDist = 4f;
        private const float MaxDist = 14f;
        private const float BaseFov = 55f;

        /// <summary>Camera yaw in degrees, for the HUD compass.</summary>
        public float YawDegrees { get { return _yaw; } }

        private void Awake()
        {
            _cam = gameObject.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.60f, 0.74f, 0.86f);
            _cam.fieldOfView = 55f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 2200f;
            gameObject.tag = "MainCamera";
        }

        /// <summary>Hold a fixed pose (verification) for holdSeconds.</summary>
        public void SetPose(Vector3 position, Vector3 lookAt, float holdSeconds)
        {
            transform.position = position;
            transform.LookAt(lookAt);
            _holdUntil = Time.time + holdSeconds;
        }

        /// <summary>Reframe instantly behind the protagonist.</summary>
        public void SnapToAgent()
        {
            var sim = GameBootstrap.Instance != null ? GameBootstrap.Instance.Sim : null;
            if (sim == null || sim.State.Agent == null) return;
            AgentState a = sim.State.Agent;
            float y = sim.State.World.SampleHeight(a.X, a.Z);
            var target = new Vector3(a.X, y + 1.2f, a.Z);
            _yaw = a.Facing * Mathf.Rad2Deg + 180f;
            _pitch = 22f;
            _dist = 7f;
            transform.position = target + OffsetDir() * _dist;
            transform.LookAt(target);
            _smoothLook = target;
            _hasLook = true;
            _holdUntil = -1f;
        }

        private Vector3 OffsetDir()
        {
            float p = _pitch * Mathf.Deg2Rad;
            float yw = _yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yw) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(yw) * Mathf.Cos(p));
        }

        private void LateUpdate()
        {
            if (Time.time < _holdUntil) return; // verification pose holds
            var boot = GameBootstrap.Instance;
            var sim = boot != null ? boot.Sim : null;
            if (sim == null || sim.State.Agent == null || !sim.State.Agent.IsAlive) return;
            AgentState a = sim.State.Agent;
            WorldData world = sim.State.World;

            // Drag-orbit / wheel zoom. Never touches the character.
            if (Input.GetMouseButtonDown(0) && !HudPointerOverUI()) { _dragging = true; _lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(0)) _dragging = false;
            if (_dragging)
            {
                Vector2 m = Input.mousePosition;
                Vector2 d = m - _lastMouse;
                _lastMouse = m;
                _yaw -= d.x * 0.25f;
                _pitch = Mathf.Clamp(_pitch + d.y * 0.2f, 5f, 70f);
            }
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                _dist = Mathf.Clamp(_dist * (1f - wheel * 0.9f), MinDist, MaxDist);

            // Gamepad: right stick orbits, triggers zoom. Never touches the character.
            Vector2 padLook = GamepadInput.RightStick;
            if (padLook.sqrMagnitude > 0.0001f)
            {
                _yaw -= padLook.x * 130f * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch + padLook.y * 95f * Time.deltaTime, 5f, 70f);
            }
            float padZoom = GamepadInput.RightTrigger - GamepadInput.LeftTrigger;
            if (Mathf.Abs(padZoom) > 0.05f)
                _dist = Mathf.Clamp(_dist * (1f - padZoom * 1.6f * Time.deltaTime), MinDist, MaxDist);

            float y = world.SampleHeight(a.X, a.Z);
            var target = new Vector3(a.X, y + 1.2f, a.Z);

            // Lead the fox a touch: frame where it's going, not where it was.
            // (Documentary cameramen always lead the animal.)
            _speedSm = Mathf.Lerp(_speedSm, a.Speed, 1f - Mathf.Exp(-3f * Time.deltaTime));
            float lead = Mathf.Clamp(_speedSm * 0.22f, 0f, 1.6f);
            float fr = a.Facing;
            Vector3 leadTarget = target + new Vector3(Mathf.Sin(fr), 0f, Mathf.Cos(fr)) * lead;

            if (!_hasLook) { _smoothLook = leadTarget; _hasLook = true; }
            // The look target glides — the camera never whips.
            float lk = 1f - Mathf.Exp(-5f * Time.deltaTime);
            _smoothLook = Vector3.Lerp(_smoothLook, leadTarget, lk);

            Vector3 desired = target + OffsetDir() * _dist;

            // Terrain clearance.
            float groundY = world.SampleHeight(desired.x, desired.z) + 0.6f;
            if (desired.y < groundY) desired.y = groundY;

            // Follow like a documentary rig: gentle lag, more lag at speed so
            // running reads as fast without the camera feeling glued on.
            float followK = 1f - Mathf.Exp(-(3.4f - Mathf.Clamp01(_speedSm / 10f) * 1.4f) * Time.deltaTime);
            Vector3 pos = Vector3.Lerp(transform.position, desired, followK);

            // Handheld breath: barely-there drift so the frame feels held, not mounted.
            // Scaled by the accessibility sway setting (0 = off for motion sensitivity).
            _driftT += Time.deltaTime;
            float sway = AccessibilitySettings.SwayFactor;
            pos += new Vector3(
                Mathf.Sin(_driftT * 0.45f) * 0.05f,
                Mathf.Sin(_driftT * 0.62f + 1.7f) * 0.035f,
                Mathf.Sin(_driftT * 0.38f + 3.1f) * 0.05f) * sway;
            transform.position = pos;

            // Rotation eases toward the look target — no more per-frame LookAt snap.
            Vector3 lookDir = _smoothLook - transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion wantRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                float rk = 1f - Mathf.Exp(-6f * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, rk);
            }

            // Speed feel: the frame widens a breath as the fox runs.
            float wantFov = BaseFov + Mathf.Clamp01(_speedSm / 10f) * 5f;
            _fov = Mathf.Lerp(_fov, wantFov, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            _cam.fieldOfView = _fov;
        }

        private static bool HudPointerOverUI()
        {
            var hud = GameBootstrap.Instance != null ? GameBootstrap.Instance.Hud : null;
            return hud != null && hud.IsTyping;
        }
    }
}
