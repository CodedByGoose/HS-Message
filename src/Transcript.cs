using System.Collections.Generic;

namespace HSMessage
{
    /// <summary>
    /// A rolling record of everything Hearthstone Access has spoken, captured at
    /// ScreenReader.Output.
    ///
    /// The single most important property here: adding a new line does NOT move
    /// the review cursor. That reset-to-newest behaviour is exactly what makes
    /// NVDA's speech history unusable during a Battlegrounds fight, so we
    /// deliberately do not reproduce it. You can browse backwards at your own
    /// pace while the game carries on talking over the top of you.
    ///
    /// Our own review speech goes straight to Tolk rather than through
    /// ScreenReader.Output, so it never pollutes this buffer.
    /// </summary>
    internal static class Transcript
    {
        private static readonly List<string> Lines = new List<string>();

        /// <summary>-1 means "not browsing yet"; the first move starts at the newest line.</summary>
        private static int _cursor = -1;

        internal static void Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            Lines.Add(text);

            int cap = Plugin.TranscriptSize.Value;
            if (cap < 1) cap = 1;

            while (Lines.Count > cap)
            {
                Lines.RemoveAt(0);
                // Keep the cursor pointing at the same line it was on.
                if (_cursor >= 0) _cursor--;
            }

            if (_cursor < -1) _cursor = -1;
        }

        internal static string Move(int delta)
        {
            if (!Plugin.CaptureAllSpeech.Value)
                return "Speech capture is off. Turn it on in the plugin config.";

            if (Lines.Count == 0) return "Nothing captured yet.";

            // First press lands on the most recent line, whichever direction.
            if (_cursor < 0)
            {
                _cursor = Lines.Count - 1;
                return Format();
            }

            int target = _cursor + delta;
            if (target < 0) return "Start of transcript.";
            if (target >= Lines.Count) return "End of transcript.";

            _cursor = target;
            return Format();
        }

        internal static string Repeat()
        {
            if (!Plugin.CaptureAllSpeech.Value)
                return "Speech capture is off. Turn it on in the plugin config.";

            if (Lines.Count == 0) return "Nothing captured yet.";
            if (_cursor < 0) _cursor = Lines.Count - 1;

            return Format();
        }

        private static string Format()
        {
            return string.Format("{0}. {1} of {2}.", Lines[_cursor], _cursor + 1, Lines.Count);
        }
    }
}
