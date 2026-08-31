using System;
using System.Text;
using UnityEngine;

namespace HSMessage
{
    /// <summary>
    /// A one line text editor with a real caret and a real selection.
    ///
    /// It exists because there is nothing to borrow. Unity exposes no control
    /// to MSAA or UI Automation, so even a genuine Unity InputField would be as
    /// silent to a screen reader as a bare string is. Hearthstone Access hits
    /// the same wall and solves it the same way, by speaking deck code entry
    /// itself.
    ///
    /// So the caret is ours, and so is every announcement. The behaviour
    /// deliberately copies what NVDA does in a normal edit field: arrowing
    /// speaks the character you land on, the position one past the last
    /// character reads as "blank", and word movement speaks the whole word.
    /// Learning a second set of rules just to send a whisper would be no
    /// bargain.
    ///
    /// Every method returns the text to speak, or null for silence. Nothing
    /// here talks to Tolk directly, which keeps the editing rules testable and
    /// leaves the echo policy to the caller.
    /// </summary>
    internal sealed class LineEditor
    {
        private readonly StringBuilder _text = new StringBuilder();

        /// <summary>Where the caret sits, 0 through Length inclusive.</summary>
        private int _caret;

        /// <summary>
        /// The far end of the selection. Equal to the caret when nothing is
        /// selected, which is how selection is tested everywhere below.
        /// </summary>
        private int _anchor;

        internal int Length { get { return _text.Length; } }
        internal bool HasSelection { get { return _anchor != _caret; } }

        private int SelStart { get { return Math.Min(_anchor, _caret); } }
        private int SelEnd { get { return Math.Max(_anchor, _caret); } }

        internal string Text { get { return _text.ToString(); } }

        internal void Clear()
        {
            _text.Length = 0;
            _caret = 0;
            _anchor = 0;
        }

        // ---------------------------------------------------------- movement

        internal string MoveLeft(bool select)
        {
            if (!select && HasSelection)
            {
                // Collapse to the near edge, the way every edit box does.
                _caret = SelStart;
                _anchor = _caret;
                return CharAt(_caret);
            }

            if (_caret == 0) return null;

            _caret--;
            if (!select) _anchor = _caret;
            return CharAt(_caret);
        }

        internal string MoveRight(bool select)
        {
            if (!select && HasSelection)
            {
                _caret = SelEnd;
                _anchor = _caret;
                return CharAt(_caret);
            }

            if (_caret >= _text.Length) return null;

            // Selecting rightwards speaks the character being taken into the
            // selection. Moving plainly speaks the one the caret lands on,
            // which may be the blank past the end.
            char crossed = _text[_caret];
            _caret++;
            if (!select) _anchor = _caret;
            return select ? Speakable.Character(crossed) : CharAt(_caret);
        }

        internal string MoveWordLeft(bool select)
        {
            if (_caret == 0 && !HasSelection) return null;

            _caret = WordStartBefore(_caret);
            if (!select) _anchor = _caret;
            return WordAt(_caret);
        }

        internal string MoveWordRight(bool select)
        {
            if (_caret >= _text.Length && !HasSelection) return null;

            _caret = WordStartAfter(_caret);
            if (!select) _anchor = _caret;
            return WordAt(_caret);
        }

        internal string MoveHome(bool select)
        {
            _caret = 0;
            if (!select) _anchor = 0;
            return select ? Describe(Selected()) : CharAt(0);
        }

        internal string MoveEnd(bool select)
        {
            _caret = _text.Length;
            if (!select) _anchor = _caret;
            return select ? Describe(Selected()) : Speakable.Blank;
        }

        internal string SelectAll()
        {
            if (_text.Length == 0) return Speakable.Blank;

            _anchor = 0;
            _caret = _text.Length;
            return "Selected " + Describe(Selected());
        }

        // ----------------------------------------------------------- editing

        /// <summary>
        /// Insert a typed character. Silent on purpose: whether typing is
        /// echoed by character, by word or not at all is a config question, so
        /// the caller decides. Everything else in this class speaks, because
        /// everything else is either navigation or a bulk change you would not
        /// want to miss.
        /// </summary>
        internal void Insert(char c)
        {
            DeleteSelection();
            _text.Insert(_caret, c);
            _caret++;
            _anchor = _caret;
        }

        internal string Backspace()
        {
            if (HasSelection)
            {
                string removed = Selected();
                DeleteSelection();
                return "Deleted " + Describe(removed);
            }

            if (_caret == 0) return Speakable.Blank;

            _caret--;
            char removedChar = _text[_caret];
            _text.Remove(_caret, 1);
            _anchor = _caret;
            return Speakable.Character(removedChar);
        }

        internal string Delete()
        {
            if (HasSelection)
            {
                string removed = Selected();
                DeleteSelection();
                return "Deleted " + Describe(removed);
            }

            if (_caret >= _text.Length) return Speakable.Blank;

            char removedChar = _text[_caret];
            _text.Remove(_caret, 1);
            return Speakable.Character(removedChar);
        }

        internal string DeleteWordLeft()
        {
            if (HasSelection) return Backspace();
            if (_caret == 0) return Speakable.Blank;

            int start = WordStartBefore(_caret);
            string removed = _text.ToString(start, _caret - start);
            _text.Remove(start, _caret - start);
            _caret = start;
            _anchor = start;
            return "Deleted " + Describe(removed);
        }

