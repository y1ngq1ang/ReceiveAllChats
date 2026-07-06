using ChatShared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReceiveAllChats;

internal static class AttachmentCollector
{
    public static bool HasPendingAttachments(SocialNetworkClass socialNetwork) =>
        socialNetwork.Dialogues.Any(d =>
            d.Type != EMessageType.GlobalChat && (d.AttachmentsNew > 0 || d.HasMessagesWithRewards));

    public static async Task<ChatMessageClass[]> CollectAllMessages(SocialNetworkClass socialNetwork)
    {
        if (socialNetwork == null)
        {
            Plugin.LogWarning("ReceiveAllChats: social network was null while collecting attachments.");
            return Array.Empty<ChatMessageClass>();
        }

        var stopwatch = Stopwatch.StartNew();
        var initialDialogueCount = socialNetwork.Dialogues.Count();
        Plugin.LogDebug($"Collect started. initialDialogues={initialDialogueCount}, fullyRead={socialNetwork.FullyRead}, updating={socialNetwork.MessageListUpdating}");

        var loadedPages = 0;
        for (var i = 0; i < 50 && !socialNetwork.FullyRead; i++)
        {
            if (!await WaitForDialogueListIdle(socialNetwork))
            {
                Plugin.LogWarning($"ReceiveAllChats: timed out waiting for dialogue list before page {i + 1}.");
                break;
            }

            if (!await RequestNextDialoguePage(socialNetwork))
            {
                Plugin.LogWarning($"ReceiveAllChats: timed out while requesting dialogue page {i + 1}.");
                break;
            }

            loadedPages++;
            Plugin.LogDebug($"Loaded dialogue page {loadedPages}. totalDialogues={socialNetwork.Dialogues.Count()}, fullyRead={socialNetwork.FullyRead}");
        }

        var dialogues = socialNetwork.Dialogues
            .Where(d => d.Type != EMessageType.GlobalChat)
            .ToList();

        var flaggedDialogues = dialogues.Count(d => d.AttachmentsNew > 0 || d.HasMessagesWithRewards);
        Plugin.LogDebug($"Scanning dialogues. totalNonGlobal={dialogues.Count}, flagged={flaggedDialogues}, maxConcurrent={Plugin.MaxConcurrentRequests}, timeoutMs={Plugin.TimeoutMilliseconds}");

        var results = new ChatMessageClass[dialogues.Count][];
        using (var semaphore = new SemaphoreSlim(Plugin.MaxConcurrentRequests))
        {
            var tasks = dialogues.Select(async (dialogue, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    results[index] = await FetchDialogAttachments(socialNetwork, dialogue);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        var allMessages = new List<ChatMessageClass>();
        var timedOutRequests = 0;
        var dialoguesWithRewards = 0;
        var staleCounters = 0;

        for (var i = 0; i < dialogues.Count; i++)
        {
            if (results[i] == null)
            {
                timedOutRequests++;
                continue;
            }

            if (results[i].Length > 0)
            {
                dialoguesWithRewards++;
                allMessages.AddRange(results[i]);
                Plugin.LogDebug($"Dialogue returned rewards. id={dialogues[i]._id}, type={dialogues[i].Type}, attachmentsNew={dialogues[i].AttachmentsNew}, hasRewardsMarker={dialogues[i].HasMessagesWithRewards}, messages={results[i].Length}");
            }
            else if (dialogues[i].AttachmentsNew > 0)
            {
                dialogues[i].AttachmentsNew = 0;
                dialogues[i].OnDialogueAttachmentsChanged.Invoke();
                staleCounters++;
                Plugin.LogDebug($"Cleared stale attachment counter. id={dialogues[i]._id}, type={dialogues[i].Type}");
            }
        }

        Plugin.LogDebug($"Collect finished. messages={allMessages.Count}, dialoguesWithRewards={dialoguesWithRewards}, timedOutRequests={timedOutRequests}, staleCounters={staleCounters}, elapsedMs={stopwatch.ElapsedMilliseconds}");
        return allMessages.ToArray();
    }

    private static async Task<bool> WaitForDialogueListIdle(SocialNetworkClass socialNetwork)
    {
        var timeout = Task.Delay(Plugin.TimeoutMilliseconds);
        while (socialNetwork.MessageListUpdating)
        {
            var yield = Task.Yield();
            await yield;
            if (timeout.IsCompleted)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> RequestNextDialoguePage(SocialNetworkClass socialNetwork)
    {
        var tcs = new TaskCompletionSource<bool>();
        socialNetwork.UpdateDialogueList(() => tcs.TrySetResult(true));
        return await Task.WhenAny(tcs.Task, Task.Delay(Plugin.TimeoutMilliseconds)) == tcs.Task;
    }

    private static async Task<ChatMessageClass[]> FetchDialogAttachments(SocialNetworkClass socialNetwork, DialogueClass dialogue)
    {
        var dialogId = dialogue._id;
        var tcs = new TaskCompletionSource<ChatMessageClass[]>();
        socialNetwork.AllAttachmentsFromDialog(dialogId, messages => tcs.TrySetResult(messages ?? Array.Empty<ChatMessageClass>()));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(Plugin.TimeoutMilliseconds)) == tcs.Task;
        if (!completed)
        {
            Plugin.LogWarning($"ReceiveAllChats: timed out fetching attachments. id={dialogId}, type={dialogue.Type}, attachmentsNew={dialogue.AttachmentsNew}, hasRewardsMarker={dialogue.HasMessagesWithRewards}");
            return null;
        }

        return tcs.Task.Result;
    }
}
