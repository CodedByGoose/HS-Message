using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HearthstoneChatBuffer
{
    [BepInPlugin(Guid, "Hearthstone Chat Buffer", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string Guid = "com.codedbygoose.hearthstonechatbuffer";

        internal static ManualLogSource Log;

        internal static ConfigEntry<int> MaxMessagesPerConversation;
        internal static ConfigEntry<bool> LogToDisk;
        internal static ConfigEntry<string> LogDirectory;
        internal static ConfigEntry<bool> BrailleIncoming;
        internal static ConfigEntry<bool> CaptureAllSpeech;
        internal static ConfigEntry<int> TranscriptSize;
        internal static ConfigEntry<bool> EchoTypedCharacters;
        internal static ConfigEntry<bool> EchoTypedWords;

        private void Awake()
        {
            Log = Logger;

            MaxMessagesPerConversation = Config.Bind(
                "Buffer", "MaxMessagesPerConversation", 200,
                "How many messages to keep per person. Older ones are dropped. 0 means unlimited.");

            LogToDisk = Config.Bind(
                "Buffer", "LogToDisk", true,
                "Also append every whisper to a dated text file, so nothing is ever lost.");

            LogDirectory = Config.Bind(
                "Buffer", "LogDirectory", "",
                "Where to write those logs. Leave blank for BepInEx\\chat-logs.");

            BrailleIncoming = Config.Bind(
                "Braille", "BrailleIncomingMessages", false,
                "Silently push incoming whispers to a braille display as they arrive. " +
                "Lets you read chat mid-combat without touching speech at all. " +
                "UNTESTED against real hardware.");

            CaptureAllSpeech = Config.Bind(
                "Transcript", "CaptureAllSpeech", true,
                "Keep a rolling record of everything Hearthstone Access says, browsable with " +
                "Alt+comma and Alt+period. Unlike NVDA's speech history, the review position " +
                "does not jump back to the newest line every time the game speaks.");

            TranscriptSize = Config.Bind(
                "Transcript", "TranscriptSize", 300,
                "How many spoken lines to keep.");

            EchoTypedCharacters = Config.Bind(
                "Replying", "EchoTypedCharacters", false,
                "Speak every character as you type a reply. Accurate but chatty. " +
                "There is no real edit control for a screen reader to read, so this " +
                "plugin has to provide the feedback itself.");

            EchoTypedWords = Config.Bind(
                "Replying", "EchoTypedWords", true,
                "Speak each word as you finish it with a space.");

            try
            {
                var harmony = new Harmony(Guid);
                Hooks.Apply(harmony);
            }
            catch (Exception e)
            {
                Log.LogError("Failed to apply hooks. The plugin is inert: " + e);
                return;
            }

            // A detached object so we get an Update tick regardless of what
            // scene the game is in.
            var host = new GameObject("HearthstoneChatBuffer");
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<Runtime>();
            DontDestroyOnLoad(host);

            Log.LogInfo("Hearthstone Chat Buffer ready. Press Alt+H in game for the command list.");
        }
    }

    /// <summary>
    /// Drives the Alt layer. Kept separate from the Harmony prefix on purpose:
    /// the prefix only decides whether HSA gets to see a frame, while all of our
    /// own key handling happens here. That way our commands still work even if
    /// HSA's input method is not being called for some reason.
    /// </summary>
    internal class Runtime : MonoBehaviour
    {
        private void Update()
        {
            try
            {
                // Composing owns the whole keyboard; its input arrives in OnGUI.
                if (Compose.Active) return;

                AltLayer.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Alt layer tick failed: " + e);
            }
        }

        private void OnGUI()
        {
            if (!Compose.Active) return;

            try
            {
                Compose.HandleGuiEvent(Event.current);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Compose event handling failed: " + e);
            }
        }
    }
}
