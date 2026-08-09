using EFT.UI;
using EFT.UI.Chat;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using ChatShared;

namespace ReceiveAllChats.Patches;

internal sealed class ReceiveAllButtonVisibilityPatch : ModulePatch
{
    private static readonly MethodInfo EnableHighlight =
        AccessTools.Method(typeof(ChatMessageSendBlock), "EnableBlock");

    private static readonly MethodInfo DisableHighlight =
        AccessTools.Method(typeof(ChatMessageSendBlock), "DisableBlock");

    private static readonly MethodInfo KeepHighlightAlive =
        AccessTools.Method(typeof(ChatMessageSendBlock), "CalculateEndStorageTime");

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ChatMessageSendBlock), "UpdateRewards");

    [PatchPostfix]
    private static void Postfix(
        ChatMessageSendBlock __instance,
        SocialNetwork ___socialNetwork,
        DefaultUIButton ____receiveAllButton) =>
        Apply(__instance, ___socialNetwork, ____receiveAllButton);

    internal sealed class OnGlobalMessagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ChatMessageSendBlock), nameof(ChatMessageSendBlock.LastMessageUpdatedHandler));

        [PatchPostfix]
        private static void Postfix(
            ChatMessageSendBlock __instance,
            SocialNetwork ___socialNetwork,
            DefaultUIButton ____receiveAllButton,
            KeyValuePair<UpdatableChatDialogue, DialogueChatMessage> dialogue)
        {
            if (dialogue.Value.HasRewards)
            {
                Apply(__instance, ___socialNetwork, ____receiveAllButton);
            }
        }
    }

    internal sealed class UpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ChatMessageSendBlock), nameof(ChatMessageSendBlock.Update));

        [PatchPostfix]
        private static void Postfix(
            ChatMessageSendBlock __instance,
            SocialNetwork ___socialNetwork,
            DefaultUIButton ____receiveAllButton) =>
            Apply(__instance, ___socialNetwork, ____receiveAllButton);
    }

    private static void Apply(
        ChatMessageSendBlock block,
        SocialNetwork socialNetwork,
        DefaultUIButton receiveAllButton)
    {
        if (socialNetwork == null || receiveAllButton == null)
        {
            return;
        }

        var hasAttachments = AttachmentCollector.HasPendingAttachments(socialNetwork);
        receiveAllButton.gameObject.SetActive(hasAttachments);
        receiveAllButton.Interactable = hasAttachments && !ReceiveAllFromAllChatsPatch.IsCollecting;

        if (hasAttachments)
        {
            KeepHighlightAlive.Invoke(block, new object[] { null });
            EnableHighlight.Invoke(block, null);
        }
        else
        {
            DisableHighlight.Invoke(block, null);
        }
    }
}
