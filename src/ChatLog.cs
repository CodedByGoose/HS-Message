using System;
using System.IO;
using System.Text;

namespace HSMessage
{
    /// <summary>
    /// Appends every whisper to a plain text file, one day per file. Belt and
    /// braces: even if the in-game buffer has a bug, or you close the client
    /// before reading something, the message is still on disk.
    /// </summary>
    internal static class ChatLog
    {
        private static string _dir;
        private static bool _broken;

        private static string Directory
        {
            get
            {
                if (_dir != null) return _dir;

                var configured = Plugin.LogDirectory.Value;
                _dir = string.IsNullOrEmpty(configured)
                    ? Path.Combine(BepInEx.Paths.BepInExRootPath, "chat-logs")
                    : configured;

                return _dir;
            }
        }

        internal static void Append(string peer, string text, bool outgoing)
        {
            if (!Plugin.LogToDisk.Value || _broken) return;

            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    System.IO.Directory.CreateDirectory(Directory);

                var path = Path.Combine(
                    Directory, "hearthstone-chat-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");

                var line = string.Format("[{0}] {1} {2}: {3}{4}",
                    DateTime.Now.ToString("HH:mm:ss"),
                    outgoing ? "->" : "<-",
                    peer,
                    text,
                    Environment.NewLine);

                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception e)
            {
                // Log once and stop trying. A read-only folder should never
                // take the plugin down with it.
                _broken = true;
                Plugin.Log.LogWarning("Chat logging disabled, could not write: " + e.Message);
            }
        }
    }
}
