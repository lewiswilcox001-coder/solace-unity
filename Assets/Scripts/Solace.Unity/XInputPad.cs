// Solace.Unity — raw XInput gamepad access (Windows).
//
// Why XInput instead of Unity's legacy Input Manager:
//   - Unity's default InputManager has no right-stick / trigger / d-pad axes,
//     and ProjectSettings/InputManager.asset is not tracked in this repo, so
//     we cannot rely on (or safely ship) custom axis definitions.
//   - XInput gives buttons, both sticks, both triggers AND rumble with zero
//     project-settings dependency. It is the native API on Windows and Xbox.
//
// Graceful degradation: on non-Windows platforms, or when no XInput device
// is present, every call is a no-op and Available/IsConnected stay false.
// Keyboard + mouse keep working exactly as before.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Solace.Unity
{
    /// <summary>Thin, safe wrapper around XInput (Windows only).</summary>
    internal static class XInputPad
    {
        // XInput button flags (wButtons bitmask).
        public const ushort DpadUp = 0x0001;
        public const ushort DpadDown = 0x0002;
        public const ushort DpadLeft = 0x0004;
        public const ushort DpadRight = 0x0008;
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;
        public const ushort LeftThumb = 0x0040;
        public const ushort RightThumb = 0x0080;
        public const ushort LeftShoulder = 0x0100;
        public const ushort RightShoulder = 0x0200;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        private struct Gamepad
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct State
        {
            public uint dwPacketNumber;
            public Gamepad Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Vibration
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        // Try modern XInput first, fall back to older versions.
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState14(uint dwUserIndex, ref State pState);
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern uint SetState14(uint dwUserIndex, ref Vibration pVibration);
        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState13(uint dwUserIndex, ref State pState);
        [DllImport("xinput1_3.dll", EntryPoint = "XInputSetState")]
        private static extern uint SetState13(uint dwUserIndex, ref Vibration pVibration);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState91(uint dwUserIndex, ref State pState);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        private static extern uint SetState91(uint dwUserIndex, ref Vibration pVibration);

        private const uint ErrorSuccess = 0;

        private static int _api = -1; // -1 unknown, 0 = 1.4, 1 = 1.3, 2 = 9.1.0, 3 = none
        private static bool _probed;

        private static bool IsWindows
        {
            get
            {
                return Application.platform == RuntimePlatform.WindowsPlayer ||
                       Application.platform == RuntimePlatform.WindowsEditor;
            }
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            if (!IsWindows) { _api = 3; return; }
            var s = new State();
            try { if (GetState14(0, ref s) == ErrorSuccess || true) { _api = 0; return; } }
            catch (Exception) { /* fall through */ }
            try { GetState13(0, ref s); _api = 1; return; }
            catch (Exception) { /* fall through */ }
            try { GetState91(0, ref s); _api = 2; return; }
            catch (Exception) { /* fall through */ }
            _api = 3;
        }

        /// <summary>True if an XInput API is present on this machine.</summary>
        public static bool Available
        {
            get { Probe(); return _api >= 0 && _api < 3; }
        }

        /// <summary>Snapshot of controller 0. Returns false when not connected.</summary>
        public static bool GetState(out ushort buttons, out float lx, out float ly,
                                   out float rx, out float ry, out float lt, out float rt)
        {
            buttons = 0; lx = ly = rx = ry = lt = rt = 0f;
            Probe();
            if (_api < 0 || _api >= 3) return false;
            var s = new State();
            uint r;
            try
            {
                if (_api == 0) r = GetState14(0, ref s);
                else if (_api == 1) r = GetState13(0, ref s);
                else r = GetState91(0, ref s);
            }
            catch (Exception)
            {
                _api = 3;
                return false;
            }
            if (r != ErrorSuccess) return false;
            buttons = s.Gamepad.wButtons;
            lx = s.Gamepad.sThumbLX / 32768f;
            ly = s.Gamepad.sThumbLY / 32768f;
            rx = s.Gamepad.sThumbRX / 32768f;
            ry = s.Gamepad.sThumbRY / 32768f;
            lt = s.Gamepad.bLeftTrigger / 255f;
            rt = s.Gamepad.bRightTrigger / 255f;
            return true;
        }

        /// <summary>Set rumble motors, 0..1 each. No-op when unavailable.</summary>
        public static void SetVibration(float leftMotor, float rightMotor)
        {
            Probe();
            if (_api < 0 || _api >= 3) return;
            var v = new Vibration
            {
                wLeftMotorSpeed = (ushort)(Mathf.Clamp01(leftMotor) * 65535f),
                wRightMotorSpeed = (ushort)(Mathf.Clamp01(rightMotor) * 65535f)
            };
            try
            {
                if (_api == 0) SetState14(0, ref v);
                else if (_api == 1) SetState13(0, ref v);
                else SetState91(0, ref v);
            }
            catch (Exception)
            {
                _api = 3;
            }
        }
    }
}
