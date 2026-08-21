using ChatShared;
using EFT.UI;
using EFT.UI.Chat;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EFT;

namespace ReceiveAllChats.Patches;

internal sealed class TransferItemsClosePatch : ModulePatch
{
    private static FieldInfo _dialogueViewDialogueField;

    protected override MethodBase GetTargetMethod()
    {
        _dialogueViewDialogueField = AccessTools.Field(typeof(DialogueView), "_dialogue");
        return AccessTools.DeclaredMethod(typeof(TransferItemsScreen), nameof(TransferItemsScreen.Close));
    }

    [HarmonyPriority(Priority.Last)]
    [PatchPostfix]
    private static async void Postfix(IEnumerable<DialogueChatMessage> ____messages, ItemUiContext ____itemUiContext)
    {
        if (____messages == null || !____messages.Any())
        {
            return;
        }

        var socialNetwork = ____itemUiContext.Session.SocialNetwork;
        var transferredMessages = ____messages.ToList();
        var snapshot = ReceiveAllFromAllChatsPatch.LastCollectedMessages;
        var snapshotIds = snapshot == null ? null : new HashSet<string>(snapshot.Select(message => message._id));
        var isOwnTransfer = snapshotIds != null && transferredMessages.All(message => snapshotIds.Contains(message._id));
        if (isOwnTransfer)
        {
            ReceiveAllFromAllChatsPatch.ConsumeLastCollectedMessages();
        }

        var countedMessages = isOwnTransfer ? snapshot : transferredMessages;
        var mappedMessages = countedMessages
            .Select(message => new { Message = message, Dialogue = GetDialogFromMessage(message, socialNetwork) })
            .ToList();
        var unmappedMessages = mappedMessages.Count(entry => entry.Dialogue == null);
        if (unmappedMessages > 0)
        {
            Plugin.LogDebug($"Could not map {unmappedMessages} reward message(s) back to a dialogue.");
        }

        var messagesByDialogue = mappedMessages
            .Where(entry => entry.Dialogue != null)
            .GroupBy(entry => entry.Dialogue)
            .ToList();

        Plugin.LogDebug($"TransferItemsScreen closed. transferred={transferredMessages.Count}, ownTransfer={isOwnTransfer}, countedMessages={mappedMessages.Count}, mappedDialogues={messagesByDialogue.Count}");

        if (!isOwnTransfer && messagesByDialogue.Count <= 1)
        {
            return;
        }

        await ____itemUiContext.ClientSession.FlushOperationQueue();
        await Task.Yield();

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
                dialogueView.NewAttachmentsMessagesCount = dialogue.AttachmentsNew;
            }
        }

        MonoBehaviourSingleton<PreloaderUI>.Instance.MenuTaskBar.SetNewMessagesCount();
    }

    private static UpdatableChatDialogue GetDialogFromMessage(DialogueChatMessage message, SocialNetwork socialNetwork)
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
