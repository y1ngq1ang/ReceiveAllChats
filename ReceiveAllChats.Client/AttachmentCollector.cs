using ChatShared;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReceiveAllChats.Shared;
using SPTushonka.Reflection.Il2Cpp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EFT;

namespace ReceiveAllChats;

internal static class AttachmentCollector
{
    private const string ServerAggregationRoute = ReceiveAllChatsProtocol.GetAllAttachmentsRoute;

    public static bool HasPendingAttachments(SocialNetwork socialNetwork)
    {
        if (socialNetwork == null)
        {
            return false;
        }

        foreach (var dialogue in socialNetwork.Dialogues)
        {
            if (dialogue == null || dialogue.Type == EMessageType.GlobalChat)
            {
                continue;
            }

            if (dialogue.AttachmentsNew > 0 || dialogue.HasMessagesWithRewards || HasLoadedRewardMessage(dialogue))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The dialogue counters can lag behind the messages that are already loaded (for
    /// example after the mailbox was read), so loaded messages are checked as well.
    /// </summary>
    private static bool HasLoadedRewardMessage(UpdatableChatDialogue dialogue)
    {
        try
        {
            var messages = dialogue.ChatMessages;
            if (messages == null)
            {
                return false;
            }

            foreach (var message in messages)
            {
                if (message != null && message.DisplayRewardStatus)
                {
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Could not inspect loaded messages. id={dialogue._id}, error={exception.Message}");
        }

        return false;
    }

    public static string DescribeDialogueFlags(SocialNetwork socialNetwork)
    {
        if (socialNetwork == null)
        {
            return "socialNetwork=null";
        }

        var description = new System.Text.StringBuilder();
        foreach (var dialogue in socialNetwork.Dialogues)
        {
            if (dialogue == null)
            {
                description.Append("null  ");
                continue;
            }

            description.Append(dialogue._id)
                .Append(":type=").Append(dialogue.Type)
                .Append(":attachments=").Append(dialogue.AttachmentsNew)
                .Append(":rewards=").Append(dialogue.HasMessagesWithRewards)
                .Append("  ");
        }

        return description.ToString();
    }

    public static async Task<List<DialogueChatMessage>> CollectAllMessages(SocialNetwork socialNetwork)
    {
        if (socialNetwork == null)
        {
            Plugin.LogWarning("ReceiveAllChats: social network was null while collecting attachments.");
            return new List<DialogueChatMessage>();
        }

        var stopwatch = Stopwatch.StartNew();
        var dialogues = GetCollectableDialogues(socialNetwork);
        Plugin.LogDebug($"Collect started. initialDialogues={dialogues.Count}, fullyRead={socialNetwork.FullyRead}, updating={socialNetwork.MessageListUpdating}");

        var serverMessages = await TryCollectFromServer(socialNetwork);
        if (serverMessages != null)
        {
            // The aggregate response is deserialized in isolation, so messages from
            // dialogues the UI has not loaded may not resolve when the transfer window
            // is built. Re-fetch every dialogue that still reports pending attachments
            // through the game's own API and merge the results by message id.
            var messages = await MergeWithPendingDialogues(socialNetwork, dialogues, serverMessages);
            ApplyServerResult(socialNetwork, messages);
            LogCollectedMessages(socialNetwork, messages);
            Plugin.LogDebug($"Collect finished via server aggregation. serverMessages={serverMessages.Count}, mergedMessages={messages.Count}, elapsedMs={stopwatch.ElapsedMilliseconds}");
            return messages;
        }

        Plugin.LogDebug("Server aggregation unavailable; falling back to dialogue-list scanning.");
        if (!socialNetwork.FullyRead)
        {
            var beforeCount = dialogues.Count;
            if (await RequestNextDialoguePage(socialNetwork))
            {
                dialogues = GetCollectableDialogues(socialNetwork);
                Plugin.LogDebug($"Refreshed dialogue list. before={beforeCount}, after={dialogues.Count}, fullyRead={socialNetwork.FullyRead}, updating={socialNetwork.MessageListUpdating}");
            }
            else
            {
                Plugin.LogWarning("ReceiveAllChats: timed out while refreshing the dialogue list; scanning currently loaded dialogues.");
            }
        }

        var flaggedDialogues = CountFlagged(dialogues);
        Plugin.LogDebug($"Scanning dialogues. totalNonGlobal={dialogues.Count}, flagged={flaggedDialogues}, fullyRead={socialNetwork.FullyRead}, updating={socialNetwork.MessageListUpdating}, maxConcurrent={Plugin.MaxConcurrentRequests}, timeoutMs={Plugin.TimeoutMilliseconds}");

        var results = new List<DialogueChatMessage>[dialogues.Count];
        using (var semaphore = new SemaphoreSlim(Plugin.MaxConcurrentRequests))
        {
            var tasks = new List<Task>(dialogues.Count);
            for (var index = 0; index < dialogues.Count; index++)
            {
                var dialogueIndex = index;
                tasks.Add(FetchAsync(socialNetwork, dialogues, dialogueIndex, results, semaphore));
            }

            await Task.WhenAll(tasks);
        }

        var allMessages = new List<DialogueChatMessage>();
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

            if (results[i].Count > 0)
            {
                dialoguesWithRewards++;
                allMessages.AddRange(results[i]);
                Plugin.LogDebug($"Dialogue returned rewards. id={dialogues[i]._id}, type={dialogues[i].Type}, attachmentsNew={dialogues[i].AttachmentsNew}, hasRewardsMarker={dialogues[i].HasMessagesWithRewards}, messages={results[i].Count}");
            }
            else if (dialogues[i].AttachmentsNew > 0)
            {
                ClearCounters(dialogues[i]);
                staleCounters++;
                Plugin.LogDebug($"Cleared stale attachment counter. id={dialogues[i]._id}, type={dialogues[i].Type}");
            }
        }

        LogCollectedMessages(socialNetwork, allMessages);
        Plugin.LogDebug($"Collect finished. messages={allMessages.Count}, scannedDialogues={dialogues.Count}, dialoguesWithRewards={dialoguesWithRewards}, timedOutRequests={timedOutRequests}, staleCounters={staleCounters}, fullyRead={socialNetwork.FullyRead}, elapsedMs={stopwatch.ElapsedMilliseconds}");
        return allMessages;
    }

    /// <summary>
    /// Merges the aggregate response with messages fetched through
    /// <see cref="SocialNetwork.AllAttachmentsFromDialog"/>. The game's own fetch lets the
    /// client register the messages it is about to show, which the raw aggregate response
    /// does not do; the client-fetched message wins when both contain the same id.
    /// </summary>
    private static async Task<List<DialogueChatMessage>> MergeWithPendingDialogues(
        SocialNetwork socialNetwork,
        List<UpdatableChatDialogue> dialogues,
        List<DialogueChatMessage> serverMessages)
    {
        var merged = new Dictionary<string, DialogueChatMessage>();
        foreach (var message in serverMessages)
        {
            if (message?._id != null)
            {
                merged[message._id] = message;
            }
        }

        var pending = new List<UpdatableChatDialogue>();
        for (var i = 0; i < dialogues.Count; i++)
        {
            if (IsFlagged(dialogues[i]))
            {
                pending.Add(dialogues[i]);
            }
        }

        var pendingFetched = 0;
        if (pending.Count > 0)
        {
            var results = new List<DialogueChatMessage>[pending.Count];
            using (var semaphore = new SemaphoreSlim(Plugin.MaxConcurrentRequests))
            {
                var tasks = new List<Task>(pending.Count);
                for (var index = 0; index < pending.Count; index++)
                {
                    tasks.Add(FetchAsync(socialNetwork, pending, index, results, semaphore));
                }

                await Task.WhenAll(tasks);
            }

            for (var i = 0; i < pending.Count; i++)
            {
                if (results[i] == null)
                {
                    continue;
                }

                pendingFetched++;
                foreach (var message in results[i])
                {
                    if (message?._id != null)
                    {
                        merged[message._id] = message;
                    }
                }
            }
        }

        Plugin.LogDebug($"Merged aggregate with client-side fetches. serverMessages={serverMessages.Count}, pendingDialogues={pending.Count}, clientFetchedDialogues={pendingFetched}, mergedMessages={merged.Count}");
        return new List<DialogueChatMessage>(merged.Values);
    }

    private static void LogCollectedMessages(SocialNetwork socialNetwork, List<DialogueChatMessage> messages)
    {
        foreach (var message in messages)
        {
            Plugin.LogDebug($"Collected message. id={message._id}, dialogue={GetDialogueIdFromMessage(message, socialNetwork) ?? "unknown"}, anyItems={message.AnyItems}, itemsNull={message.Items == null}, stashNull={message.Items?.Stash == null}");
        }
    }

    private static async Task FetchAsync(
        SocialNetwork socialNetwork,
        List<UpdatableChatDialogue> dialogues,
        int index,
        List<DialogueChatMessage>[] results,
        SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync();
        try
        {
            results[index] = await FetchDialogAttachments(socialNetwork, dialogues[index]);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static List<UpdatableChatDialogue> GetCollectableDialogues(SocialNetwork socialNetwork)
    {
        var dialogues = new List<UpdatableChatDialogue>();
        if (socialNetwork == null)
        {
            return dialogues;
        }

        foreach (var dialogue in socialNetwork.Dialogues)
        {
            if (dialogue == null || dialogue.Type == EMessageType.GlobalChat)
            {
                continue;
            }

            dialogues.Add(dialogue);
        }

        return dialogues;
    }

    private static bool IsFlagged(UpdatableChatDialogue dialogue) =>
        dialogue.AttachmentsNew > 0 || dialogue.HasMessagesWithRewards;

    private static int CountFlagged(List<UpdatableChatDialogue> dialogues)
    {
        var count = 0;
        for (var i = 0; i < dialogues.Count; i++)
        {
            if (IsFlagged(dialogues[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static void ClearCounters(UpdatableChatDialogue dialogue)
    {
        dialogue.AttachmentsNew = 0;
        dialogue.HasMessagesWithRewards = false;
        dialogue.OnDialogueAttachmentsChanged?.Invoke();
    }

    private static async Task<List<DialogueChatMessage>> TryCollectFromServer(SocialNetwork socialNetwork)
    {
        if (!Plugin.UseServerAggregation)
        {
            Plugin.LogDebug("Server aggregation disabled via config.");
            return null;
        }

        var session = socialNetwork._session?.TryCast<EftClientBackendSession>();
        if (session == null)
        {
            Plugin.LogDebug($"Session does not expose the backend request pipeline. sessionType={socialNetwork._session?.GetType().Name ?? "null"}");
            return null;
        }

        var parameters = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
        parameters.Add("dialogId", "all");

        var request = new SendRequest
        {
            Url = session._backendUrls.Messaging + ServerAggregationRoute,
            Params = parameters,
            Retries = SendRequest.NoRetries,
            HandlingMode = ERequestHandlingMode.Ignore,
        };

        try
        {
            var requestTask = session.Send<ChatMessagesList>(request).AsManaged();
            if (await Task.WhenAny(requestTask, Task.Delay(Plugin.TimeoutMilliseconds)) != requestTask)
            {
                Plugin.LogWarning("ReceiveAllChats: server aggregation request timed out; falling back to dialogue-list scanning.");
                return null;
            }

            var response = await requestTask;
            var messages = ToManagedList(response?.messages);
            Plugin.LogDebug($"Server aggregation returned {messages.Count} reward message(s).");
            return messages;
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Server aggregation request failed (server mod missing?). error={exception.GetBaseException().Message}");
            return null;
        }
    }

    private static void ApplyServerResult(SocialNetwork socialNetwork, List<DialogueChatMessage> messages)
    {
        var rewardDialogueIds = new HashSet<string>();
        foreach (var message in messages)
        {
            var dialogueId = GetDialogueIdFromMessage(message, socialNetwork);
            if (dialogueId != null)
            {
                rewardDialogueIds.Add(dialogueId);
            }
        }

        var staleCounters = 0;
        foreach (var dialogue in GetCollectableDialogues(socialNetwork))
        {
            if (rewardDialogueIds.Contains(dialogue._id))
            {
                dialogue.HasMessagesWithRewards = true;
                continue;
            }

            if (dialogue.AttachmentsNew > 0 || dialogue.HasMessagesWithRewards)
            {
                ClearCounters(dialogue);
                staleCounters++;
            }
        }

        if (staleCounters > 0)
        {
            Plugin.LogDebug($"Cleared stale attachment counters after server aggregation. staleCounters={staleCounters}");
        }
    }

    private static string GetDialogueIdFromMessage(DialogueChatMessage message, SocialNetwork socialNetwork)
    {
        if (message?.Member != null)
        {
            return message.Member.Id;
        }

        if (message?.Type == EMessageType.SystemMessage)
        {
            return socialNetwork.SystemMember.Id;
        }

        return null;
    }

    private static async Task<bool> RequestNextDialoguePage(SocialNetwork socialNetwork)
    {
        try
        {
            var updateTask = socialNetwork.UpdateDialogueList().AsManaged();
            return await Task.WhenAny(updateTask, Task.Delay(Plugin.TimeoutMilliseconds)) == updateTask;
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Dialogue list refresh failed. error={exception.GetBaseException().Message}");
            return false;
        }
    }

    private static async Task<List<DialogueChatMessage>> FetchDialogAttachments(SocialNetwork socialNetwork, UpdatableChatDialogue dialogue)
    {
        var dialogId = dialogue._id;
        var tcs = new TaskCompletionSource<Il2CppReferenceArray<DialogueChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Il2CppSystem.Action<Il2CppReferenceArray<DialogueChatMessage>> onAttachmentsReceived =
            new System.Action<Il2CppReferenceArray<DialogueChatMessage>>(messages => tcs.TrySetResult(messages));
        socialNetwork.AllAttachmentsFromDialog(dialogId, onAttachmentsReceived);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(Plugin.TimeoutMilliseconds)) == tcs.Task;
        if (!completed)
        {
            Plugin.LogWarning($"ReceiveAllChats: timed out fetching attachments. id={dialogId}, type={dialogue.Type}, attachmentsNew={dialogue.AttachmentsNew}, hasRewardsMarker={dialogue.HasMessagesWithRewards}");
            return null;
        }

        return ToManagedList(tcs.Task.Result);
    }

    private static List<DialogueChatMessage> ToManagedList(Il2CppReferenceArray<DialogueChatMessage> messages)
    {
        var managed = new List<DialogueChatMessage>();
        if (messages == null)
        {
            return managed;
        }

        foreach (var message in messages)
        {
            if (message != null)
            {
                managed.Add(message);
            }
        }

        return managed;
    }

    public static List<DialogueChatMessage> ToManagedList(IReadOnlyList<DialogueChatMessage> messages)
    {
        var managed = new List<DialogueChatMessage>();
        if (messages == null)
        {
            return managed;
        }

        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i] != null)
            {
                managed.Add(messages[i]);
            }
        }

        return managed;
    }
}
