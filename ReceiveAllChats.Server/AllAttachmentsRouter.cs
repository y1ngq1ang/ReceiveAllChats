using ReceiveAllChats.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Utils;

namespace ReceiveAllChats.Server;
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class AllAttachmentsRouter(
    JsonUtil jsonUtil,
    HttpResponseUtil httpResponseUtil,
    AllAttachmentsService allAttachmentsService)
    : StaticRouter(jsonUtil,
        [
            new RouteAction<GetAllAttachmentsRequestData>(
                ReceiveAllChatsProtocol.GetAllAttachmentsRoute,
                async (_, _, sessionId, _, _) =>
                    httpResponseUtil.GetBody(allAttachmentsService.CollectAllAttachments(sessionId))),
        ])
{
}
