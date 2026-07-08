using EFT.UI.Chat;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ReceiveAllChats.Patches;

internal sealed class ReceiveAllFromAllChatsPatch : ModulePatch
{
    private static readonly MethodInfo ProcessMessagesMethod =
        AccessTools.Method(typeof(ChatScreen), "method_17");

    private static bool _collecting;

    internal static bool IsCollecting => _collecting;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ChatScreen), nameof(ChatScreen.method_3));

    [PatchPrefix]
    private static bool Prefix(ChatScreen __instance, SocialNetworkClass ___socialNetworkClass)
    {
        Plugin.LogDebug("Receive-all button clicked.");
        Collect(__instance, ___socialNetworkClass);
        return false;
    }

    private static async void Collect(ChatScreen chatScreen, SocialNetworkClass socialNetwork)
    {
        if (_collecting)
        {
            Plugin.LogDebug("Ignored receive-all click because collection is already running.");
            return;
        }

        _collecting = true;
        try
        {
            var messages = await AttachmentCollector.CollectAllMessages(socialNetwork);
            Plugin.LogDebug($"Collector returned {messages.Length} reward message(s).");
            if (messages.Length > 0)
            {
                Plugin.LogDebug("Opening transfer/reward handling screen.");
                ProcessMessagesMethod.Invoke(chatScreen, new object[] { messages });
            }
            else
            {
                Plugin.LogDebug("No reward messages found; nothing to open.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"ReceiveAllChats failed to collect attachments: {exception}");
        }
        finally
        {
            _collecting = false;
        }
    }
}
