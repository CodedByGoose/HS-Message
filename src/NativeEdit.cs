using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HSMessage
{
    /// <summary>
    /// A real Windows edit control, parented to the Hearthstone window and
    /// given the keyboard focus while you compose.
    ///
    /// This is the one route to a box your screen reader genuinely owns. Unity
    /// exposes nothing to MSAA or UI Automation, so a Unity text field can only
    /// ever be narrated second hand by whoever wrote the mod. A plain Win32
    /// EDIT, by contrast, is a control NVDA already knows inside out: caret
    /// reporting, word and character navigation, selection, the review cursor,
    /// your own punctuation and keyboard echo settings, and clipboard keys that
    /// work because Windows implements them, not because we reimplemented them.
    ///
    /// It is a child window rather than a window of its own on purpose. A child
    /// takes the focus without deactivating the game, so Hearthstone is never
    /// backgrounded and a full screen client will not minimise underneath you.
    ///
    /// It is nonetheless a foreign control inside a game that does not expect
    /// one, which is why it is off by default and why every failure path here
    /// falls back to the plugin's own line editor rather than leaving you with
    /// no way to type. <see cref="Compose"/> also keeps its Escape handling
    /// alive on the Unity side throughout, so there is always a way out even if
    /// this window stops responding.
    /// </summary>
    internal static class NativeEdit
    {
        private const int GWLP_WNDPROC = -4;

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_BORDER = 0x00800000;
        private const int ES_AUTOHSCROLL = 0x0080;

        private const int WM_SETFONT = 0x0030;
        private const int WM_GETTEXT = 0x000D;
        private const int WM_GETTEXTLENGTH = 0x000E;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_CHAR = 0x0102;
        private const int EM_SETSEL = 0x00B1;
        private const int EM_SETLIMITTEXT = 0x00C5;

        private const int VK_TAB = 0x09;
        private const int VK_RETURN = 0x0D;
        private const int VK_ESCAPE = 0x1B;

        private const int PM_REMOVE = 0x0001;

        private const int DEFAULT_GUI_FONT = 17;

        /// <summary>Room for any whisper, with plenty to spare.</summary>
        private const int MaxLength = 1024;

        private static IntPtr _parent;
        private static IntPtr _label;
        private static IntPtr _edit;
        private static IntPtr _originalProc;

        /// <summary>
        /// Held for the life of the process. If this were collected while the
        /// control still pointed at it, Windows would call into freed memory
        /// and take the game down with it.
        /// </summary>
        private static WndProc _procKeepAlive;

        internal static bool Active { get { return _edit != IntPtr.Zero; } }

        /// <summary>Set by the window procedure, drained by <see cref="Compose"/>.</summary>
        internal static bool Submitted { get; private set; }
        internal static bool Cancelled { get; private set; }

        // -------------------------------------------------------------- open

        /// <summary>
        /// Returns false if anything at all goes wrong, at which point the
        /// caller should quietly use the built in editor instead. Nothing here
        /// is worth failing a reply over.
        /// </summary>
        internal static bool TryOpen(string labelText)
        {
            if (Active) Close();

            Submitted = false;
            Cancelled = false;

            try
            {
                // The active window of this thread is the game window. Unity
                // does not hand out its HWND, but it is the only top level
                // window we own.
                _parent = GetActiveWindow();
                if (_parent == IntPtr.Zero) _parent = GetForegroundWindow();
                if (_parent == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("Native reply box: could not find the game window.");
                    return false;
                }

                RECT client;
                if (!GetClientRect(_parent, out client)) return false;

                int width = Math.Max(240, Math.Min(700, client.Right - client.Left - 40));
                int height = 28;
                int x = 20;
                int y = Math.Max(0, client.Bottom - client.Top - height - 40);

                var module = GetModuleHandle(null);

                // A static label created first, so it precedes the edit in
                // z-order. That is how MSAA decides what an edit control is
                // called, and it is why the screen reader announces who you are
                // writing to rather than just "edit".
                _label = CreateWindowEx(
                    0, "STATIC", labelText,
                    WS_CHILD | WS_VISIBLE,
                    x, Math.Max(0, y - 22), width, 20,
                    _parent, IntPtr.Zero, module, IntPtr.Zero);

                _edit = CreateWindowEx(
                    0, "EDIT", string.Empty,
                    WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
                    x, y, width, height,
                    _parent, IntPtr.Zero, module, IntPtr.Zero);

                if (_edit == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning(
                        "Native reply box: CreateWindowEx failed, error " + Marshal.GetLastWin32Error() + ".");
                    Close();
                    return false;
                }

                var font = GetStockObject(DEFAULT_GUI_FONT);
                if (font != IntPtr.Zero)
                {
                    SendMessage(_edit, WM_SETFONT, font, new IntPtr(1));
                    if (_label != IntPtr.Zero) SendMessage(_label, WM_SETFONT, font, new IntPtr(1));
                }

                SendMessage(_edit, EM_SETLIMITTEXT, new IntPtr(MaxLength), IntPtr.Zero);

                // Enter and Escape have to be ours: a single line edit would
                // otherwise just beep at them, and there would be no way to
                // send or cancel.
                if (_procKeepAlive == null) _procKeepAlive = EditProc;
                _originalProc = SetWindowLongPtrSafe(
                    _edit, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_procKeepAlive));

                if (_originalProc == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("Native reply box: could not subclass the control.");
                    Close();
                    return false;
                }

                SetFocus(_edit);
                SendMessage(_edit, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);

                if (GetFocus() != _edit)
                {
                    Plugin.Log.LogWarning("Native reply box: the control would not take focus.");
                    Close();
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Native reply box could not be created: " + e.Message);
                Close();
                return false;
            }
        }

        // -------------------------------------------------------------- tick

        /// <summary>
        /// Called every frame while the box is open. Returns false once the
        /// window has gone away underneath us, which the caller treats as a
        /// cancel rather than leaving the keyboard captured.
        /// </summary>
        internal static bool Tick()
        {
            if (!Active) return false;

            if (!IsWindow(_edit))
            {
                Plugin.Log.LogWarning("Native reply box vanished; falling back to cancel.");
                return false;
            }

            // Unity runs its own message pump and we cannot know whether it
            // filters on its own window handle. Draining anything addressed to
            // our control covers the case where it does; where it does not,
            // Unity has already dispatched them and this finds nothing.
            MSG msg;
            int guard = 0;
            while (guard++ < 64 && PeekMessage(out msg, _edit, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // Alt-tabbing away and back can leave the focus on the game window
            // instead of the control, which would silently drop your typing.
            // Take it back, but only while the game is actually in front.
            if (GetActiveWindow() == _parent && GetFocus() != _edit)
                SetFocus(_edit);

            return true;
        }

        internal static string ReadText()
        {
            if (!Active) return string.Empty;

            try
            {
                int length = (int)SendMessage(_edit, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
                if (length <= 0) return string.Empty;

                var buffer = new StringBuilder(length + 1);
                SendMessageText(_edit, WM_GETTEXT, new IntPtr(length + 1), buffer);
                return buffer.ToString();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the native reply box: " + e.Message);
                return string.Empty;
            }
        }

        // ------------------------------------------------------------- close

        internal static void Close()
        {
            try
            {
                // Put the original procedure back before the window dies, so
                // teardown never re-enters managed code.
                if (_edit != IntPtr.Zero && _originalProc != IntPtr.Zero)
                    SetWindowLongPtrSafe(_edit, GWLP_WNDPROC, _originalProc);

                if (_edit != IntPtr.Zero) DestroyWindow(_edit);
                if (_label != IntPtr.Zero) DestroyWindow(_label);

                // Hand the keyboard back, or Unity gets nothing from here on.
                if (_parent != IntPtr.Zero) SetFocus(_parent);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Native reply box cleanup failed: " + e.Message);
            }
            finally
            {
                _edit = IntPtr.Zero;
                _label = IntPtr.Zero;
                _originalProc = IntPtr.Zero;
                _parent = IntPtr.Zero;
            }
        }

        // --------------------------------------------------------- procedure

        private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private static IntPtr EditProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                int key = wParam.ToInt32();

                if (msg == WM_KEYDOWN)
                {
                    if (key == VK_RETURN) { Submitted = true; return IntPtr.Zero; }
                    if (key == VK_ESCAPE) { Cancelled = true; return IntPtr.Zero; }

                    // Tab would move the focus off the control, and there is
                    // nowhere sensible for it to go.
                    if (key == VK_TAB) return IntPtr.Zero;
                }

                // The character messages for those same keys have to go too, or
                // a single line edit answers each one with a system beep.
                if (msg == WM_CHAR && (key == '\r' || key == '\n' || key == 27 || key == '\t'))
                    return IntPtr.Zero;
            }
            catch (Exception)
            {
                // A throw crossing back into Windows would be fatal. Swallow it
                // and let the control behave normally.
            }

            return CallWindowProc(_originalProc, hwnd, msg, wParam, lParam);
        }

        // ------------------------------------------------------------ interop

        private static IntPtr SetWindowLongPtrSafe(IntPtr hwnd, int index, IntPtr value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr(hwnd, index, value)
                : new IntPtr(SetWindowLong(hwnd, index, value.ToInt32()));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr Hwnd;
            public int Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public int Time;
            public POINT Point;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
        private static extern IntPtr CreateWindowEx(
            int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageText(IntPtr hwnd, int msg, IntPtr wParam, StringBuilder text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, int filterMin, int filterMax, int action);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
        private static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int index);
    }
}
