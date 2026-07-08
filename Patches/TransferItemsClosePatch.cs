using ChatShared;
using EFT.UI;
using EFT.UI.Chat;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ReceiveAllChats.Patches;

internal sealed class TransferItemsClosePatch : ModulePatch
{
    private static FieldInfo _dialogueViewDialogueField;

    protected override MethodBase GetTargetMethod()
    {
        _dialogueViewDialogueField = AccessTools.Field(typeof(DialogueView), "dialogueClass");
        return AccessTools.DeclaredMethod(typeof(TransferItemsScreen), nameof(TransferItemsScreen.Close));
    }

    [HarmonyPriority(Priority.Last)]
    [PatchPostfix]
    private static async void Postfix(IEnumerable<ChatMessageClass> ___ienumerable_0, ItemUiContext ___itemUiContext_0)
    {
        if (___ienumerable_0 == null || !___ienumerable_0.Any())
        {
            return;
        }

        var socialNetwork = ___itemUiContext_0.Session.SocialNetwork;
        var mappedMessages = ___ienumerable_0
            .Select(message => new { Message = message, Dialogue = GetDialogFromMessage(message, socialNetwork) })
            .ToList();
        var unmappedMessages = mappedMessages.Count(entry => entry.Dialogue == null);
        if (unmappedMessages > 0)
        {
            Plugin.LogDebug($"Could not map {unmappedMessages} transferred reward message(s) back to a dialogue.");
        }

        var messagesByDialogue = mappedMessages
            .Where(entry => entry.Dialogue != null)
            .GroupBy(entry => entry.Dialogue)
            .ToList();

        Plugin.LogDebug($"TransferItemsScreen closed. messages={mappedMessages.Count}, mappedDialogues={messagesByDialogue.Count}");

        if (messagesByDialogue.Count <= 1)
        {
            return;
        }

        await ___itemUiContext_0.ClientSession.FlushOperationQueue();

        var dialoguesContainerField = AccessTools.Field(typeof(ChatScreen), "_dialoguesContainer");
        var chatScreen = MonoBehaviourSingleton<CommonUI>.Instance?.ChatScreen;
        var dialoguesContainer = chatScreen == null
            ? null
            : dialoguesContainerField.GetValue(chatScreen) as DialoguesContainer;
        DialogueView[] dialogueViews = dialoguesContainer?.GetComponentsInChildren<DialogueView>();

        foreach (var group in messagesByDialogue)
        {
            var dialogue = group.Key;
            var remaining = group.Count(entry => entry.Message.DisplayRewardStatus);

            dialogue.AttachmentsNew = remaining;
            dialogue.HasMessagesWithRewards = remaining > 0;

            dialogue.OnDialogueAttachmentsChanged.Invoke();
            Plugin.LogDebug($"Updated dialogue after transfer. id={dialogue._id}, type={dialogue.Type}, remaining={remaining}");

            if (dialogueViews == null)
            {
                continue;
            }

            var dialogueView = dialogueViews.FirstOrDefault(view =>
                _dialogueViewDialogueField.GetValue(view) == dialogue);

            if (dialogueView != null)
            {
                dialogueView.Int32_1 = dialogue.AttachmentsNew;
            }
        }

        MonoBehaviourSingleton<PreloaderUI>.Instance.MenuTaskBar.method_12();
    }

    private static DialogueClass GetDialogFromMessage(ChatMessageClass message, SocialNetworkClass socialNetwork)
    {
        string dialogueId;
        if (message.Member != null)
        {
            dialogueId = message.Member.Id;
        }
        else if (message.Type == EMessageType.SystemMessage)
        {
            dialogueId = socialNetwork.SystemMember.Id;
        }
        else
        {
            Plugin.LogDebug($"Could not derive dialogue id from message. messageId={message._id}, type={message.Type}");
            return null;
        }

        var dialogue = socialNetwork.Dialogues.FirstOrDefault(d => d._id == dialogueId);
        if (dialogue == null)
        {
            Plugin.LogDebug($"No dialogue found for reward message. messageId={message._id}, type={message.Type}, memberId={dialogueId}");
        }

        return dialogue;
    }
}
