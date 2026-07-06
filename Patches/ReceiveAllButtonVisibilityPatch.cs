using EFT.UI;
using EFT.UI.Chat;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace ReceiveAllChats.Patches;

internal sealed class ReceiveAllButtonVisibilityPatch : ModulePatch
{
    private static readonly MethodInfo EnableHighlight =
        AccessTools.Method(typeof(ChatMessageSendBlock), "method_0");

    private static readonly MethodInfo DisableHighlight =
        AccessTools.Method(typeof(ChatMessageSendBlock), "method_1");

    private static readonly MethodInfo KeepHighlightAlive =
        AccessTools.Method(typeof(ChatMessageSendBlock), "method_5");

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ChatMessageSendBlock), "method_2");

    [PatchPostfix]
    private static void Postfix(
        ChatMessageSendBlock __instance,
        SocialNetworkClass ___socialNetworkClass,
        DefaultUIButton ____receiveAllButton) =>
        Apply(__instance, ___socialNetworkClass, ____receiveAllButton);

    internal sealed class OnGlobalMessagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ChatMessageSendBlock), nameof(ChatMessageSendBlock.method_4));

        [PatchPostfix]
        private static void Postfix(
            ChatMessageSendBlock __instance,
            SocialNetworkClass ___socialNetworkClass,
            DefaultUIButton ____receiveAllButton,
            KeyValuePair<DialogueClass, ChatMessageClass> dialogue)
        {
            if (dialogue.Value.HasRewards)
            {
                Apply(__instance, ___socialNetworkClass, ____receiveAllButton);
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
            SocialNetworkClass ___socialNetworkClass,
            DefaultUIButton ____receiveAllButton) =>
            Apply(__instance, ___socialNetworkClass, ____receiveAllButton);
    }

    private static void Apply(
        ChatMessageSendBlock block,
        SocialNetworkClass socialNetwork,
        DefaultUIButton receiveAllButton)
    {
        if (socialNetwork == null || receiveAllButton == null)
        {
            return;
        }

        var hasAttachments = AttachmentCollector.HasPendingAttachments(socialNetwork);
        receiveAllButton.gameObject.SetActive(hasAttachments);

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
