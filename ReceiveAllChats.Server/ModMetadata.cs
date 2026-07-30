using ReceiveAllChats.Shared;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ReceiveAllChats.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = ReceiveAllChatsProtocol.ModGuid;
    public override string Name { get; init; } = ReceiveAllChatsProtocol.ModName;
    public override string Author { get; init; } = "maschine";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new(ReceiveAllChatsProtocol.Version);
    public override SemanticVersioning.Range SptVersion { get; init; } = new($"~{ReceiveAllChatsProtocol.SptVersion}");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/maschine34675/ReceiveAllChats";
#pragma warning disable CS8764
    public override bool? IsBundleMod { get; init; } = false;
#pragma warning restore CS8764
    public override string License { get; init; } = "MIT";
}
