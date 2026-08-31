using System;
using System.Diagnostics;
using System.IO;

namespace HSMessage
{
    /// <summary>
    /// How NVDA is set to echo what you type. The three states are NVDA's own,
    /// from its TypingEcho enum.
    /// </summary>
    internal enum TypingEcho
    {
        Off = 0,

        /// <summary>Speak only when the focus is a real edit control.</summary>
        EditControls = 1,

        /// <summary>Speak in any window at all.</summary>
        Always = 2,
    }

    /// <summary>
    /// Reads NVDA's own "speak typed characters" and "speak typed words"
    /// settings out of nvda.ini, so the reply box can follow them instead of
    /// making you configure the same preference twice.
    ///
    /// There is no live way to ask. NVDA's controller client exposes speech and
    /// braille and nothing else, so the settings file is the only source. That
    /// brings one real limitation: NVDA writes the file when it exits or when
    /// you press NVDA+Control+C, so toggling echo mid-session with NVDA+2 is
    /// not seen until it has been saved.
    ///
    /// Only consulted when NVDA is actually running. The file would otherwise
    /// still be sitting there for someone who has since moved to JAWS, and we
    /// would be following a screen reader they no longer use.
    /// </summary>
    internal static class NvdaSettings
    {
        private static TypingEcho? _characters;
        private static TypingEcho? _words;

        /// <summary>NVDA's setting, or null if we could not read one.</summary>
        internal static TypingEcho? SpeakTypedCharacters { get { return _characters; } }
        internal static TypingEcho? SpeakTypedWords { get { return _words; } }

        /// <summary>
        /// Re-read the file. Called when the reply box opens, which is rare
        /// enough that parsing a two kilobyte ini costs nothing and picks up
        /// any change saved since the last reply.
        /// </summary>
        internal static void Refresh()
        {
            _characters = null;
            _words = null;

            try
            {
                var reader = Speech.DetectScreenReader();
                if (!IsNvda(reader))
                {
                    Report("not following NVDA: active screen reader is " +
                           (string.IsNullOrEmpty(reader) ? "unknown" : reader));
                    return;
                }

                var path = FindConfig();
                if (path == null)
                {
                    Report("not following NVDA: could not find nvda.ini");
                    return;
                }

                Parse(path);
                Report("NVDA echo settings from " + path + ": characters=" +
                       Describe(_characters) + ", words=" + Describe(_words));
            }
            catch (Exception e)
            {
                // Never worth failing a reply over. We simply fall back to the
                // plugin's own echo settings.
                Plugin.Log.LogWarning("Could not read NVDA's settings: " + e.Message);
                _characters = null;
                _words = null;
            }
        }

        private static string Describe(TypingEcho? value)
        {
            return value == null ? "unreadable" : value.Value.ToString();
        }

        /// <summary>
        /// Says what was decided, but only when the answer changes. This runs
        /// every time the reply box opens, and a line per whisper would bury
        /// the log; saying nothing at all was worse, which is how a silent
        /// fallback to the plugin's own settings went unnoticed.
        /// </summary>
        private static string _lastReport;

        private static void Report(string message)
        {
            if (message == _lastReport) return;
            _lastReport = message;
            Plugin.Log.LogInfo(message);
        }

        private static bool IsNvda(string screenReaderName)
        {
            if (!string.IsNullOrEmpty(screenReaderName))
                return screenReaderName.IndexOf("nvda", StringComparison.OrdinalIgnoreCase) >= 0;

            // Tolk could not tell us. Fall back to asking the operating system
            // whether NVDA is there at all, which is weaker -- it cannot tell
            // an idle NVDA from the one actually speaking -- but better than
            // giving up.
            try
            {
                var running = Process.GetProcessesByName("nvda");
                foreach (var p in running) p.Dispose();
                return running.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// An installed NVDA keeps its config in the roaming profile; a
        /// portable copy keeps it beside the executable. The installed case is
        /// tried first because it is much the commoner one and costs nothing.
        /// </summary>
        private static string FindConfig()
        {
            // The environment variable rather than GetFolderPath. Unity's Mono
            // is not always willing to resolve a special folder, and when it
            // declines it returns an empty string rather than throwing, which
            // silently turns into a relative path that exists nowhere.
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (string.IsNullOrEmpty(appData))
            {
                try
                {
                    appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                }
                catch (Exception)
                {
                    appData = null;
                }
            }

            if (!string.IsNullOrEmpty(appData))
            {
                var roaming = Path.Combine(appData, "nvda\\nvda.ini");
                if (File.Exists(roaming)) return roaming;
            }

            return FindPortableConfig();
        }

        /// <summary>
        /// Best effort. Reading another process's module list can be refused
        /// outright, and a 64 bit game asking about a 32 bit NVDA is exactly
        /// the case where it often is, so nothing here is relied upon.
        /// </summary>
        private static string FindPortableConfig()
        {
            Process[] running;
            try
            {
                running = Process.GetProcessesByName("nvda");
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                foreach (var p in running)
                {
                    try
                    {
                        var exe = p.MainModule != null ? p.MainModule.FileName : null;
                        if (string.IsNullOrEmpty(exe)) continue;

                        var dir = Path.GetDirectoryName(exe);
                        if (string.IsNullOrEmpty(dir)) continue;

                        var portable = Path.Combine(dir, "userConfig\\nvda.ini");
                        if (File.Exists(portable)) return portable;
                    }
                    catch (Exception)
                    {
                        // Refused. Nothing to do but try the next one.
                    }
                }
            }
            finally
            {
                foreach (var p in running) p.Dispose();
            }

            return null;
        }

        /// <summary>
        /// nvda.ini is configobj format: tab indented, with nested sections
        /// written as double brackets. We want two keys from the top level
        /// [keyboard] section, so anything at greater depth is skipped rather
        /// than risking a same-named key from somewhere else in the file.
        /// </summary>
        private static void Parse(string path)
        {
            bool inKeyboard = false;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[')
                {
                    // "[keyboard]" is the one we want. "[[keyboard]]" would be
                    // a subsection of something else.
                    inKeyboard = line == "[keyboard]";
                    continue;
                }

                if (!inKeyboard) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim().Trim('"', '\'');

                if (key == "speakTypedCharacters") _characters = ParseEcho(value);
                else if (key == "speakTypedWords") _words = ParseEcho(value);
            }
        }

        /// <summary>
        /// Current NVDA writes 0, 1 or 2. Older versions wrote True or False
        /// for the same settings, back when the choice was only on or off, and
        /// on then meant speaking in every window.
        /// </summary>
        private static TypingEcho? ParseEcho(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            switch (value.ToLowerInvariant())
            {
                case "0":
                case "false":
                case "no":
                    return TypingEcho.Off;

                case "1":
                    return TypingEcho.EditControls;

                case "2":
                case "true":
                case "yes":
                    return TypingEcho.Always;
            }

            return null;
        }
    }
}
