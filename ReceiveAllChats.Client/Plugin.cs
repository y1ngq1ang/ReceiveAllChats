using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ReceiveAllChats.Patches;
using ReceiveAllChats.Shared;
using System;
using System.IO;

namespace ReceiveAllChats;

[BepInPlugin(ReceiveAllChatsProtocol.ModGuid, ReceiveAllChatsProtocol.PluginName, ReceiveAllChatsProtocol.Version)]
public class Plugin : BaseUnityPlugin
{
    private const string Version = ReceiveAllChatsProtocol.Version;
    private static readonly object DebugLogLock = new();

    internal static ManualLogSource Log { get; private set; }
    internal static ConfigEntry<bool> DebugLogging { get; private set; }
    internal static ConfigEntry<bool> DebugFileLogging { get; private set; }
    internal static ConfigEntry<bool> MirrorDebugLoggingToBepInExLog { get; private set; }
    internal static ConfigEntry<int> RequestTimeoutSeconds { get; private set; }
    internal static ConfigEntry<int> MaxConcurrentAttachmentRequests { get; private set; }
    internal static ConfigEntry<bool> UseServerAggregationRequest { get; private set; }
    internal static string DebugLogPath { get; private set; }

    private void Awake()
    {
        Log = Logger;
        DebugLogPath = Path.Combine(BepInEx.Paths.ConfigPath, "ReceiveAllChats-debug.log");
        DebugLogging = Config.Bind(
            "Debug",
            "EnableDebugLogging",
            false,
            AdvancedDescription("Enable detailed receive-all diagnostics. Disabled by default for release builds."));
        DebugFileLogging = Config.Bind(
            "Debug",
            "EnableDebugFileLogging",
            false,
            AdvancedDescription("Write detailed diagnostics to a separate ReceiveAllChats-debug.log file when debug logging is enabled."));
        MirrorDebugLoggingToBepInExLog = Config.Bind(
            "Debug",
            "MirrorDebugLoggingToBepInExLog",
            false,
            AdvancedDescription("Also write debug diagnostics to the main BepInEx log. Usually not needed when sharing the separate debug file."));
        RequestTimeoutSeconds = Config.Bind(
            "Debug",
            "RequestTimeoutSeconds",
            15,
            AdvancedDescription("How long to wait for each messenger request before skipping it and logging a timeout."));
        MaxConcurrentAttachmentRequests = Config.Bind(
            "Debug",
            "MaxConcurrentAttachmentRequests",
            4,
            AdvancedDescription("Maximum number of attachment requests sent at the same time. Lower values are slower but more reliable."));
        UseServerAggregationRequest = Config.Bind(
            "Debug",
            "UseServerAggregationRequest",
            true,
            AdvancedDescription("Ask the optional ReceiveAllChats.Server mod for all attachments in a single request. Falls back to per-dialogue scanning when the server mod is not installed."));

        StartDebugLog();

        try
        {
            new DialogueListUpdatingPatch().Enable();
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

    internal static void LogDebug(string message)
    {
        if (DebugLogging?.Value != true)
        {
            return;
        }

        if (DebugFileLogging?.Value == true)
        {
            WriteDebugFile($"DEBUG {message}");
        }

        if (MirrorDebugLoggingToBepInExLog?.Value == true)
        {
            Log?.LogInfo($"[Debug] {message}");
        }
    }

    internal static void LogWarning(string message)
    {
        if (DebugLogging?.Value == true && DebugFileLogging?.Value == true)
        {
            WriteDebugFile($"WARN  {message}");
        }

        Log?.LogWarning(message);
    }

    private static void StartDebugLog()
    {
        if (DebugLogging?.Value != true || DebugFileLogging?.Value != true)
        {
            return;
        }

        try
        {
            var header =
                $"ReceiveAllChats debug log started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"version={Version} timeoutSeconds={TimeoutMilliseconds / 1000} maxConcurrentRequests={MaxConcurrentRequests}\n";
            File.WriteAllText(DebugLogPath, header);
            Log?.LogInfo($"ReceiveAllChats debug logging enabled: {DebugLogPath}");
        }
        catch (Exception exception)
        {
            Log?.LogWarning($"ReceiveAllChats could not create debug log file: {exception}");
        }
    }

    private static void WriteDebugFile(string message)
    {
        if (DebugFileLogging?.Value != true || string.IsNullOrEmpty(DebugLogPath))
        {
            return;
        }

        try
        {
            lock (DebugLogLock)
            {
                File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception exception)
        {
            Log?.LogWarning($"ReceiveAllChats could not write to debug log file: {exception.Message}");
        }
    }

    internal static int TimeoutMilliseconds =>
        Math.Max(1, RequestTimeoutSeconds?.Value ?? 15) * 1000;

    internal static int MaxConcurrentRequests =>
        Math.Max(1, MaxConcurrentAttachmentRequests?.Value ?? 4);

    internal static bool UseServerAggregation =>
        UseServerAggregationRequest?.Value != false;

    private static ConfigDescription AdvancedDescription(string description) =>
        new(description, null, new ConfigurationManagerAttributes { IsAdvanced = true });
}
