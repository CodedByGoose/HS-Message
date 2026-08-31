using System.Collections.Generic;
using UnityEngine;

namespace HSMessage
{
    /// <summary>
    /// Owns every Alt+key combination while Hearthstone Access is running.
    ///
    /// Why a whole layer rather than a handful of bindings: HSA's AccessibleKey
    /// only ever tests Shift and Ctrl. It never looks at Alt, so its plain-key
    /// bindings still fire while Alt is held. Alt+Left would otherwise both
    /// cycle our conversations and move HSA's item cursor. Claiming the entire
    /// Alt layer, and suppressing HSA's input handler for those frames, is the
    /// only way to get a clean split.
    /// </summary>
    internal static class AltLayer
    {
        /// <summary>
        /// Keys we have already acted on, held until they are released.
        ///
        /// HSA reacts on key-UP (AccessibleKey.IsPressed uses Input.GetKeyUp)
        /// while we react on key-DOWN. If the user let go of Alt before letting
        /// go of the number key, HSA would see a bare key-up and act on it. So
        /// we keep suppressing until every consumed key is released, plus one
        /// frame of slack.
        /// </summary>
        private static readonly HashSet<KeyCode> Consumed = new HashSet<KeyCode>();
        private static readonly List<KeyCode> Releasing = new List<KeyCode>();

        internal static bool AltHeld
        {
            get { return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); }
        }

        /// <summary>
        /// Asked by the Harmony prefix on AccessibilityMgr.HandleKeyboardInput.
        /// Returning true makes HSA skip its entire input handling for this
        /// frame.
        /// </summary>
        internal static bool ShouldSuppressHsa()
        {
            // While composing a reply we own every key, not just the Alt layer.
            // HSA should already be standing down because we called its
            // AllowTextInput, but belt and braces: a stray keystroke reaching
            // the game while you are mid-sentence would be nasty.
            if (Compose.Active) return true;

            // Otherwise never interfere with a text field HSA itself opened.
            if (HsaBridge.IsTextInputAllowed()) return false;

            return AltHeld || Consumed.Count > 0;
        }

        /// <summary>
        /// Forget any keys we were holding suppression open for.
        ///
        /// Called around composing, because Tick does not run then, so nothing
        /// would notice those keys being released.
        /// </summary>
        internal static void ResetConsumed()
        {
            Consumed.Clear();
            Releasing.Clear();
        }

        internal static void Tick()
        {
            // Deferred by one frame, so HSA never observes the key-up of
            // something we handled.
            for (int i = 0; i < Releasing.Count; i++) Consumed.Remove(Releasing[i]);
            Releasing.Clear();

            // Tested with GetKey rather than GetKeyUp deliberately. GetKeyUp is
            // a single-frame edge, and an edge can be missed: alt-tabbing away,
            // or any frame where this method does not run. A missed edge used to
            // strand a key in here forever, and because a non-empty set
            // suppresses HSA's entire input handler, that locked the player out
            // of the game until they restarted it. Asking whether the key is
            // still physically down cannot be missed.
            foreach (var key in Consumed)
                if (!Input.GetKey(key)) Releasing.Add(key);

            // Long readouts stop on any keypress, matching what HSA trains you
            // to expect.
            if (Speech.CancelOnNextKey && Input.anyKeyDown && !AltHeld)
                Speech.Silence();

            if (!AltHeld) return;
            if (HsaBridge.IsTextInputAllowed()) return;

            Dispatch();
        }

        private static bool Hit(KeyCode key)
        {
            if (!Input.GetKeyDown(key)) return false;
            Consumed.Add(key);
            return true;
        }

        private static void Dispatch()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Alt+Shift+1 .. Alt+Shift+9 -- jump straight to a person. Slots are
            // assigned in first-contact order and never shuffle, so Alt+Shift+3
            // keeps meaning the same person for the whole session. Handy once
            // more than a couple of people are talking to you.
            if (shift)
            {
                for (int i = 0; i < 9; i++)
                {
                    if (Hit((KeyCode)((int)KeyCode.Alpha1 + i)))
                    {
                        Speech.Say(ChatStore.SelectConversation(i));
                        return;
                    }
                }
            }
            else
            {
                // Alt+1 .. Alt+9, then Alt+0 for the tenth -- the last ten
                // messages RECEIVED from whoever you are currently on, counting
                // back from the newest. Your own replies do not take up slots.
                for (int i = 0; i < 9; i++)
                {
                    if (Hit((KeyCode)((int)KeyCode.Alpha1 + i)))
                    {
                        Speech.Say(ChatStore.SelectRecent(i + 1));
                        return;
                    }
                }

                if (Hit(KeyCode.Alpha0)) { Speech.Say(ChatStore.SelectRecent(10)); return; }
            }

