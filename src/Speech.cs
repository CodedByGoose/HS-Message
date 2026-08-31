using System;
using System.Reflection;
using BepInEx.Logging;

namespace HearthstoneChatBuffer
{
    /// <summary>
    /// Talks to the screen reader directly through Tolk, deliberately bypassing
    /// Hearthstone Access's own speech queue.
    ///
    /// This matters. HSA's AccessibleSpeechMgr.Update() calls InterruptTexts()
    /// whenever Input.anyKeyDown is true, and InterruptTexts drains the entire
    /// pending queue rather than just the current utterance. Anything we pushed
    /// through that queue would be destroyed by the very keypress that asked for
    /// it. Calling Tolk straight is immune to that.
    ///
    /// Everything is resolved by reflection so the plugin never needs a
    /// compile-time reference to the game's assemblies, and therefore does not
    /// need rebuilding every time Hearthstone or HSA updates.
    /// </summary>
    internal static class Speech
    {
        private static bool _resolved;
        private static MethodInfo _output;   // Output(string, bool interrupt)
        private static MethodInfo _braille;  // Braille(string)
        private static MethodInfo _silence;  // Silence()
        private static MethodInfo _hasBraille;

        private static ManualLogSource Log => Plugin.Log;

        /// <summary>
        /// Set when we speak something long enough that the user may want to cut
        /// it short with any old keypress, the way HSA behaves.
        /// </summary>
        internal static bool CancelOnNextKey;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            Type tolk = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    tolk = asm.GetType("DavyKager.Tolk", false);
                }
                catch (Exception)
                {
                    // A few dynamic assemblies throw on GetType. Nothing to do.
                    continue;
                }

                if (tolk != null) break;
            }

            if (tolk == null)
            {
                Log.LogError(
                    "Could not find DavyKager.Tolk. Is Hearthstone Access installed? " +
                    "Chat review will be silent until it is.");
                return;
            }

            _output = tolk.GetMethod("Output", new[] { typeof(string), typeof(bool) });
            _braille = tolk.GetMethod("Braille", new[] { typeof(string) });
            _silence = tolk.GetMethod("Silence", Type.EmptyTypes);
            _hasBraille = tolk.GetMethod("HasBraille", Type.EmptyTypes);

            if (_output == null)
                Log.LogError("Found DavyKager.Tolk but not its Output method. Tolk API may have changed.");
            else
                Log.LogInfo("Screen reader output wired up through Tolk.");
        }

        /// <summary>Speak immediately, cutting off whatever is in progress.</summary>
        internal static void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            Resolve();
            if (_output == null) return;

            CancelOnNextKey = false;

            try
            {
                // interrupt: true. Review speech should never queue behind the
                // combat chatter the user is trying to read past.
                _output.Invoke(null, new object[] { text, true });
            }
            catch (Exception e)
            {
                Log.LogError("Tolk.Output failed: " + e);
            }
        }

        /// <summary>
        /// Speak something long (a whole conversation), and let any subsequent
        /// keypress stop it, which is what HSA trains you to expect.
        /// </summary>
        internal static void SayInterruptible(string text)
        {
            Say(text);
            if (_output != null) CancelOnNextKey = true;
        }

        /// <summary>
        /// Push text to a braille display without speaking it. This is the one
        /// way to read a message mid-combat without stepping on game speech at
        /// all.
        ///
        /// UNTESTED: written against the documented Tolk API, but never
        /// exercised against real hardware. If it misbehaves it should fail
        /// quietly rather than break anything else.
        /// </summary>
        internal static void Braille(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            Resolve();
            if (_braille == null) return;

            try
            {
                _braille.Invoke(null, new object[] { text });
            }
            catch (Exception e)
            {
                Log.LogWarning("Tolk.Braille failed: " + e.Message);
            }
        }

        internal static bool HasBrailleDisplay()
        {
            Resolve();
            if (_hasBraille == null) return false;

            try
            {
                return (bool)_hasBraille.Invoke(null, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void Silence()
        {
            Resolve();
            if (_silence == null) return;

            try
            {
                _silence.Invoke(null, null);
            }
            catch (Exception)
            {
                // Silencing is best-effort by nature.
            }

            CancelOnNextKey = false;
        }
    }
}
