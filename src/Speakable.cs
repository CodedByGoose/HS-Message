using System.Collections.Generic;
using System.Globalization;

namespace HSMessage
{
    /// <summary>
    /// Turns a single character into something worth hearing.
    ///
    /// This matters more than it looks. Text sent through Tolk reaches the
    /// screen reader as ordinary speech, so it obeys the punctuation level the
    /// user has set. At the usual settings a lone full stop is spoken as
    /// nothing at all, which makes arrowing along a pasted link useless: half
    /// of it is punctuation. Naming the characters ourselves is the only way to
    /// be sure they are heard.
    ///
    /// The names are NVDA's own en-US symbol names, so what you hear here
    /// matches what you hear everywhere else rather than being a private
    /// dialect.
    /// </summary>
    internal static class Speakable
    {
        /// <summary>Standard screen reader word for an empty position.</summary>
        internal const string Blank = "blank";

        private static readonly Dictionary<char, string> Names = new Dictionary<char, string>
        {
            { ' ', "space" },
            { '\t', "tab" },
            { '!', "bang" },
            { '"', "quote" },
            { '#', "number" },
            { '$', "dollar" },
            { '%', "percent" },
            { '&', "and" },
            { '\'', "tick" },
            { '(', "left paren" },
            { ')', "right paren" },
            { '*', "star" },
            { '+', "plus" },
            { ',', "comma" },
            { '-', "dash" },
            { '.', "dot" },
            { '/', "slash" },
            { ':', "colon" },
            { ';', "semi" },
            { '<', "less" },
            { '=', "equals" },
            { '>', "greater" },
            { '?', "question" },
            { '@', "at" },
            { '[', "left bracket" },
            { '\\', "backslash" },
            { ']', "right bracket" },
            { '^', "caret" },
            { '_', "line" },
            { '`', "graav" },
            { '{', "left brace" },
            { '|', "bar" },
            { '}', "right brace" },
            { '~', "tilde" },
        };

        /// <summary>
        /// Whether a character is part of a word for the purpose of word echo.
        ///
        /// NVDA counts the Unicode letter, mark and number categories, and
        /// treats anything else as ending the word. So a full stop or a slash
        /// finishes a word just as a space does, which is what makes typing a
        /// URL announce itself in pieces rather than in one lump at the end.
        /// </summary>
        internal static bool IsWordCharacter(char c)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                    return true;
                default:
                    return false;
            }
        }

        internal static string Character(char c)
        {
            string name;
            if (Names.TryGetValue(c, out name)) return name;

            // Speech has no pitch to raise, so capitals are said outright, the
            // way a screen reader announces them during character navigation.
            if (char.IsUpper(c)) return "cap " + c;

            return c.ToString();
        }
    }
}
