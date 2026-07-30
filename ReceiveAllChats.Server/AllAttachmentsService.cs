using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace ReceiveAllChats.Server;

[Injectable(InjectionType.Singleton)]
public class AllAttachmentsService(DialogueHelper dialogueHelper, TimeUtil timeUtil)
{
    public GetAllAttachmentsResponse CollectAllAttachments(MongoId sessionId)
    {
        var timeNow = timeUtil.GetTimeStamp();
        var dialogues = dialogueHelper.GetDialogsForProfile(sessionId);

        var messages = dialogues.Values
            .Where(dialogue => dialogue.Type != MessageType.GlobalChat)
            .SelectMany(dialogue => dialogue.Messages ?? [])
            .Where(message => timeNow < message.DateTime + (message.MaxStorageTime ?? 0))
            .Where(message => (message.Items?.Data?.Count ?? 0) > 0)
            .ToList();

        return new GetAllAttachmentsResponse
        {
            Messages = messages,
            Profiles = [],
            HasMessagesWithRewards = messages.Count > 0,
        };
    }
}
