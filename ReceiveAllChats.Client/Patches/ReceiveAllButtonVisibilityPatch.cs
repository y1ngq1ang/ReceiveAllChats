using HarmonyLib;
using SPTushonka.Reflection.Patching;
using System.Reflection;

namespace ReceiveAllChats.Patches;

/// <summary>
/// Vanilla only lights the "Receive All" button up for the dialogue that is currently
/// selected. The mod keeps it visible whenever any dialogue still holds attachments.
/// </summary>
internal sealed class ReceiveAllButtonVisibilityPatch : ModulePatch
{
    private static bool? _lastReportedHasAttachments;

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(EFT.UI.Chat.ChatMessageSendBlock), "UpdateRewards");

    [PatchPostfix]
    private static void Postfix(EFT.UI.Chat.ChatMessageSendBlock __instance) => Apply(__instance);

    internal sealed class OnGlobalMessagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(EFT.UI.Chat.ChatMessageSendBlock), nameof(EFT.UI.Chat.ChatMessageSendBlock.LastMessageUpdatedHandler));

        [PatchPostfix]
        private static void Postfix(EFT.UI.Chat.ChatMessageSendBlock __instance) => Apply(__instance);
    }

    internal sealed class UpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(EFT.UI.Chat.ChatMessageSendBlock), nameof(EFT.UI.Chat.ChatMessageSendBlock.Update));

        [PatchPostfix]
        private static void Postfix(EFT.UI.Chat.ChatMessageSendBlock __instance) => Apply(__instance);
    }

    private static void Apply(EFT.UI.Chat.ChatMessageSendBlock block)
    {
        if (block == null)
        {
            return;
        }

        var receiveAllButton = block._receiveAllButton;
        if (receiveAllButton == null)
        {
            return;
        }

        var socialNetwork = ChatUi.GetSocialNetwork();
        if (socialNetwork == null)
        {
            return;
        }

        var hasAttachments = AttachmentCollector.HasPendingAttachments(socialNetwork);
        if (_lastReportedHasAttachments != hasAttachments)
        {
            _lastReportedHasAttachments = hasAttachments;
            Plugin.LogDebug($"Receive-all button visibility -> {hasAttachments}. {AttachmentCollector.DescribeDialogueFlags(socialNetwork)}");
        }

        receiveAllButton.gameObject.SetActive(hasAttachments);
        receiveAllButton.Interactable = hasAttachments && !ReceiveAllFromAllChatsPatch.IsCollecting;

        if (hasAttachments)
        {
            // 5.0 needs a message here, so the block is kept alive for the dialogue it is
            // showing; dialogues without a message fall back to the per-frame update patch.
            var message = block._dialogue?.Message;
            if (message != null)
            {
                block.CalculateEndStorageTime(message);
            }

            block.EnableBlock();
        }
        else
        {
            block.DisableBlock();
        }
    }
}
