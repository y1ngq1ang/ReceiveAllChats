using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading;

namespace ReceiveAllChats.Patches;

internal sealed class DialogueListUpdatingPatch : ModulePatch
{
    private static int _clearedUpdatingCallbacks;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(SocialNetworkClass), nameof(SocialNetworkClass.UpdateDialogueList));

    [PatchPrefix]
    private static void Prefix(SocialNetworkClass __instance, ref Action callback)
    {
        var originalCallback = callback;
        callback = () =>
        {
            try
            {
                originalCallback?.Invoke();
            }
            finally
            {
                if (ReceiveAllFromAllChatsPatch.IsCollecting && __instance?.MessageListUpdating == true)
                {
                    __instance.MessageListUpdating = false;
                    var totalCleared = Interlocked.Increment(ref _clearedUpdatingCallbacks);
                    Plugin.LogDebug($"Cleared dialogue list updating flag after UpdateDialogueList callback. totalCleared={totalCleared}");
                }
            }
        };
    }
}
