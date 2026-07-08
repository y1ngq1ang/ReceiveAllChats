using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ReceiveAllChats.Patches;
using System;
using System.IO;

namespace ReceiveAllChats;

[BepInPlugin("com.maschine.ReceiveAllChats", "maschine-ReceiveAllChats", "1.0.6")]
public class Plugin : BaseUnityPlugin
{
    private static readonly object DebugLogLock = new();

    internal static ManualLogSource Log { get; private set; }
    internal static ConfigEntry<bool> DebugLogging { get; private set; }
    internal static ConfigEntry<bool> DebugFileLogging { get; private set; }
    internal static ConfigEntry<bool> MirrorDebugLoggingToBepInExLog { get; private set; }
    internal static ConfigEntry<int> RequestTimeoutSeconds { get; private set; }
    internal static ConfigEntry<int> DialogueListIdleGraceMilliseconds { get; private set; }
    internal static ConfigEntry<int> MaxConcurrentAttachmentRequests { get; private set; }
    internal static string DebugLogPath { get; private set; }

    private void Awake()
    {
        Log = Logger;
        DebugLogPath = Path.Combine(BepInEx.Paths.ConfigPath, "ReceiveAllChats-debug.log");
        DebugLogging = Config.Bind(
            "Debug",
            "EnableDebugLogging",
            true,
            "Legacy debug toggle. Keep enabled unless asked otherwise.");
        DebugFileLogging = Config.Bind(
            "Debug",
            "EnableDebugFileLogging",
            true,
            "Write detailed receive-all diagnostics to a separate ReceiveAllChats-debug.log file.");
        MirrorDebugLoggingToBepInExLog = Config.Bind(
            "Debug",
            "MirrorDebugLoggingToBepInExLog",
            false,
            "Also write debug diagnostics to the main BepInEx log. Usually not needed when sharing the separate debug file.");
        RequestTimeoutSeconds = Config.Bind(
            "Advanced",
            "RequestTimeoutSeconds",
            15,
            "How long to wait for each messenger request before skipping it and logging a timeout.");
        DialogueListIdleGraceMilliseconds = Config.Bind(
            "Advanced",
            "DialogueListIdleGraceMilliseconds",
            1000,
            "How long to wait for an already-running dialogue-list update before forcing additional dialogue pages to load.");
        MaxConcurrentAttachmentRequests = Config.Bind(
            "Advanced",
            "MaxConcurrentAttachmentRequests",
            4,
            "Maximum number of attachment requests sent at the same time. Lower values are slower but more reliable.");

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
        if (DebugLogging?.Value == true)
        {
            if (DebugFileLogging?.Value == true)
            {
                WriteDebugFile($"DEBUG {message}");
            }

            if (MirrorDebugLoggingToBepInExLog?.Value == true)
            {
                Log?.LogInfo($"[Debug] {message}");
            }
        }
        else if (DebugFileLogging?.Value == true)
        {
            WriteDebugFile($"DEBUG {message}");
        }
    }

    internal static void LogWarning(string message)
    {
        WriteDebugFile($"WARN  {message}");
        Log?.LogWarning(message);
    }

    private static void StartDebugLog()
    {
        if (DebugFileLogging?.Value != true)
        {
            return;
        }

        try
        {
            var header =
                $"ReceiveAllChats debug log started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"version=1.0.6 timeoutSeconds={TimeoutMilliseconds / 1000} idleGraceMs={IdleGraceMilliseconds} maxConcurrentRequests={MaxConcurrentRequests}\n";
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

    internal static int IdleGraceMilliseconds =>
        Math.Max(0, DialogueListIdleGraceMilliseconds?.Value ?? 1000);

    internal static int MaxConcurrentRequests =>
        Math.Max(1, MaxConcurrentAttachmentRequests?.Value ?? 4);
}
