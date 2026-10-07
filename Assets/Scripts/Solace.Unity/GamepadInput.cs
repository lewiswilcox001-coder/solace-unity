// Solace.Unity — high-level gamepad API.
//
// Single choke point for all controller input. Backed by XInput on Windows
// (see XInputPad); everywhere else it quietly reports "not connected" and
// the game falls back to keyboard + mouse.
//
// Button map (Xbox layout):
//   A select / confirm            B back / close
//   X inventory                   Y journal
//   LB map                        RB lineage
//   Back chat                     Start settings
//   L3 cinematic director         R3 snap camera to fox
//   D-pad: menu navigation when a panel is open, shortcuts on the HUD
//   Back+Start chord: bug report (F12 equivalent)
//   Triggers: camera zoom. Right stick: camera orbit.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Solace.Unity
{
    /// <summary>Xbox-layout buttons.</summary>
    public enum PadButton
    {
        A = 0, B = 1, X = 2, Y = 3,
        LB = 4, RB = 5, Back = 6, Start = 7,
        L3 = 8, R3 = 9,
        DUp = 10, DDown = 11, DLeft = 12, DRight = 13
    }

    /// <summary>
    /// Static gamepad state. Updated once per frame by GamepadInputDriver
    /// (self-bootstrapped, DontDestroyOnLoad). All queries are safe to call
    /// when no controller is connected — they return neutral values.
    /// </summary>
    public static class GamepadInput
    {
        private const float StickDeadzone = 0.18f;

        private static ushort _buttons;
        private static ushort _prevButtons;
        private static Vector2 _leftStick;
        private static Vector2 _rightStick;
        private static float _leftTrigger;
        private static float _rightTrigger;
        private static bool _connected;

        public static bool IsConnected { get { return _connected; } }
        public static Vector2 LeftStick { get { return _leftStick; } }
        public static Vector2 RightStick { get { return _rightStick; } }
        public static float LeftTrigger { get { return _leftTrigger; } }
        public static float RightTrigger { get { return _rightTrigger; } }

        private static ushort FlagFor(PadButton b)
        {
            switch (b)
            {
                case PadButton.A: return XInputPad.A;
                case PadButton.B: return XInputPad.B;
                case PadButton.X: return XInputPad.X;
                case PadButton.Y: return XInputPad.Y;
                case PadButton.LB: return XInputPad.LeftShoulder;
                case PadButton.RB: return XInputPad.RightShoulder;
                case PadButton.Back: return XInputPad.Back;
                case PadButton.Start: return XInputPad.Start;
                case PadButton.L3: return XInputPad.LeftThumb;
                case PadButton.R3: return XInputPad.RightThumb;
                case PadButton.DUp: return XInputPad.DpadUp;
                case PadButton.DDown: return XInputPad.DpadDown;
                case PadButton.DLeft: return XInputPad.DpadLeft;
                case PadButton.DRight: return XInputPad.DpadRight;
                default: return 0;
            }
        }

        /// <summary>True while the button is held.</summary>
        public static bool GetButton(PadButton b) { return (_buttons & FlagFor(b)) != 0; }

        /// <summary>True on the frame the button was pressed.</summary>
        public static bool GetButtonDown(PadButton b)
        {
            ushort f = FlagFor(b);
            return (_buttons & f) != 0 && (_prevButtons & f) == 0;
        }

        /// <summary>True on the frame the button was released.</summary>
        public static bool GetButtonUp(PadButton b)
        {
            ushort f = FlagFor(b);
            return (_buttons & f) == 0 && (_prevButtons & f) != 0;
        }

        /// <summary>True on the frame both buttons became held together.</summary>
        public static bool ChordDown(PadButton a, PadButton b)
        {
            return GetButton(a) && GetButton(b) &&
                   (GetButtonDown(a) || GetButtonDown(b));
        }

        private static Vector2 Deadzone(float x, float y)
        {
            var v = new Vector2(x, y);
            float m = v.magnitude;
            if (m < StickDeadzone) return Vector2.zero;
            // Rescale so the edge of the deadzone reads as zero.
            float s = (m - StickDeadzone) / (1f - StickDeadzone);
            return v.normalized * Mathf.Min(s, 1f);
        }

        internal static void InternalUpdate()
        {
            _prevButtons = _buttons;
            ushort btn;
            float lx, ly, rx, ry, lt, rt;
            _connected = XInputPad.GetState(out btn, out lx, out ly, out rx, out ry, out lt, out rt);
            if (_connected)
            {
                _buttons = btn;
                _leftStick = Deadzone(lx, ly);
                _rightStick = Deadzone(rx, ry);
                _leftTrigger = lt;
                _rightTrigger = rt;
            }
            else
            {
                _buttons = 0;
                _leftStick = Vector2.zero;
                _rightStick = Vector2.zero;
                _leftTrigger = 0f;
                _rightTrigger = 0f;
            }
        }

        // -- d-pad menu navigation -------------------------------------------------
        //
        // Unity's StandaloneInputModule already maps A (submit) and B (cancel)
        // and moves UI focus with the LEFT stick. The d-pad is not wired on
        // Windows, so we drive EventSystem selection ourselves here. Both
        // paths write the same selection, so they cooperate.

        /// <summary>
        /// Call once per frame while a panel is open. Moves UI focus with the
        /// d-pad and returns true if the d-pad was consumed for navigation.
        /// </summary>
        public static bool UpdateMenuNavigation()
        {
            if (!_connected) return false;
            var es = EventSystem.current;
            if (es == null) return false;

            Vector2 dir = Vector2.zero;
            if (GetButtonDown(PadButton.DUp)) dir = Vector2.up;
            else if (GetButtonDown(PadButton.DDown)) dir = Vector2.down;
            else if (GetButtonDown(PadButton.DLeft)) dir = Vector2.left;
            else if (GetButtonDown(PadButton.DRight)) dir = Vector2.right;
            if (dir == Vector2.zero) return false;

            GameObject cur = es.currentSelectedGameObject;
            Selectable sel = cur != null ? cur.GetComponent<Selectable>() : null;
            if (sel != null)
            {
                Selectable next = sel.FindSelectable(dir);
                if (next != null) { es.SetSelectedGameObject(next.gameObject); return true; }
            }
            return true; // consumed even with nowhere to go
        }

        /// <summary>Focus the first button under root (call when a panel opens).</summary>
        public static void FocusFirstButton(GameObject root)
        {
            if (!_connected || root == null) return;
            var es = EventSystem.current;
            if (es == null) return;
            var buttons = root.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b != null && b.gameObject.activeInHierarchy && b.interactable)
                {
                    es.SetSelectedGameObject(b.gameObject);
                    return;
                }
            }
        }

        /// <summary>Clear UI focus (call when panels close).</summary>
        public static void ClearFocus()
        {
            var es = EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);
        }
    }

    /// <summary>
    /// Self-bootstrapped per-frame pump for GamepadInput + RumbleManager.
    /// Created once via RuntimeInitializeOnLoadMethod; survives scene loads.
    /// </summary>
    internal sealed class GamepadInputDriver : MonoBehaviour
    {
        private static bool _booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_booted) return;
            _booted = true;
            var go = new GameObject("GamepadInputDriver");
            go.AddComponent<GamepadInputDriver>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            GamepadInput.InternalUpdate();
            RumbleManager.InternalUpdate();
        }

        private void OnDisable()
        {
            // Never leave the motors buzzing.
            XInputPad.SetVibration(0f, 0f);
        }
    }
}
