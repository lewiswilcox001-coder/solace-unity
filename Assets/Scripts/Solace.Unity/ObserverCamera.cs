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

        private const float MinDist = 4f;
        private const float MaxDist = 14f;

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

            float y = world.SampleHeight(a.X, a.Z);
            var target = new Vector3(a.X, y + 1.2f, a.Z);
            Vector3 desired = target + OffsetDir() * _dist;

            // Terrain clearance.
            float groundY = world.SampleHeight(desired.x, desired.z) + 0.6f;
            if (desired.y < groundY) desired.y = groundY;

            float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.LookAt(target);
        }

        private static bool HudPointerOverUI()
        {
            var hud = GameBootstrap.Instance != null ? GameBootstrap.Instance.Hud : null;
            return hud != null && hud.IsTyping;
        }
    }
}
