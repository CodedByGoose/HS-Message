using System;
using System.Reflection;
using HarmonyLib;

namespace HSMessage
{
    /// <summary>
    /// Small reflection helpers for talking to Hearthstone Access without a
    /// compile-time reference to it.
    /// </summary>
    internal static class HsaBridge
    {
        private static bool _resolved;
        private static MethodInfo _isTextInputAllowed;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var mgr = AccessTools.TypeByName("Accessibility.AccessibilityMgr");
            if (mgr == null)
            {
                Plugin.Log.LogWarning(
                    "Accessibility.AccessibilityMgr not found. Assuming text input is never active.");
                return;
            }

            _isTextInputAllowed = AccessTools.Method(mgr, "IsTextInputAllowed");
            if (_isTextInputAllowed == null)
                Plugin.Log.LogWarning("AccessibilityMgr.IsTextInputAllowed not found; typing may be affected.");
        }

        /// <summary>
        /// True while HSA has handed the keyboard to a text field (entering a
        /// deck code, a BattleTag, and so on). We must keep our hands off the
        /// Alt layer entirely while that is true, or we would eat characters
        /// the user is trying to type.
        /// </summary>
        internal static bool IsTextInputAllowed()
        {
            Resolve();
            if (_isTextInputAllowed == null) return false;

            try
            {
                return (bool)_isTextInputAllowed.Invoke(null, null);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
