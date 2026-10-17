using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Il2Cpp;
using SPTushonka.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading;

namespace ReceiveAllChats.Patches;

/// <summary>
/// The 5.0 dialogue list refresh is a <c>Task</c> instead of a callback overload, so the
/// previous flag reset is applied once that task finishes rather than inside a wrapped callback.
/// </summary>
internal sealed class DialogueListUpdatingPatch : ModulePatch
{
    private static int _clearedUpdatingCallbacks;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(SocialNetwork), nameof(SocialNetwork.UpdateDialogueList), Type.EmptyTypes);

    [PatchPostfix]
    private static void Postfix(SocialNetwork __instance, Il2CppSystem.Threading.Tasks.Task __result)
    {
        if (__result == null || __instance == null || !ReceiveAllFromAllChatsPatch.IsCollecting)
        {
            return;
        }

        ClearUpdatingFlagWhenFinished(__instance, __result);
    }

    private static async void ClearUpdatingFlagWhenFinished(SocialNetwork socialNetwork, Il2CppSystem.Threading.Tasks.Task updateTask)
    {
        try
        {
            await updateTask.AsManaged();
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Dialogue list update faulted. error={exception.GetBaseException().Message}");
            return;
        }

        if (socialNetwork == null || !socialNetwork.MessageListUpdating)
        {
            return;
        }

        socialNetwork.MessageListUpdating = false;
        var totalCleared = Interlocked.Increment(ref _clearedUpdatingCallbacks);
        Plugin.LogDebug($"Cleared dialogue list updating flag after UpdateDialogueList completed. totalCleared={totalCleared}");
    }
}