        // --------------------------------------------------------- clipboard

        /// <summary>
        /// Copies the selection, or the whole line when nothing is selected,
        /// which is nearly always what you meant in a box this size.
        /// </summary>
        internal string Copy()
        {
            string text = HasSelection ? Selected() : _text.ToString();
            if (text.Length == 0) return "Nothing to copy.";

            try
            {
                GUIUtility.systemCopyBuffer = text;
                return "Copied.";
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clipboard copy failed: " + e.Message);
                return "Could not copy.";
            }
        }

        /// <summary>
        /// Cut needs a selection. Unlike copy it will not quietly take the
        /// whole line, because getting that wrong destroys what you typed.
        /// </summary>
        internal string Cut()
        {
            if (!HasSelection) return "Nothing selected.";

            string text = Selected();
            try
            {
                GUIUtility.systemCopyBuffer = text;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clipboard copy failed: " + e.Message);
                return "Could not cut.";
            }

            DeleteSelection();
            return "Cut " + Describe(text);
        }

        internal string Paste()
        {
            string clip;
            try
            {
                clip = GUIUtility.systemCopyBuffer;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clipboard read failed: " + e.Message);
                return "Could not read the clipboard.";
            }

            clip = Flatten(clip);
            if (clip.Length == 0) return "Clipboard is empty.";

            DeleteSelection();
            _text.Insert(_caret, clip);
            _caret += clip.Length;
            _anchor = _caret;

            // Always spoken, whatever the echo settings say. A paste is a bulk
            // change and you want to know what landed, especially for a link.
            return "Pasted " + Describe(clip);
        }

        /// <summary>
        /// A whisper is a single line, and a copied link routinely carries a
        /// trailing newline. Line breaks and tabs become spaces rather than
        /// being dropped, so words never run together.
        /// </summary>
        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsControl(c) ? ' ' : c);

            return sb.ToString().Trim();
        }

        // -------------------------------------------------------- read backs

        internal string ReadBack()
        {
            return _text.Length == 0 ? Speakable.Blank : _text.ToString();
        }

        /// <summary>
        /// The word the caret has just finished, for word by word echo. Read
        /// off the caret rather than the end of the line, so it stays correct
        /// when you go back and edit something in the middle.
        /// </summary>
        internal string WordBeforeCaret()
        {
            // The character just typed is the one that ended the word, so it
            // has to be a separator for there to be anything to say.
            int end = _caret - 1;
            if (end <= 0 || Speakable.IsWordCharacter(_text[end])) return null;

            int start = end;
            while (start > 0 && Speakable.IsWordCharacter(_text[start - 1])) start--;

            if (end - start <= 0) return null;
            return _text.ToString(start, end - start);
        }

        /// <summary>Where the caret is, for when you have lost your place.</summary>
        internal string DescribePosition()
        {
            if (_text.Length == 0) return Speakable.Blank;

            if (HasSelection)
                return Describe(Selected()) + " selected, of " + _text.Length + " characters.";

            if (_caret >= _text.Length)
                return "End, after " + _text.Length + " characters.";

            return "Character " + (_caret + 1) + " of " + _text.Length + ", " +
                   Speakable.Character(_text[_caret]) + ".";
        }

        // ----------------------------------------------------------- helpers

        private void DeleteSelection()
        {
            if (!HasSelection) return;

            int start = SelStart;
            _text.Remove(start, SelEnd - start);
            _caret = start;
            _anchor = start;
        }

        private string Selected()
        {
            return HasSelection ? _text.ToString(SelStart, SelEnd - SelStart) : string.Empty;
        }

        /// <summary>The character at an index, or "blank" one past the end.</summary>
        private string CharAt(int index)
        {
            if (index < 0 || index >= _text.Length) return Speakable.Blank;
            return Speakable.Character(_text[index]);
        }

        private string WordAt(int index)
        {
            if (index >= _text.Length) return Speakable.Blank;
            if (_text[index] == ' ') return Speakable.Character(' ');

            int end = index;
            while (end < _text.Length && _text[end] != ' ') end++;
            return _text.ToString(index, end - index);
        }

        /// <summary>
        /// Back over any spaces, then back over the word itself, landing on the
        /// first character of it. Ctrl+Left in any edit box.
        /// </summary>
        private int WordStartBefore(int from)
        {
            int i = from;
            while (i > 0 && _text[i - 1] == ' ') i--;
            while (i > 0 && _text[i - 1] != ' ') i--;
            return i;
        }

        /// <summary>Forward past this word and the spaces after it.</summary>
        private int WordStartAfter(int from)
        {
            int i = from;
            while (i < _text.Length && _text[i] != ' ') i++;
            while (i < _text.Length && _text[i] == ' ') i++;
            return i;
        }

        /// <summary>
        /// A run of text as it should be announced: a single character gets its
        /// punctuation name, a short run is read out, a long one is counted
        /// rather than recited.
        /// </summary>
        private static string Describe(string s)
        {
            if (string.IsNullOrEmpty(s)) return Speakable.Blank;
            if (s.Length == 1) return Speakable.Character(s[0]);
            if (s.Length <= 80) return s;
            return s.Length + " characters";
        }
    }
}
