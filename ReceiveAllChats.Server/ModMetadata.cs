using ReceiveAllChats.Shared;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ReceiveAllChats.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = ReceiveAllChatsProtocol.ModGuid;
    public string Name { get; init; } = ReceiveAllChatsProtocol.ModName;
    public string Author { get; init; } = "maschine";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(ReceiveAllChatsProtocol.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new($"~{ReceiveAllChatsProtocol.SptVersion}");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/maschine34675/ReceiveAllChats";
    public string License { get; init; } = "MIT";
}
