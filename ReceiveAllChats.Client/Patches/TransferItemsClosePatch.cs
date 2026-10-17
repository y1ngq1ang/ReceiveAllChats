using ChatShared;
using HarmonyLib;
using SPTushonka.Reflection.Il2Cpp;
using SPTushonka.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace ReceiveAllChats.Patches;

/// <summary>
/// After the transfer window closes the dialogue counters have to be finalised. The 5.0
/// window exposes its message list and context as properties, and the chat screen is
/// reached through the UI singleton instead of a field lookup on <c>ChatScreen</c>.
/// </summary>
internal sealed class TransferItemsClosePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(EFT.UI.TransferItemsScreen), nameof(EFT.UI.TransferItemsScreen.Close));

    [HarmonyPriority(Priority.Last)]
    [PatchPostfix]
    private static async void Postfix(EFT.UI.TransferItemsScreen __instance)
    {
        var transferredMessages = new List<DialogueChatMessage>();
        var screenMessages = __instance?._messages;
        if (screenMessages != null)
        {
            foreach (var message in screenMessages)
            {
                if (message != null)
                {
                    transferredMessages.Add(message);
                }
            }
        }

        if (transferredMessages.Count == 0)
        {
            return;
        }

        var itemUiContext = __instance._itemUiContext;
        var socialNetwork = itemUiContext?.ClientSession?.SocialNetwork;
        if (socialNetwork == null)
        {
            Plugin.LogDebug("TransferItemsScreen closed before the social network was available.");
            return;
        }

        var snapshot = ReceiveAllFromAllChatsPatch.LastCollectedMessages;
        var isOwnTransfer = IsOwnTransfer(snapshot, transferredMessages);
        if (isOwnTransfer)
        {
            ReceiveAllFromAllChatsPatch.ConsumeLastCollectedMessages();
        }

        var countedMessages = isOwnTransfer ? AttachmentCollector.ToManagedList(snapshot) : transferredMessages;
        var messagesByDialogue = new List<KeyValuePair<UpdatableChatDialogue, List<DialogueChatMessage>>>();
        var unmappedMessages = 0;

        foreach (var message in countedMessages)
        {
            var dialogue = GetDialogFromMessage(message, socialNetwork);
            if (dialogue == null)
            {
                unmappedMessages++;
                continue;
            }

            var existing = messagesByDialogue.FindIndex(entry => entry.Key == dialogue);
            if (existing < 0)
            {
                messagesByDialogue.Add(new KeyValuePair<UpdatableChatDialogue, List<DialogueChatMessage>>(dialogue, new List<DialogueChatMessage> { message }));
            }
            else
            {
                messagesByDialogue[existing].Value.Add(message);
            }
        }

        if (unmappedMessages > 0)
        {
            Plugin.LogDebug($"Could not map {unmappedMessages} reward message(s) back to a dialogue.");
        }

        Plugin.LogDebug($"TransferItemsScreen closed. transferred={transferredMessages.Count}, ownTransfer={isOwnTransfer}, countedMessages={countedMessages.Count}, mappedDialogues={messagesByDialogue.Count}");

        if (!isOwnTransfer && messagesByDialogue.Count <= 1)
        {
            return;
        }

        var session = itemUiContext?.ClientSession?.TryCast<EFT.EftClientBackendSession>();
        if (session != null)
        {
            try
            {
                await session.FlushOperationQueue().AsManaged();
            }
            catch (Exception exception)
            {
                Plugin.LogDebug($"Flushing the operation queue failed. error={exception.GetBaseException().Message}");
            }
        }

        await Task.Yield();

        var dialogueViews = ChatUi.GetChatScreen()?._dialoguesContainer?.GetComponentsInChildren<EFT.UI.Chat.DialogueView>();

        foreach (var group in messagesByDialogue)
        {
            var dialogue = group.Key;
            var remaining = 0;
            foreach (var message in group.Value)
            {
                if (message.DisplayRewardStatus)
                {
                    remaining++;
                }
            }

            dialogue.AttachmentsNew = remaining;
            dialogue.HasMessagesWithRewards = remaining > 0;

            dialogue.OnDialogueAttachmentsChanged?.Invoke();
            Plugin.LogDebug($"Updated dialogue after transfer. id={dialogue._id}, type={dialogue.Type}, remaining={remaining}");

            if (dialogueViews == null)
            {
                continue;
            }

            foreach (var dialogueView in dialogueViews)
            {
                if (dialogueView != null && dialogueView._dialogue == dialogue)
                {
                    dialogueView.NewAttachmentsMessagesCount = dialogue.AttachmentsNew;
                    break;
                }
            }
        }

        try
        {
            MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance?.MenuTaskBar?.SetNewMessagesCount();
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Refreshing the new-message counters failed. error={exception.GetBaseException().Message}");
        }
    }

    private static bool IsOwnTransfer(IReadOnlyList<DialogueChatMessage> snapshot, List<DialogueChatMessage> transferredMessages)
    {
        if (snapshot == null)
        {
            return false;
        }

        var snapshotIds = new HashSet<string>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            snapshotIds.Add(snapshot[i]._id);
        }

        foreach (var message in transferredMessages)
        {
            if (!snapshotIds.Contains(message._id))
            {
                return false;
            }
        }

        return true;
    }

    private static UpdatableChatDialogue GetDialogFromMessage(DialogueChatMessage message, EFT.SocialNetwork socialNetwork)
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

        foreach (var dialogue in socialNetwork.Dialogues)
        {
            if (dialogue != null && dialogue._id == dialogueId)
            {
                return dialogue;
            }
        }

        Plugin.LogDebug($"No dialogue found for reward message. messageId={message._id}, type={message.Type}, memberId={dialogueId}");
        return null;
    }
}
