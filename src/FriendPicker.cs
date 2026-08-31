using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HSMessage
{
    /// <summary>
    /// A spoken friend picker, so a first message no longer needs the social
    /// menu. Alt+N reads the online friends out as a list: arrows move, a
    /// letter jumps to the next name starting with it, Enter opens the reply
    /// box addressed to that person, Escape backs out.
    ///
    /// Only online friends are offered. A whisper needs the other side
    /// connected, and a list padded with people who cannot answer would just
    /// be more names to arrow past.
    ///
    /// The BnetPlayer handed to the reply box is the same kind of object the
    /// whisper hook captures, so once a message is sent this way the person
    /// lands in the buffer like anyone else and every other command works on
    /// them from then on.
    ///
    /// Everything is resolved by name at runtime, like every other touchpoint
    /// with the game's assemblies. BnetFriendMgr is decent ground to build
    /// on: Hearthstone Access itself patches helper methods into it.
    /// </summary>
    internal static class FriendPicker
    {
        private sealed class Entry
        {
            internal string Name;
            internal object Player;

            /// <summary>", away", ", busy", or empty. Decided when the list
            /// is built; presence will not change mid-pick.</summary>
            internal string Status;
        }

        private static readonly List<Entry> Friends = new List<Entry>();
        private static int _cursor;
        private static int _beganFrame = -1;

        internal static bool Active { get; private set; }

        private static bool _resolved;
        private static MethodInfo _mgrGet;
        private static MethodInfo _getFriends;
        private static MethodInfo _getBestName;
        private static MethodInfo _isOnline;
        private static MethodInfo _isAway;
        private static MethodInfo _isBusy;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var mgr = AccessTools.TypeByName("BnetFriendMgr");
            if (mgr != null)
            {
                _mgrGet = AccessTools.Method(mgr, "Get");
                _getFriends = AccessTools.Method(mgr, "GetFriends");
            }

            var player = AccessTools.TypeByName("BnetPlayer");
            if (player != null)
            {
                _getBestName = AccessTools.Method(player, "GetBestName");
                _isOnline = AccessTools.Method(player, "IsOnline");

                // Optional: without these we lose the status suffix, nothing
                // more.
                _isAway = AccessTools.Method(player, "IsAway");
                _isBusy = AccessTools.Method(player, "IsBusy");
            }

            if (_mgrGet == null || _getFriends == null || _getBestName == null || _isOnline == null)
                Plugin.Log.LogError(
                    "BnetFriendMgr or BnetPlayer surface not found. The friend picker is disabled.");
        }

        // ------------------------------------------------------------- begin

        internal static void Begin()
        {
            Resolve();

            if (_mgrGet == null || _getFriends == null || _getBestName == null || _isOnline == null)
            {
                Speech.Say("The friend list is not available.");
                return;
            }

            // Picking someone would go nowhere if the reply box cannot send.
            if (!Compose.CanReply)
            {
                Speech.Say("Replying is not available.");
                return;
            }

            Friends.Clear();
            try
            {
                var mgr = _mgrGet.Invoke(null, null);
                var list = mgr == null ? null : _getFriends.Invoke(mgr, null) as IEnumerable;
                if (list != null)
                {
                    foreach (var p in list)
                    {
                        if (p == null) continue;
                        if (!(bool)_isOnline.Invoke(p, null)) continue;

                        var name = _getBestName.Invoke(p, null) as string;
                        if (string.IsNullOrEmpty(name)) continue;

                        Friends.Add(new Entry { Name = name, Player = p, Status = StatusOf(p) });
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read the friend list: " + e);
                Friends.Clear();
            }

            if (Friends.Count == 0)
            {
                Speech.Say("No friends online.");
                return;
            }

            // Alphabetical, so first-letter jumps mean something. The game's
            // own order is whatever the server sent.
            Friends.Sort(CompareByName);

            _cursor = 0;
            _beganFrame = Time.frameCount;
            Active = true;

            // Same reason as the reply box: AltLayer.Tick stops running while
            // we are open, so the Alt+N that got us here would sit in its
            // consumed set forever and keep HSA suppressed after we close.
            AltLayer.ResetConsumed();

            Speech.SayInterruptible(
                "Choose a friend, " + Friends.Count + " online. " +
                "Arrows move, a letter jumps to a name, Enter to message, " +
                "Escape to cancel. " + Describe());
        }

        private static int CompareByName(Entry a, Entry b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string StatusOf(object player)
        {
            try
            {
                if (_isBusy != null && (bool)_isBusy.Invoke(player, null)) return ", busy";
                if (_isAway != null && (bool)_isAway.Invoke(player, null)) return ", away";
            }
            catch (Exception)
            {
                // Status is decoration; the name still works without it.
            }

            return "";
        }

        // -------------------------------------------------------------- tick

        /// <summary>
        /// Called from Runtime.Update while the picker is open. Plain
        /// Input.GetKeyDown is enough here: unlike the reply box there is no
        /// free text, so nothing depends on layout or shift state.
        /// </summary>
        internal static void Tick()
        {
            if (!Active) return;

            // The keypress that opened the picker is still in flight this
            // frame; without this, some layouts would treat it as a letter.
            if (Time.frameCount == _beganFrame) return;

            // Alt is left alone. It belongs to the layer that opened us, and
            // reading letters while Alt is held would turn Alt+Tab into a
            // jump to the T names.
            if (AltLayer.AltHeld) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close(KeyCode.Escape);
                Speech.Say("Cancelled.");
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Choose();
                return;
            }

            // Up and left both go back, down and right both go forward, so it
            // works whichever way you picture the list.
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow)) { Move(-1); return; }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow)) { Move(1); return; }

            if (Input.GetKeyDown(KeyCode.Home)) { _cursor = 0; Speech.Say(Describe()); return; }
            if (Input.GetKeyDown(KeyCode.End)) { _cursor = Friends.Count - 1; Speech.Say(Describe()); return; }

            for (var key = KeyCode.A; key <= KeyCode.Z; key++)
            {
                if (!Input.GetKeyDown(key)) continue;
                JumpToLetter((char)('a' + (key - KeyCode.A)));
                return;
            }
        }

        private static void Move(int delta)
        {
            _cursor += delta;
            if (_cursor < 0) _cursor = Friends.Count - 1;
            if (_cursor >= Friends.Count) _cursor = 0;
            Speech.Say(Describe());
        }

        /// <summary>
        /// The next name starting with this letter, searching forward from the
        /// cursor and wrapping, so pressing the letter again walks through
        /// everyone who shares it. Standard list box behaviour.
        /// </summary>
        private static void JumpToLetter(char letter)
        {
            for (int step = 1; step <= Friends.Count; step++)
            {
                int i = (_cursor + step) % Friends.Count;
                if (char.ToLowerInvariant(Friends[i].Name[0]) == letter)
                {
                    _cursor = i;
                    Speech.Say(Describe());
                    return;
                }
            }

            Speech.Say("Nobody starting with " + char.ToUpperInvariant(letter) + ".");
        }

        private static string Describe()
        {
            var f = Friends[_cursor];
            return f.Name + f.Status + ", " + (_cursor + 1) + " of " + Friends.Count + ".";
        }

        // ------------------------------------------------------------ finish

        private static void Choose()
        {
            var f = Friends[_cursor];
            Close(KeyCode.Return, KeyCode.KeypadEnter);
            Compose.Begin(f.Name, f.Player);
        }

        private static void Close(params KeyCode[] terminators)
        {
            Active = false;
            _beganFrame = -1;

            // Drop the BnetPlayer references rather than caching them. The
            // list is rebuilt on every open anyway, so people who log off are
            // never offered stale.
            Friends.Clear();

            // The key that closed us is still physically down, and HSA acts
            // on key-up. Same dance as the reply box.
            foreach (var key in terminators) AltLayer.SuppressUntilReleased(key);
        }
    }
}