            if (Hit(KeyCode.S)) { Speech.Say(ChatStore.Summary()); return; }

            // Reply to whoever you are currently on, inline. No window, no
            // friends list.
            if (Hit(KeyCode.M)) { Compose.Begin(); return; }

            if (Hit(KeyCode.LeftArrow)) { Speech.Say(ChatStore.CycleConversation(-1)); return; }
            if (Hit(KeyCode.RightArrow)) { Speech.Say(ChatStore.CycleConversation(1)); return; }

            if (Hit(KeyCode.UpArrow)) { Speech.Say(ChatStore.MoveMessage(-1)); return; }
            if (Hit(KeyCode.DownArrow)) { Speech.Say(ChatStore.MoveMessage(1)); return; }

            if (Hit(KeyCode.Home)) { Speech.Say(ChatStore.JumpToEdge(true)); return; }
            if (Hit(KeyCode.End)) { Speech.Say(ChatStore.JumpToEdge(false)); return; }

            if (Hit(KeyCode.Space)) { Speech.Say(ChatStore.NewestUnread()); return; }

            if (Hit(KeyCode.R)) { Speech.SayInterruptible(ChatStore.ReadWholeConversation()); return; }
            if (Hit(KeyCode.L)) { Speech.SayInterruptible(ChatStore.ListSlots()); return; }
            if (Hit(KeyCode.T)) { Speech.Say(ChatStore.DescribeCurrentTimestamp()); return; }

            if (Hit(KeyCode.C)) { CopyCurrent(); return; }
            if (Hit(KeyCode.B)) { BrailleCurrent(); return; }

            if (Hit(KeyCode.Backspace)) { Speech.Silence(); return; }

            // Rolling record of everything HSA has said, opt-in via config.
            if (Hit(KeyCode.Comma)) { Speech.Say(Transcript.Move(-1)); return; }
            if (Hit(KeyCode.Period)) { Speech.Say(Transcript.Move(1)); return; }
            if (Hit(KeyCode.Slash)) { Speech.Say(Transcript.Repeat()); return; }

            if (Hit(KeyCode.H)) { Speech.SayInterruptible(HelpText); return; }
        }

        private static void CopyCurrent()
        {
            var text = ChatStore.CurrentMessageText();
            if (string.IsNullOrEmpty(text))
            {
                Speech.Say(Strings.NoMessages);
                return;
            }

            try
            {
                GUIUtility.systemCopyBuffer = text;
                Speech.Say("Copied.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Clipboard copy failed: " + e.Message);
                Speech.Say("Could not copy.");
            }
        }

        private static void BrailleCurrent()
        {
            var text = ChatStore.CurrentMessageText();
            if (string.IsNullOrEmpty(text))
            {
                Speech.Say(Strings.NoMessages);
                return;
            }

            Speech.Braille(text);

            // Deliberately silent when a display is present: the whole point of
            // this key is reading mid-combat without touching speech.
            if (!Speech.HasBrailleDisplay())
                Speech.Say("No braille display detected.");
        }

        private const string HelpText =
            "Chat buffer commands, all with Alt held. " +
            "Alt plus 1 through 9, and Alt plus 0 for the tenth, read the last " +
            "ten messages received from this person, starting with the newest. " +
            "Alt plus up and down, older and newer messages, including your own replies. " +
            "Alt plus left and right, switch between people. " +
            "Alt plus shift plus 1 through 9, jump straight to a person. " +
            "Alt plus M, write a reply to this person. " +
            "Alt plus S, summary of unread. " +
            "Alt plus Home and End, first and last message. " +
            "Alt plus space, newest unread message. " +
            "Alt plus R, read the whole conversation. " +
            "Alt plus L, list people. " +
            "Alt plus T, when the current message arrived. " +
            "Alt plus C, copy the current message. " +
            "Alt plus B, send the current message to a braille display. " +
            "Alt plus comma and period, move through everything the game has said. " +
            "Alt plus slash, repeat that line. " +
            "Alt plus backspace, stop talking.";
    }
}
