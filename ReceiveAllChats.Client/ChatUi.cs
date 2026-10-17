using EFT;
using System;

namespace ReceiveAllChats;

/// <summary>
/// Finds the live chat UI objects. IL2CPP builds expose instance fields as properties,
/// so patches can no longer have the social network injected into them and have to look
/// it up from the UI singletons instead.
/// </summary>
internal static class ChatUi
{
    public static EFT.UI.Chat.ChatScreen GetChatScreen()
    {
        try
        {
            return MonoBehaviourSingleton<EFT.UI.CommonUI>.Instance?.ChatScreen;
        }
        catch (Exception exception)
        {
            Plugin.LogDebug($"Chat screen is not available yet. error={exception.Message}");
            return null;
        }
    }

    public static SocialNetwork GetSocialNetwork() => GetChatScreen()?._social;
}
