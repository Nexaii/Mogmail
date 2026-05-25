using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Mogmail.Services;

public sealed class MailRejectionWatcher : IDisposable
{
    private static readonly string[] RejectionEnglishSubstrings =
    {
        "Unable to process mail command",
        "Unable to retrieve mail",
        "Unable to send mail",
        "Cannot take attachments at this time",
        "Cannot use that command at this time",
    };

    private readonly IChatGui _chatGui;
    private readonly Dictionary<uint, string> _rejectionIdToEnglishText;

    public event Action<string>? RejectionDetected;

    public MailRejectionWatcher(IChatGui chatGui)
    {
        _chatGui = chatGui;
        _rejectionIdToEnglishText = BuildRejectionIdMap();
        _chatGui.LogMessage += OnLogMessage;
    }

    public void Dispose()
    {
        _chatGui.LogMessage -= OnLogMessage;
    }

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            if (!_rejectionIdToEnglishText.TryGetValue(message.LogMessageId, out var englishText)) return;
            RejectionDetected?.Invoke(englishText);
        }
        catch (Exception ex)
        {
            try { MogLog.Error($"[Mogmail] MailRejectionWatcher exception: {ex}"); } catch { }
        }
    }

    private static Dictionary<uint, string> BuildRejectionIdMap()
    {
        var map = new Dictionary<uint, string>();
        var sheet = Plugin.Data.GetExcelSheet<LogMessage>(ClientLanguage.English);
        if (sheet == null)
        {
            MogLog.Warning("[Mogmail] mail rejection watcher: English LogMessage sheet unavailable. Rejection detection disabled.");
            return map;
        }

        foreach (var row in sheet)
        {
            var text = row.Text.ExtractText();
            if (string.IsNullOrEmpty(text)) continue;

            foreach (var needle in RejectionEnglishSubstrings)
            {
                if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                map[row.RowId] = needle;
                break;
            }
        }

        if (map.Count == 0)
            MogLog.Warning("[Mogmail] mail rejection watcher: zero rejection LogMessage rows identified. Patterns may have changed.");

        return map;
    }
}
