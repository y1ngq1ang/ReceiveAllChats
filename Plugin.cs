using BepInEx;
using ReceiveAllChats.Patches;

namespace ReceiveAllChats;

[BepInPlugin("com.maschine.ReceiveAllChats", "maschine-ReceiveAllChats", "1.0.1")]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        try
        {
            new ReceiveAllFromAllChatsPatch().Enable();
            new ReceiveAllButtonVisibilityPatch().Enable();
            new ReceiveAllButtonVisibilityPatch.OnGlobalMessagePatch().Enable();
            new ReceiveAllButtonVisibilityPatch.UpdatePatch().Enable();
            new TransferItemsClosePatch().Enable();
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"ReceiveAllChats failed to apply patches, the mod will not work: {exception}");
        }
    }
}
