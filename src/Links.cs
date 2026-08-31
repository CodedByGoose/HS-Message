using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HSMessage
{
    /// <summary>
    /// Opens web links found in a whisper.
    ///
    /// Only http and https are ever opened. The scheme is checked rather than
    /// assumed, because the text comes from another player and handing an
    /// arbitrary string to the shell would be a poor idea.
    /// </summary>
    internal static class Links
    {
        private static readonly Regex Pattern =
            new Regex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

        // Trailing punctuation is almost always sentence punctuation rather than
        // part of the address. Closing brackets are only trimmed when unmatched.
        private const string TrailingJunk = ".,;:!?’\"'";

        private static string _lastMessage;
        private static int _next;

        internal static void OpenFromCurrentMessage()
        {
            var text = ChatStore.CurrentMessageText();
            if (string.IsNullOrEmpty(text))
            {
                Speech.Say(Strings.NoMessages);
                return;
            }

            var links = Extract(text);
            if (links.Count == 0)
            {
                Speech.Say("No link in this message.");
                return;
            }

            // Repeated presses walk through the links in one message. Landing on
            // a different message starts again from the first.
            if (!string.Equals(text, _lastMessage, StringComparison.Ordinal))
            {
                _lastMessage = text;
                _next = 0;
            }

            if (_next < 0 || _next >= links.Count) _next = 0;

            var url = links[_next];
            var position = links.Count > 1
                ? string.Format(", {0} of {1}", _next + 1, links.Count)
                : "";
            _next++;

            // The host is spoken rather than the whole address. It is the part
            // that tells you where you are about to go, and full URLs are
            // miserable to listen to.
            Speech.Say("Opening " + Describe(url) + position + ".");

            try
            {
                Application.OpenURL(url);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not open " + url + ": " + e);
                Speech.Say("Could not open the link.");
            }
        }

        internal static List<string> Extract(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;

            foreach (Match m in Pattern.Matches(text))
            {
                var url = Trim(m.Value);
                if (url.Length == 0) continue;
                if (!found.Contains(url)) found.Add(url);
            }

            return found;
        }

        private static string Trim(string url)
        {
            while (url.Length > 0)
            {
                char last = url[url.Length - 1];

                if (TrailingJunk.IndexOf(last) >= 0)
                {
                    url = url.Substring(0, url.Length - 1);
                    continue;
                }

                // "(see https://example.com/a)" should not keep the bracket, but
                // "https://example.com/a_(b)" should.
                if ((last == ')' && Count(url, '(') < Count(url, ')')) ||
                    (last == ']' && Count(url, '[') < Count(url, ']')))
                {
                    url = url.Substring(0, url.Length - 1);
                    continue;
                }

                break;
            }

            return url;
        }

        private static int Count(string s, char c)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) if (s[i] == c) n++;
            return n;
        }

        private static string Describe(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch (Exception)
            {
                return "link";
            }
        }
    }
}
