using ChatShared;
using Comfort.Common;
using ReceiveAllChats.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReceiveAllChats;

internal static class AttachmentCollector
{
    private const string ServerAggregationRoute = ReceiveAllChatsProtocol.GetAllAttachmentsRoute;

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
        var serverMessages = await TryCollectFromServer(socialNetwork);
        if (serverMessages != null)
        {
            ApplyServerResult(socialNetwork, serverMessages);
            Plugin.LogDebug($"Collect finished via server aggregation. messages={serverMessages.Length}, elapsedMs={stopwatch.ElapsedMilliseconds}");
            return serverMessages;
        }

        Plugin.LogDebug("Server aggregation unavailable; falling back to dialogue-list scanning.");
        if (!socialNetwork.FullyRead)
        {
            var beforeCount = socialNetwork.Dialogues.Count();
            if (await RequestNextDialoguePage(socialNetwork))
            {
                Plugin.LogDebug($"Refreshed dialogue list. before={beforeCount}, after={socialNetwork.Dialogues.Count()}, fullyRead={socialNetwork.FullyRead}, updating={socialNetwork.MessageListUpdating}");
            }
            else
            {
                Plugin.LogWarning("ReceiveAllChats: timed out while refreshing the dialogue list; scanning currently loaded dialogues.");
            }
        }
        var dialogues = socialNetwork.Dialogues
            .Where(d => d.Type != EMessageType.GlobalChat)
            .ToList();

        var flaggedDialogues = dialogues.Count(d => d.AttachmentsNew > 0 || d.HasMessagesWithRewards);
        Plugin.LogDebug($"Scanning dialogues. totalNonGlobal={dialogues.Count}, flagged={flaggedDialogues}, fullyRead={socialNetwork.FullyRead}, updating={socialNetwork.MessageListUpdating}, maxConcurrent={Plugin.MaxConcurrentRequests}, timeoutMs={Plugin.TimeoutMilliseconds}");

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

        Plugin.LogDebug($"Collect finished. messages={allMessages.Count}, scannedDialogues={dialogues.Count}, dialoguesWithRewards={dialoguesWithRewards}, timedOutRequests={timedOutRequests}, staleCounters={staleCounters}, fullyRead={socialNetwork.FullyRead}, elapsedMs={stopwatch.ElapsedMilliseconds}");
        return allMessages.ToArray();
    }

    private static async Task<ChatMessageClass[]> TryCollectFromServer(SocialNetworkClass socialNetwork)
    {
        if (!Plugin.UseServerAggregation)
        {
            Plugin.LogDebug("Server aggregation disabled via config.");
            return null;
        }

        if (!(socialNetwork.IChatInteractions is ProfileEndpointFactoryAbstractClass session))
        {
            Plugin.LogDebug($"Session does not expose the backend request pipeline. sessionType={socialNetwork.IChatInteractions?.GetType().Name ?? "null"}");
            return null;
        }

        var request = new LegacyParamsStruct
        {
            Url = session.Gclass1392_0.Messaging + ServerAggregationRoute,
            Params = new Class35<string>("all"),
            Retries = LegacyParamsStruct.NoRetries,
            HandlingMode = ERequestHandlingMode.Ignore,
        };

        var tcs = new TaskCompletionSource<ChatMessageClass[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var requestTask = session.method_5(request, new Callback<GClass1061>(result =>
            {
                if (!result.Succeed)
                {
                    Plugin.LogDebug($"Server aggregation request failed (server mod missing?). error={result.Error}");
                    tcs.TrySetResult(null);
                    return;
                }

                tcs.TrySetResult(result.Value?.messages ?? Array.Empty<ChatMessageClass>());
            }));
            _ = requestTask.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Plugin.LogDebug($"Server aggregation request faulted. error={task.Exception?.GetBaseException().Message}");
                }

                tcs.TrySetResult(null);
            }, TaskContinuationOptions.NotOnRanToCompletion);
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Server aggregation request threw. error={exception.Message}");
            return null;
        }

        if (await Task.WhenAny(tcs.Task, Task.Delay(Plugin.TimeoutMilliseconds)) != tcs.Task)
        {
            Plugin.LogWarning("ReceiveAllChats: server aggregation request timed out; falling back to dialogue-list scanning.");
            return null;
        }

        return tcs.Task.Result;
    }

    private static void ApplyServerResult(SocialNetworkClass socialNetwork, ChatMessageClass[] messages)
    {
        var rewardDialogueIds = new HashSet<string>(messages
            .Select(message => GetDialogueIdFromMessage(message, socialNetwork))
            .Where(id => id != null));

        var staleCounters = 0;
        foreach (var dialogue in socialNetwork.Dialogues.Where(d => d.Type != EMessageType.GlobalChat).ToList())
        {
            if (rewardDialogueIds.Contains(dialogue._id))
            {
                dialogue.HasMessagesWithRewards = true;
                continue;
            }

            if (dialogue.AttachmentsNew > 0 || dialogue.HasMessagesWithRewards)
            {
                dialogue.AttachmentsNew = 0;
                dialogue.HasMessagesWithRewards = false;
                dialogue.OnDialogueAttachmentsChanged.Invoke();
                staleCounters++;
            }
        }

        if (staleCounters > 0)
        {
            Plugin.LogDebug($"Cleared stale attachment counters after server aggregation. staleCounters={staleCounters}");
        }
    }

    private static string GetDialogueIdFromMessage(ChatMessageClass message, SocialNetworkClass socialNetwork)
    {
        if (message.Member != null)
        {
            return message.Member.Id;
        }

        if (message.Type == EMessageType.SystemMessage)
        {
            return socialNetwork.SystemMember.Id;
        }

        return null;
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
