using System;
using System.Runtime.InteropServices;

namespace HSMessage
{
    /// <summary>
    /// Asks Windows directly whether Alt is physically down.
    ///
    /// Unity learns about keys from messages delivered to its own window, so if
    /// the focus leaves that window while a key is held, the key-up lands
    /// somewhere else and Unity never hears about it. Input.GetKey then reports
    /// that key as held for the rest of the session, and it does not correct
    /// itself.
    ///
    /// This was written for a real fault: an experimental Win32 reply box took
    /// the keyboard focus, ate the release of the Alt held to open it, and left
    /// the whole Alt layer live afterwards, so the arrow keys kept moving
    /// through conversations instead of playing the game. That box is gone.
    ///
    /// It is kept because the hazard is not. Alt+Tab holds Alt across exactly
    /// the same kind of focus change, and stranding the Alt layer is the single
    /// nastiest failure this plugin has, having made the game unplayable three
    /// times already. Unity may well clear its key state on focus loss and make
    /// this redundant; that has not been proven either way, and the cost of
    /// being wrong is far higher than the cost of the check.
    /// </summary>
    internal static class NativeKeys
    {
        private const int VK_MENU = 0x12;   // Either Alt key.

        /// <summary>
        /// True while Alt is genuinely held, according to Windows rather than
        /// Unity. On any failure this returns true, so the caller falls back to
        /// trusting Unity rather than having the Alt layer silently stop
        /// working.
        /// </summary>
        internal static bool AltDown()
        {
            try
            {
                // The high bit is the current physical state. The low bit is a
                // "pressed since last call" flag we deliberately ignore.
                return (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
