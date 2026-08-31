using System;
using System.Reflection;
using HarmonyLib;

namespace HSMessage
{
    /// <summary>
    /// Every hook into Hearthstone Access, resolved by name at runtime.
    ///
    /// Nothing here is a compile-time reference to Assembly-CSharp, which is
    /// what lets the plugin survive HSA and Hearthstone updates without being
    /// rebuilt. If a method we want has moved or been renamed, that hook logs a
    /// clear error and is skipped -- the game keeps working, we just lose that
    /// one feature.
    /// </summary>
    internal static class Hooks
    {
        private static FieldInfo _speakerName;
        private static FieldInfo _receiverName;
        private static FieldInfo _messageText;
        private static FieldInfo _whisper;
        private static MethodInfo _getSpeaker;
        private static MethodInfo _getReceiver;

        internal static void Apply(Harmony harmony)
        {
            PatchChatBubble(harmony);
            PatchInputGate(harmony);

            if (Plugin.CaptureAllSpeech.Value)
                PatchScreenReader(harmony);
        }

        // ------------------------------------------------------------ capture

        /// <summary>
        /// ChatBubbleFrame.ReadMessage is the exact moment HSA announces a
        /// whisper, and the instance is holding the sender and the text in
        /// private fields. Reading them here gives us properly structured data
        /// with no string parsing and no dependence on the display language.
        ///
        /// This is a postfix, so HSA still speaks the message exactly as it does
        /// today. We are only keeping a copy.
        /// </summary>
        private static void PatchChatBubble(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ChatBubbleFrame");
            if (type == null)
            {
                Plugin.Log.LogError("ChatBubbleFrame not found. Chat capture is disabled.");
                return;
            }

            var target = AccessTools.Method(type, "ReadMessage");
            if (target == null)
            {
                Plugin.Log.LogError("ChatBubbleFrame.ReadMessage not found. Chat capture is disabled.");
                return;
            }

            _speakerName = AccessTools.Field(type, "m_speakerName");
            _receiverName = AccessTools.Field(type, "m_receiverName");
            _messageText = AccessTools.Field(type, "m_messageText");

            if (_speakerName == null || _receiverName == null || _messageText == null)
            {
                Plugin.Log.LogError(
                    "ChatBubbleFrame fields have changed shape. Chat capture is disabled.");
                return;
            }

            // Optional extras: without these we still capture messages, we just
            // cannot reply to them.
            _whisper = AccessTools.Field(type, "m_whisper");
            var whisperUtil = AccessTools.TypeByName("WhisperUtil");
            if (whisperUtil != null)
            {
                _getSpeaker = AccessTools.Method(whisperUtil, "GetSpeaker");
                _getReceiver = AccessTools.Method(whisperUtil, "GetReceiver");
            }

            if (_whisper == null || _getSpeaker == null)
                Plugin.Log.LogWarning("Could not resolve whisper sender lookup. Replying will be unavailable.");

            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(Hooks).GetMethod(nameof(OnReadMessage), BindingFlags.NonPublic | BindingFlags.Static)));

            Plugin.Log.LogInfo("Hooked ChatBubbleFrame.ReadMessage.");
        }

        private static void OnReadMessage(object __instance)
        {
            try
            {
                var text = _messageText.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(text)) return;

                var receiver = _receiverName.GetValue(__instance) as string;
                var speaker = _speakerName.GetValue(__instance) as string;

                if (!string.IsNullOrEmpty(receiver))
                {
                    ChatStore.Add(receiver, text, true, ResolvePlayer(__instance, _getReceiver));
                    ChatLog.Append(receiver, text, true);
                }
                else if (!string.IsNullOrEmpty(speaker))
                {
                    ChatStore.Add(speaker, text, false, ResolvePlayer(__instance, _getSpeaker));
                    ChatLog.Append(speaker, text, false);

                    if (Plugin.BrailleIncoming.Value)
                        Speech.Braille(speaker + ": " + text);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to capture a chat message: " + e);
            }
        }

        /// <summary>
        /// Pulls the BnetPlayer off the whisper so we know who to reply to.
        /// Returns null if anything is missing; that only costs us replying.
        /// </summary>
        private static object ResolvePlayer(object chatBubbleFrame, MethodInfo lookup)
        {
            if (_whisper == null || lookup == null) return null;

            try
            {
                var whisper = _whisper.GetValue(chatBubbleFrame);
                if (whisper == null) return null;
                return lookup.Invoke(null, new[] { whisper });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not resolve whisper player: " + e.Message);
                return null;
            }
        }

        // -------------------------------------------------------------- input

        /// <summary>
        /// AccessibilityMgr.HandleKeyboardInput is the single entry point for
        /// all of HSA's key handling. Suppressing it for frames where Alt is
        /// held hands us the whole Alt layer with no per-key conflict work.
        /// </summary>
        private static void PatchInputGate(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Accessibility.AccessibilityMgr");
            if (type == null)
            {
                Plugin.Log.LogError(
                    "Accessibility.AccessibilityMgr not found. Alt commands will clash with HSA.");
                return;
            }

            var target = AccessTools.Method(type, "HandleKeyboardInput");
            if (target == null)
            {
                Plugin.Log.LogError(
                    "AccessibilityMgr.HandleKeyboardInput not found. Alt commands will clash with HSA.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(Hooks).GetMethod(nameof(BeforeHsaInput), BindingFlags.NonPublic | BindingFlags.Static)));

            Plugin.Log.LogInfo("Hooked AccessibilityMgr.HandleKeyboardInput; Alt layer reserved.");
        }

        /// <summary>Returning false makes Harmony skip HSA's original method.</summary>
        private static bool BeforeHsaInput()
        {
            try
            {
                return !AltLayer.ShouldSuppressHsa();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Input gate failed, letting HSA through: " + e);
                return true;
            }
        }

        // --------------------------------------------------------- transcript

        /// <summary>
        /// ScreenReader.Output is the funnel every single line of HSA speech
        /// passes through. Tapping it gives a complete rolling transcript.
        /// </summary>
        private static void PatchScreenReader(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Accessibility.ScreenReader");
            if (type == null)
            {
                Plugin.Log.LogWarning("Accessibility.ScreenReader not found. Speech capture is disabled.");
                return;
            }

            var target = AccessTools.Method(type, "Output");
            if (target == null)
            {
                Plugin.Log.LogWarning("ScreenReader.Output not found. Speech capture is disabled.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(Hooks).GetMethod(nameof(OnScreenReaderOutput), BindingFlags.NonPublic | BindingFlags.Static)));

            Plugin.Log.LogInfo("Hooked ScreenReader.Output for the speech transcript.");
        }

        // Parameter name must match the original method's, so Harmony can bind it.
        private static void OnScreenReaderOutput(string text)
        {
            try
            {
                Transcript.Add(text);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Transcript capture failed: " + e);
            }
        }
    }
}
