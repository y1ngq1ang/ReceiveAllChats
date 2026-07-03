using ChatShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReceiveAllChats;

internal static class AttachmentCollector
{
    private const int RequestTimeoutMs = 15000;

    public static bool HasPendingAttachments(SocialNetworkClass socialNetwork) =>
        socialNetwork.Dialogues.Any(d =>
            d.Type != EMessageType.GlobalChat && (d.AttachmentsNew > 0 || d.HasMessagesWithRewards));

    public static async Task<ChatMessageClass[]> CollectAllMessages(SocialNetworkClass socialNetwork)
    {
        for (var i = 0; i < 50 && !socialNetwork.FullyRead; i++)
        {
            while (socialNetwork.MessageListUpdating)
            {
                await Task.Yield();
            }

            if (!await RequestNextDialoguePage(socialNetwork))
            {
                break;
            }
        }

        var dialogues = socialNetwork.Dialogues
            .Where(d => d.Type != EMessageType.GlobalChat)
            .ToList();

        var results = await Task.WhenAll(dialogues.Select(d => FetchDialogAttachments(socialNetwork, d._id)));
        var allMessages = new List<ChatMessageClass>();
        for (var i = 0; i < dialogues.Count; i++)
        {
            if (results[i] == null)
            {
                continue;
            }

            if (results[i].Length > 0)
            {
                allMessages.AddRange(results[i]);
            }
            else if (dialogues[i].AttachmentsNew > 0)
            {
                dialogues[i].AttachmentsNew = 0;
                dialogues[i].OnDialogueAttachmentsChanged.Invoke();
            }
        }

        return allMessages.ToArray();
    }

    private static async Task<bool> RequestNextDialoguePage(SocialNetworkClass socialNetwork)
    {
        var tcs = new TaskCompletionSource<bool>();
        socialNetwork.UpdateDialogueList(() => tcs.TrySetResult(true));
        return await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs)) == tcs.Task;
    }

    private static async Task<ChatMessageClass[]> FetchDialogAttachments(SocialNetworkClass socialNetwork, string dialogId)
    {
        var tcs = new TaskCompletionSource<ChatMessageClass[]>();
        socialNetwork.AllAttachmentsFromDialog(dialogId, messages => tcs.TrySetResult(messages ?? Array.Empty<ChatMessageClass>()));
        return await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs)) == tcs.Task ? tcs.Task.Result : null;
    }
}
