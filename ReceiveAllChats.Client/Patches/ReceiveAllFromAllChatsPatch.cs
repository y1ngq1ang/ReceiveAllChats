using ChatShared;
using EFT;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SPTushonka.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReceiveAllChats.Patches;

/// <summary>
/// Replaces the per-dialogue "Receive All" transfer with a collection across every dialogue,
/// then hands the aggregated messages to the vanilla reward/transfer flow.
/// </summary>
internal sealed class ReceiveAllFromAllChatsPatch : ModulePatch
{
    private static bool _collecting;
    private static List<DialogueChatMessage> _lastCollectedMessages;

    internal static bool IsCollecting => _collecting;
    internal static IReadOnlyList<DialogueChatMessage> LastCollectedMessages => _lastCollectedMessages;
    internal static void ConsumeLastCollectedMessages() => _lastCollectedMessages = null;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(EFT.UI.Chat.ChatScreen), nameof(EFT.UI.Chat.ChatScreen.TransferAll));

    [PatchPrefix]
    private static bool Prefix(EFT.UI.Chat.ChatScreen __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        Plugin.LogDebug("Receive-all button clicked.");
        Collect(__instance);
        return false;
    }

    private static async void Collect(EFT.UI.Chat.ChatScreen chatScreen)
    {
        if (_collecting)
        {
            Plugin.LogDebug("Ignored receive-all click because collection is already running.");
            return;
        }

        _collecting = true;
        _lastCollectedMessages = null;
        try
        {
            var messages = await AttachmentCollector.CollectAllMessages(chatScreen._social);
            Plugin.LogDebug($"Collector returned {messages.Count} reward message(s).");
            _lastCollectedMessages = messages;
            if (messages.Count > 0)
            {
                Plugin.LogDebug("Opening transfer/reward handling screen.");
                // The screen opens asynchronously; collecting is considered finished here so
                // the button becomes usable again while the transfer window is up.
                _ = chatScreen.ProcessRewards(new Il2CppReferenceArray<DialogueChatMessage>(messages.ToArray()));
            }
            else
            {
                Plugin.LogDebug("No reward messages found; nothing to open.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Log?.LogError($"ReceiveAllChats failed to collect attachments: {exception}");
        }
        finally
        {
            _collecting = false;
        }
    }
}
