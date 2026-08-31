using System;
using System.Reflection;
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
    /// real edit control anywhere for a screen reader to find.
    ///
    /// This drives <see cref="LineEditor"/>: our own caret, our own selection,
    /// our own announcements through Tolk. It depends on nothing but Unity
    /// delivering key events, which is why it always works.
    ///
    /// A genuine Win32 edit control was tried instead, and removed. It did work
    /// -- the screen reader read it natively, as hoped -- but every reply
    /// manufactured a new window for NVDA to discover, which cost about a
    /// second before anything was announced, and that cost is not ours to fix.
    /// Taking the keyboard focus off a game engine that does not expect it also
    /// produced two separate ways to strand the player. The line editor below
    /// already covers what people actually asked for.
    ///
    /// While composing we call HSA's own AllowTextInput(), which is the
    /// sanctioned way to tell it to keep its hands off the keyboard. It is the
    /// same mechanism HSA uses for deck code entry.
    /// </summary>
    internal static class Compose
    {
        private static readonly LineEditor Editor = new LineEditor();

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

            Editor.Clear();
            _peerName = peer;
            _peerPlayer = player;
            _beganFrame = Time.frameCount;
            Active = true;

            DecideEcho();

            // AltLayer.Tick stops running while we are composing, so the Alt+M
            // that got us here would otherwise sit in its consumed set forever
            // and keep HSA suppressed after we finish.
            AltLayer.ResetConsumed();

            SetHsaTextInput(true);

            Speech.SayInterruptible(
                "Message to " + peer + ". Type, then Enter to send. " +
                "Arrows move, F2 reads it back, Escape cancels.");
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

            // Not e.command as well: on Windows that is the Windows key, and
            // Win+V already means something to the operating system.
            bool ctrl = e.control && !e.alt;
            bool shift = e.shift;

            // Everything a normal edit box does with Control held. Paste is the
            // one people miss most: a link copied from a browser was previously
            // impossible to get in here.
            if (ctrl)
            {
                switch (e.keyCode)
                {
                    case KeyCode.V: Speech.Say(Editor.Paste()); e.Use(); return;
                    case KeyCode.C: Speech.Say(Editor.Copy()); e.Use(); return;
                    case KeyCode.X: Speech.Say(Editor.Cut()); e.Use(); return;
                    // Interruptible, because selecting everything now reads
                    // everything, and a long message should be stoppable.
                    case KeyCode.A: Speech.SayInterruptible(Editor.SelectAll()); e.Use(); return;

                    case KeyCode.LeftArrow: Speech.Say(Editor.MoveWordLeft(shift)); e.Use(); return;
                    case KeyCode.RightArrow: Speech.Say(Editor.MoveWordRight(shift)); e.Use(); return;

                    case KeyCode.Backspace: Speech.Say(Editor.DeleteWordLeft()); e.Use(); return;
                }

                // Anything else with Control falls through: Control plus Home
                // and End mean the same as plain Home and End on one line.
            }

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Cancel(KeyCode.Escape); e.Use(); return;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Both are passed on because HSA treats them as one key:
                    // its CONFIRM binding accepts a keypad Enter release while
                    // matching on Return.
                    Send(Editor.Text, KeyCode.Return, KeyCode.KeypadEnter); e.Use(); return;

                case KeyCode.F2:
                    // Shift for where the caret is, plain for the whole line.
                    Speech.SayInterruptible(shift ? Editor.DescribePosition() : Editor.ReadBack());
                    e.Use(); return;

                case KeyCode.LeftArrow: Speech.Say(Editor.MoveLeft(shift)); e.Use(); return;
                case KeyCode.RightArrow: Speech.Say(Editor.MoveRight(shift)); e.Use(); return;
                case KeyCode.Home: Speech.Say(Editor.MoveHome(shift)); e.Use(); return;
                case KeyCode.End: Speech.Say(Editor.MoveEnd(shift)); e.Use(); return;

                case KeyCode.Backspace: Speech.Say(Editor.Backspace()); e.Use(); return;
                case KeyCode.Delete: Speech.Say(Editor.Delete()); e.Use(); return;

                // Up and down have nothing to move to on a single line, so they
                // read the whole message instead. That is not a liberty: a
                // screen reader reads the line you arrow onto, and here the
                // line is the message. It saves reaching for F2.
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    Speech.SayInterruptible(Editor.ReadBack()); e.Use(); return;

                // Nothing sensible to do with these on one line. Swallowed
                // rather than passed on, so they cannot reach the game behind
                // us.
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                case KeyCode.Tab:
                    e.Use(); return;
            }

            // Unity delivers the key code and the character as separate events,
            // so a printable character arrives here with keyCode None.
            char c = e.character;
            if (c == '\0' || char.IsControl(c)) return;

            bool replacedSelection = Editor.HasSelection;
            Editor.Insert(c);
            e.Use();

            // Typing over a selection is a bigger change than one character, so
            // it is always confirmed, whatever the echo settings say.
            if (replacedSelection)
            {
                Speech.Say(Speakable.Character(c));
            }
            else if (_echoCharacters)
            {
                Speech.Say(Speakable.Character(c));
            }
            else if (_echoWords)
            {
                var word = Editor.WordBeforeCaret();
                if (!string.IsNullOrEmpty(word)) Speech.Say(word);
            }
        }

        // -------------------------------------------------------------- echo

        /// <summary>
        /// Whether we speak characters and words as they are typed. Settled
        /// once when the box opens rather than asked per keystroke, so a long
        /// message cannot change its mind halfway through.
        /// </summary>
        private static bool _echoCharacters;
        private static bool _echoWords;

        private static void DecideEcho()
        {
            if (!Plugin.FollowScreenReaderEcho.Value)
            {
                _echoCharacters = Plugin.EchoTypedCharacters.Value;
                _echoWords = Plugin.EchoTypedWords.Value;
                return;
            }

            NvdaSettings.Refresh();

            _echoCharacters = Follow(NvdaSettings.SpeakTypedCharacters, Plugin.EchoTypedCharacters.Value);
            _echoWords = Follow(NvdaSettings.SpeakTypedWords, Plugin.EchoTypedWords.Value);
        }

        /// <summary>
        /// Turn one of NVDA's echo settings into a yes or no for us.
        ///
        /// The middle case is the ordinary one and the reason this is worth
        /// doing at all. "Only in edit controls" means NVDA looks at what has
        /// the focus, sees a Unity window rather than an edit field, and says
        /// nothing. It cannot see our box, so we are the only thing that can
        /// speak, and we do.
        ///
        /// "Always" is the case that has to be inverted, and it is easy to get
        /// backwards. NVDA speaks typed characters in any window in that mode,
        /// this one included, so it is already doing the job. Echoing as well
        /// would say every character twice.
        ///
        /// None of this applies when the native control is up: that really is
        /// an edit field, so NVDA handles both modes itself and correctly, and
        /// this code is not reached.
        /// </summary>
        private static bool Follow(TypingEcho? nvda, bool fallback)
        {
            if (nvda == null) return fallback;

            switch (nvda.Value)
            {
                case TypingEcho.Off: return false;
                case TypingEcho.EditControls: return true;
                case TypingEcho.Always: return false;
                default: return fallback;
            }
        }

        // ------------------------------------------------------------ finish

        private static void Cancel(params KeyCode[] terminators)
        {
            Editor.Clear();
            End(terminators);
            Speech.Say("Cancelled.");
        }

        private static void Send(string raw, params KeyCode[] terminators)
        {
            var text = (raw ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                Editor.Clear();
                End(terminators);
                Speech.Say("Nothing to send.");
                return;
            }

            var peer = _peerName;
            var player = _peerPlayer;

            Editor.Clear();
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

            // Nothing is said on success. The game raises its own chat bubble
            // for the outgoing message, which Hearthstone Access reads out and
            // accompanies with a tone, so a confirmation on top of that is
            // just something else to sit through. That bubble is also what our
            // ChatBubbleFrame hook files as the outgoing entry, which is why
            // nothing is added to the store here either.
            //
            // Failure still speaks. Nothing is read out and no tone plays when
            // a send fails, so silence would be indistinguishable from success.
            if (!sent) Speech.Say("Could not send to " + peer + ".");
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

            // Reading the message back inside the box arms "stop on the next
            // keypress", and that speech is over now. Left armed, the first key
            // pressed afterwards would silence the screen reader part way
            // through Hearthstone Access announcing the message we just sent.
            Speech.CancelOnNextKey = false;

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
