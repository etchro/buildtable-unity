using System;
using System.Reflection;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Polls a <see cref="KeyCode"/> through the Input System package when the
    /// legacy Input Manager is disabled.
    ///
    /// Reflection rather than a direct reference on purpose: an asmdef reference
    /// to <c>Unity.InputSystem</c> is a hard dependency, and a project without
    /// the package installed would fail to compile the whole SDK. This path only
    /// runs in Input System-only projects; everything else uses UnityEngine.Input
    /// directly.
    /// </summary>
    public static class BuildtableInputSystemBridge
    {
        private static bool _resolved;
        private static bool _available;
        private static PropertyInfo _currentKeyboard;
        private static PropertyInfo _keyboardIndexer;
        private static Type _keyEnum;
        private static PropertyInfo _wasPressedThisFrame;
        private static bool _warnedMissingKey;

        public static bool WasPressedThisFrame(KeyCode keyCode)
        {
            if (!Resolve()) return false;

            try
            {
                var keyboard = _currentKeyboard.GetValue(null);
                if (keyboard == null) return false;

                var key = ToKeyEnum(keyCode);
                if (key == null) return false;

                var control = _keyboardIndexer.GetValue(keyboard, new[] { key });
                if (control == null) return false;

                return (bool)_wasPressedThisFrame.GetValue(control);
            }
            catch
            {
                // A layout change or an API rename must not spam a running game.
                _available = false;
                return false;
            }
        }

        private static bool Resolve()
        {
            if (_resolved) return _available;
            _resolved = true;

            var keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
            _keyEnum = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
            var buttonType = Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl, Unity.InputSystem");

            if (keyboardType == null || _keyEnum == null || buttonType == null)
            {
                Debug.LogWarning(
                    "[Buildtable] The Input System package was not found, so the bug reporter hotkey is " +
                    "inactive. Call Buildtable.ShowBugReporter() from your own input handling instead.");
                return false;
            }

            _currentKeyboard = keyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            _keyboardIndexer = keyboardType.GetProperty("Item", new[] { _keyEnum });
            _wasPressedThisFrame = buttonType.GetProperty("wasPressedThisFrame");

            _available = _currentKeyboard != null && _keyboardIndexer != null && _wasPressedThisFrame != null;
            return _available;
        }

        /// <summary>
        /// Maps a KeyCode onto the Input System's Key enum by name. The two
        /// enums agree on most names (F1–F12, letters, digits); the handful that
        /// differ are spelled out below.
        /// </summary>
        private static object ToKeyEnum(KeyCode keyCode)
        {
            var name = keyCode.ToString();
            switch (keyCode)
            {
                case KeyCode.BackQuote: name = "Backquote"; break;
                case KeyCode.Return: name = "Enter"; break;
                case KeyCode.KeypadEnter: name = "NumpadEnter"; break;
                case KeyCode.LeftControl: name = "LeftCtrl"; break;
                case KeyCode.RightControl: name = "RightCtrl"; break;
                case KeyCode.Alpha0: name = "Digit0"; break;
                case KeyCode.Alpha1: name = "Digit1"; break;
                case KeyCode.Alpha2: name = "Digit2"; break;
                case KeyCode.Alpha3: name = "Digit3"; break;
                case KeyCode.Alpha4: name = "Digit4"; break;
                case KeyCode.Alpha5: name = "Digit5"; break;
                case KeyCode.Alpha6: name = "Digit6"; break;
                case KeyCode.Alpha7: name = "Digit7"; break;
                case KeyCode.Alpha8: name = "Digit8"; break;
                case KeyCode.Alpha9: name = "Digit9"; break;
            }

            try
            {
                return Enum.Parse(_keyEnum, name, true);
            }
            catch
            {
                if (!_warnedMissingKey)
                {
                    _warnedMissingKey = true;
                    Debug.LogWarning(
                        $"[Buildtable] Toggle key {keyCode} has no Input System equivalent; " +
                        "pick another key or open the reporter from your own input handling.");
                }
                return null;
            }
        }
    }
}
