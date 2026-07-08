using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ReceiveAllChats.Patches;

internal sealed class DialogueListUpdatingPatch : ModulePatch
{
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
                if (__instance?.MessageListUpdating == true)
                {
                    __instance.MessageListUpdating = false;
                    Plugin.LogDebug("Cleared dialogue list updating flag after UpdateDialogueList callback.");
                }
            }
        };
    }
}
