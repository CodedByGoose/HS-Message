using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace HSMessage
{
    /// <summary>
    /// An inline reply box. No window, no friends list, no navigating anything:
    /// press the key, type, press Enter.
    ///
    /// Hearthstone Access never implemented chat input -- ChatMgr.HandleGUIInput
    /// returns immediately with "Chat is not implemented yet" -- so there is no
    /// real edit control anywhere for a screen reader to find. We therefore run
    /// our own tiny line editor: characters come from Input.inputString, and we
    /// speak the feedback ourselves through Tolk, because nothing else will.
    ///
    /// While composing we call HSA's own AllowTextInput(), which is the
    /// sanctioned way to tell it to keep its hands off the keyboard. It is the
    /// same mechanism HSA uses for deck code entry.
    /// </summary>
    internal static class Compose
    {
        private static readonly StringBuilder Buffer = new StringBuilder();
        private static string _peerName;
        private static object _peerPlayer;
        private static int _beganFrame = -1;

        internal static bool Active { get; private set; }

        // Resolved lazily, all by name, like everything else here.
        private static bool _resolved;
        private static MethodInfo _allowTextInput;
        private static MethodInfo _disallowTextInput;
        private static MethodInfo _whisperMgrGet;
        private static MethodInfo _sendWhisper;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var mgr = AccessTools.TypeByName("Accessibility.AccessibilityMgr");
            if (mgr != null)
            {
                _allowTextInput = AccessTools.Method(mgr, "AllowTextInput");
                _disallowTextInput = AccessTools.Method(mgr, "DisallowTextInput");
            }

            var whisperMgr = AccessTools.TypeByName("BnetWhisperMgr");
            if (whisperMgr != null)
            {
                _whisperMgrGet = AccessTools.Method(whisperMgr, "Get");
                _sendWhisper = AccessTools.Method(whisperMgr, "SendWhisper");
            }

            if (_sendWhisper == null)
                Plugin.Log.LogError("BnetWhisperMgr.SendWhisper not found. Replying is disabled.");
        }

        // ------------------------------------------------------------- begin

        internal static void Begin()
        {
            Resolve();

            if (_sendWhisper == null)
            {
                Speech.Say("Replying is not available.");
                return;
            }

            var peer = ChatStore.CurrentPeerName();
            var player = ChatStore.CurrentPeerPlayer();

            if (string.IsNullOrEmpty(peer))
            {
                Speech.Say("No one selected.");
                return;
            }

            if (player == null)
            {
                // We only learn who someone actually is from a whisper going in
                // or out, so we can only reply to people already in the buffer.
                Speech.Say("Cannot reply to " + peer + " yet.");
                return;
            }

            Buffer.Length = 0;
            _peerName = peer;
            _peerPlayer = player;
            _beganFrame = Time.frameCount;
            Active = true;

            // AltLayer.Tick stops running while we are composing, so the Alt+M
            // that got us here would otherwise sit in its consumed set forever
            // and keep HSA suppressed after we finish.
            AltLayer.ResetConsumed();

            SetHsaTextInput(true);

            Speech.SayInterruptible(
                "Message to " + peer + ". Type, then Enter to send. " +
                "F2 reads it back, Escape cancels.");
        }

        private static void SetHsaTextInput(bool allow)
        {
            try
            {
                var m = allow ? _allowTextInput : _disallowTextInput;
                if (m != null) m.Invoke(null, null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not toggle HSA text input: " + e.Message);
            }
        }

        // -------------------------------------------------------------- tick

        /// <summary>
        /// Text comes from the IMGUI event stream rather than Input.inputString,
        /// which Unity 6 removed from the legacy input module. This is the
        /// better source anyway: it respects the keyboard layout and shift
        /// state, so punctuation and capitals arrive correctly rather than
        /// having to be reconstructed from raw key codes.
        ///
        /// Called from Runtime.OnGUI.
        /// </summary>
        internal static void HandleGuiEvent(Event e)
        {
            if (!Active || e == null) return;
            if (e.type != EventType.KeyDown) return;

            // The keypress that opened the box is still in flight this frame.
            // Without this, Alt+M would type an "m".
            if (Time.frameCount == _beganFrame) return;

            // Alt is our command layer, never text. Control plus Alt is left
            // alone because that is AltGr, which does produce characters on
            // several European layouts.
            if (e.alt && !e.control) return;

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Cancel(KeyCode.Escape); e.Use(); return;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Both are passed on because HSA treats them as one key:
                    // its CONFIRM binding accepts a keypad Enter release while
                    // matching on Return.
                    Send(KeyCode.Return, KeyCode.KeypadEnter); e.Use(); return;

                case KeyCode.F2:
                    ReadBack(); e.Use(); return;

                case KeyCode.Backspace:
                    Backspace(); e.Use(); return;
            }

            // Unity delivers the key code and the character as separate events,
            // so a printable character arrives here with keyCode None.
            char c = e.character;
            if (c == '\0' || char.IsControl(c)) return;

            Buffer.Append(c);
            e.Use();

            if (Plugin.EchoTypedCharacters.Value)
            {
                Speech.Say(SpeakableCharacter(c));
            }
            else if (Plugin.EchoTypedWords.Value && c == ' ')
            {
                var word = LastCompletedWord();
                if (!string.IsNullOrEmpty(word)) Speech.Say(word);
            }
        }

        private static string LastCompletedWord()
        {
            // Buffer currently ends with the space that triggered us.
            int end = Buffer.Length - 1;
            if (end <= 0) return null;

            int start = end - 1;
            while (start >= 0 && Buffer[start] != ' ') start--;
            start++;

            if (end - start <= 0) return null;
            return Buffer.ToString(start, end - start);
        }

        private static string SpeakableCharacter(char c)
        {
            if (c == ' ') return "space";
            return c.ToString();
        }

        private static void Backspace()
        {
            if (Buffer.Length == 0)
            {
                Speech.Say("Blank.");
                return;
            }

            char removed = Buffer[Buffer.Length - 1];
            Buffer.Length--;
            Speech.Say(SpeakableCharacter(removed));
        }

        private static void ReadBack()
        {
            Speech.SayInterruptible(
                Buffer.Length == 0 ? "Blank." : Buffer.ToString());
        }

        // ------------------------------------------------------------ finish

        private static void Cancel(params KeyCode[] terminators)
        {
            Buffer.Length = 0;
            End(terminators);
            Speech.Say("Cancelled.");
        }

        private static void Send(params KeyCode[] terminators)
        {
            var text = Buffer.ToString().Trim();

            if (text.Length == 0)
            {
                Buffer.Length = 0;
                End(terminators);
                Speech.Say("Nothing to send.");
                return;
            }

            var peer = _peerName;
            var player = _peerPlayer;

            Buffer.Length = 0;
            End(terminators);

            bool sent = false;
            try
            {
                var mgr = _whisperMgrGet.Invoke(null, null);
                if (mgr != null)
                    sent = (bool)_sendWhisper.Invoke(mgr, new object[] { player, text });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("SendWhisper failed: " + e);
            }

            // On success the game raises its own chat bubble for the outgoing
            // message, which our ChatBubbleFrame hook files as an outgoing
            // entry. So we deliberately do not add it to the store here.
            Speech.Say(sent ? "Sent to " + peer + "." : "Could not send to " + peer + ".");
        }

        /// <summary>
        /// <paramref name="terminators"/> are the keys that closed the box. They
        /// are still physically down at this point, and HSA acts on key-up, so
        /// they have to stay suppressed or HSA will act on them the moment we
        /// hand control back.
        /// </summary>
        private static void End(params KeyCode[] terminators)
        {
            Active = false;
            _peerName = null;
            _peerPlayer = null;
            _beganFrame = -1;
            SetHsaTextInput(false);

            // Whatever we were holding open, let it go. Handing control back to
            // HSA in a bad state makes the game unplayable.
            AltLayer.ResetConsumed();

            if (terminators == null) return;
            foreach (var key in terminators) AltLayer.SuppressUntilReleased(key);
        }

        /// <summary>
        /// A guaranteed way out.
        ///
        /// Typing arrives through OnGUI, but if those events ever stopped
        /// reaching us the box could not be closed, and since composing
        /// suppresses all of HSA's input that would lock up the game. Escape is
        /// therefore also checked from Update, which runs unconditionally.
        /// </summary>
        internal static void UpdateFallback()
        {
            if (!Active) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Cancel(KeyCode.Escape);
        }
    }
}
