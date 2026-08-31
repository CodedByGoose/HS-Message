using System;
using System.Collections.Generic;
using System.Text;

namespace HSMessage
{
    internal sealed class ChatMessage
    {
        internal DateTime At;
        internal string Text;
        internal bool Outgoing;
        internal bool Read;
    }

    internal sealed class Conversation
    {
        internal string Peer;

        /// <summary>
        /// The game's BnetPlayer for this person, held as object so we never
        /// need a compile-time reference to Assembly-CSharp. Needed to reply.
        /// Learned from the whisper itself, in either direction, which is why
        /// you can only reply to people already in the buffer. One message sent
        /// through the game's own social menu is enough to put someone there,
        /// because the outgoing bubble goes through the same hook.
        /// </summary>
        internal object PeerPlayer;

        internal readonly List<ChatMessage> Messages = new List<ChatMessage>();

        /// <summary>Which message the review cursor is sitting on.</summary>
        internal int Cursor;

        internal int Unread
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Messages.Count; i++)
                    if (!Messages[i].Read && !Messages[i].Outgoing) n++;
                return n;
            }
        }

        internal ChatMessage Current
        {
            get
            {
                if (Cursor < 0 || Cursor >= Messages.Count) return null;
                return Messages[Cursor];
            }
        }

        internal DateTime LastActivity
        {
            get { return Messages.Count == 0 ? DateTime.MinValue : Messages[Messages.Count - 1].At; }
        }

        internal void MarkAllRead()
        {
            for (int i = 0; i < Messages.Count; i++) Messages[i].Read = true;
        }
    }

    /// <summary>
    /// Every whisper this client session has seen, grouped by the person on the
    /// other end. Conversations keep their slot in first-contact order so that
    /// Alt+3 means the same person for as long as the client is running.
    /// </summary>
    internal static class ChatStore
    {
        private static readonly List<Conversation> Conversations = new List<Conversation>();
        private static int _current = -1;

        internal static int Count { get { return Conversations.Count; } }

        // ----------------------------------------------------------------- add

        internal static void Add(string peer, string text, bool outgoing, object peerPlayer)
        {
            if (string.IsNullOrEmpty(peer) || string.IsNullOrEmpty(text)) return;

            var convo = Find(peer);
            if (convo == null)
            {
                convo = new Conversation { Peer = peer };
                Conversations.Add(convo);
                if (_current < 0) _current = 0;
            }

            // Refresh each time; the player object can be replaced as the
            // session goes on.
            if (peerPlayer != null) convo.PeerPlayer = peerPlayer;

            var msg = new ChatMessage
            {
                At = DateTime.Now,
                Text = text,
                Outgoing = outgoing,
                // Our own sent messages are never "unread".
                Read = outgoing
            };

            convo.Messages.Add(msg);

            int cap = Plugin.MaxMessagesPerConversation.Value;
            if (cap > 0)
            {
                while (convo.Messages.Count > cap)
                {
                    convo.Messages.RemoveAt(0);
                    if (convo.Cursor > 0) convo.Cursor--;
                }
            }

            // A new message moves the cursor to it, so Alt+Up walks backwards
            // from the newest thing rather than from wherever you last were.
            convo.Cursor = convo.Messages.Count - 1;
        }

        private static Conversation Find(string peer)
        {
            for (int i = 0; i < Conversations.Count; i++)
                if (string.Equals(Conversations[i].Peer, peer, StringComparison.OrdinalIgnoreCase))
                    return Conversations[i];
            return null;
        }

        private static Conversation CurrentConvo
        {
            get
            {
                if (_current < 0 || _current >= Conversations.Count) return null;
                return Conversations[_current];
            }
        }

        // -------------------------------------------------------- conversations

        internal static string SelectConversation(int index)
        {
            if (Conversations.Count == 0) return Strings.NoMessages;
            if (index < 0 || index >= Conversations.Count)
                return string.Format("No conversation {0}.", index + 1);

            _current = index;
            return DescribeCurrentConversation();
        }

        internal static string CycleConversation(int delta)
        {
            if (Conversations.Count == 0) return Strings.NoMessages;

            _current += delta;
            if (_current < 0) _current = Conversations.Count - 1;
            if (_current >= Conversations.Count) _current = 0;

            return DescribeCurrentConversation();
        }

        /// <summary>
        /// Announces who you are on, how much is unread, and one message.
        /// Reads the unread count before clearing it, so you hear it once.
        ///
        /// The message spoken is the newest UNREAD one when there is any,
        /// not the newest overall. They differ when your own reply is the
        /// most recent thing in the thread: reading that back while marking
        /// the real message read would lose it without it ever being heard.
        /// </summary>
        private static string DescribeCurrentConversation()
        {
            var c = CurrentConvo;
            if (c == null) return Strings.NoMessages;

            int unread = c.Unread;

            c.Cursor = c.Messages.Count - 1;
            if (unread > 0)
            {
                for (int i = c.Messages.Count - 1; i >= 0; i--)
                {
                    if (!c.Messages[i].Read && !c.Messages[i].Outgoing)
                    {
                        c.Cursor = i;
                        break;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append(c.Peer);
            if (unread > 0) sb.Append(", ").Append(unread).Append(" unread");
            sb.Append(". ");
            sb.Append(Describe(c, c.Current));

            c.MarkAllRead();
            return sb.ToString();
        }

        // ------------------------------------------------------------ messages

        /// <summary>
        /// Jump to the nth most recently RECEIVED message in the current
        /// conversation, counting from 1 as the newest.
        ///
        /// Messages you sent are skipped when counting, because there are only
        /// ten number keys and your own replies would burn through them fast.
        /// They are still in the list, so Alt+Up and Alt+Down walk over them
        /// normally once you have landed somewhere.
        /// </summary>
        internal static string SelectRecent(int n)
        {
            var c = CurrentConvo;
            if (c == null || c.Messages.Count == 0) return Strings.NoMessages;
            if (n < 1) return Strings.NoMessages;

            int received = 0;
            for (int i = c.Messages.Count - 1; i >= 0; i--)
            {
                if (c.Messages[i].Outgoing) continue;

                received++;
                if (received != n) continue;

                c.Cursor = i;
                c.Messages[i].Read = true;
                return Describe(c, c.Messages[i], false);
            }

            if (received == 0) return "Nothing received from " + c.Peer + ".";

            return string.Format("Only {0} message{1} received.",
                received, received == 1 ? "" : "s");
        }

        internal static string MoveMessage(int delta)
        {
            var c = CurrentConvo;
            if (c == null) return Strings.NoMessages;
            if (c.Messages.Count == 0) return Strings.NoMessages;

            int target = c.Cursor + delta;
            if (target < 0) return "Start of conversation.";
            if (target >= c.Messages.Count) return "End of conversation.";

            c.Cursor = target;
            var m = c.Current;
            if (m != null) m.Read = true;
            return Describe(c, m);
        }

        internal static string JumpToEdge(bool first)
        {
            var c = CurrentConvo;
            if (c == null || c.Messages.Count == 0) return Strings.NoMessages;

            c.Cursor = first ? 0 : c.Messages.Count - 1;
            var m = c.Current;
            if (m != null) m.Read = true;
            return Describe(c, m);
        }

        internal static string RepeatCurrent()
        {
            var c = CurrentConvo;
            if (c == null || c.Current == null) return Strings.NoMessages;
            return Describe(c, c.Current);
        }

        internal static string CurrentPeerName()
        {
            var c = CurrentConvo;
            return c == null ? null : c.Peer;
        }

        internal static object CurrentPeerPlayer()
        {
            var c = CurrentConvo;
            return c == null ? null : c.PeerPlayer;
        }

        internal static string CurrentMessageText()
        {
            var c = CurrentConvo;
            if (c == null || c.Current == null) return null;
            return c.Current.Text;
        }

        internal static string DescribeCurrentTimestamp()
        {
            var c = CurrentConvo;
            if (c == null || c.Current == null) return Strings.NoMessages;

            var age = DateTime.Now - c.Current.At;
            return string.Format("{0}, {1}.", c.Current.At.ToString("h:mm tt"), Relative(age));
        }

        private static string Relative(TimeSpan age)
        {
            if (age.TotalSeconds < 45) return "just now";
            if (age.TotalMinutes < 2) return "a minute ago";
            if (age.TotalMinutes < 60) return ((int)age.TotalMinutes) + " minutes ago";
            if (age.TotalHours < 2) return "an hour ago";
            return ((int)age.TotalHours) + " hours ago";
        }

        // ------------------------------------------------------------- reading

        internal static string ReadWholeConversation()
        {
            var c = CurrentConvo;
            if (c == null || c.Messages.Count == 0) return Strings.NoMessages;

            var sb = new StringBuilder();
            sb.Append("Conversation with ").Append(c.Peer).Append(", ")
              .Append(c.Messages.Count).Append(c.Messages.Count == 1 ? " message. " : " messages. ");

            for (int i = 0; i < c.Messages.Count; i++)
            {
                sb.Append(Speaker(c, c.Messages[i])).Append(": ")
                  .Append(c.Messages[i].Text).Append(". ");
            }

            c.MarkAllRead();
            return sb.ToString();
        }

        internal static string NewestUnread()
        {
            Conversation best = null;
            DateTime bestAt = DateTime.MinValue;

            for (int i = 0; i < Conversations.Count; i++)
            {
                var c = Conversations[i];
                for (int j = c.Messages.Count - 1; j >= 0; j--)
                {
                    var m = c.Messages[j];
                    if (m.Read || m.Outgoing) continue;
                    if (m.At > bestAt) { bestAt = m.At; best = c; }
                    break;
                }
            }

            if (best == null) return "No unread messages.";

            _current = Conversations.IndexOf(best);
            return DescribeCurrentConversation();
        }

        internal static string Summary()
        {
            if (Conversations.Count == 0) return Strings.NoMessages;

            var sb = new StringBuilder();
            sb.Append(Conversations.Count)
              .Append(Conversations.Count == 1 ? " conversation. " : " conversations. ");

            int totalUnread = 0;
            var withUnread = new List<Conversation>();
            for (int i = 0; i < Conversations.Count; i++)
            {
                int u = Conversations[i].Unread;
                if (u > 0) { totalUnread += u; withUnread.Add(Conversations[i]); }
            }

            if (totalUnread == 0)
            {
                sb.Append("Nothing unread.");
            }
            else
            {
                for (int i = 0; i < withUnread.Count && i < 5; i++)
                {
                    sb.Append(withUnread[i].Unread).Append(" from ")
                      .Append(withUnread[i].Peer).Append(". ");
                }
            }

            return sb.ToString();
        }

        /// <summary>Spoken list of slots, so Alt+1 through Alt+9 are discoverable.</summary>
        internal static string ListSlots()
        {
            if (Conversations.Count == 0) return Strings.NoMessages;

            var sb = new StringBuilder();
            for (int i = 0; i < Conversations.Count && i < 9; i++)
            {
                sb.Append(i + 1).Append(", ").Append(Conversations[i].Peer);
                int u = Conversations[i].Unread;
                if (u > 0) sb.Append(", ").Append(u).Append(" unread");
                sb.Append(". ");
            }
            return sb.ToString();
        }

        // ----------------------------------------------------------- formatting

        private static string Speaker(Conversation c, ChatMessage m)
        {
            return m.Outgoing ? "You" : c.Peer;
        }

        private static string Describe(Conversation c, ChatMessage m)
        {
            return Describe(c, m, true);
        }

        /// <summary>
        /// The position counter is useful while arrowing around, because it
        /// tells you where you are in the thread. It is just noise on the number
        /// keys, where you already know which message you asked for, and the
        /// two numbers disagree anyway: the counter is absolute and oldest
        /// first, while the number keys count back from the newest.
        /// </summary>
        private static string Describe(Conversation c, ChatMessage m, bool includePosition)
        {
            if (m == null) return Strings.NoMessages;

            if (!includePosition)
                return string.Format("{0}: {1}.", Speaker(c, m), m.Text);

            return string.Format("{0}: {1}. {2} of {3}.",
                Speaker(c, m), m.Text, c.Cursor + 1, c.Messages.Count);
        }
    }

    internal static class Strings
    {
        internal const string NoMessages = "No messages.";
    }
}
