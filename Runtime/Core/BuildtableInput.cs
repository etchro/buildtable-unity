using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// One place that knows which input backend the project uses.
    /// <see cref="Input"/> throws in projects that disabled the legacy Input
    /// Manager, so every key poll in the SDK goes through here.
    /// </summary>
    public static class BuildtableInput
    {
        public static bool GetKeyDown(KeyCode keyCode)
        {
#if !ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return BuildtableInputSystemBridge.WasPressedThisFrame(keyCode);
#endif
        }
    }
}
