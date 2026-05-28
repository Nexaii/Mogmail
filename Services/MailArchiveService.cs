using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Mogmail.Models;

namespace Mogmail.Services;

public sealed unsafe class MailArchiveService : IDisposable
{
    private const int DefaultRetention = 5000;
    private const long SaveDebounceMs = 1000;
    private const int DisposeFlushTimeoutMs = 2000;
    private const int LogoutFlushTimeoutMs = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Dictionary<string, ArchiveEntry> _entries = new();
    private ulong _loadedContentId;

    private readonly object _writeLock = new();
    private string? _pendingPayload;
    private string? _pendingPath;
    private Task? _writerTask;

    private int _dirty;
    private long _lastSaveMs;

    public IReadOnlyDictionary<string, ArchiveEntry> Entries => _entries;
    public ulong LoadedContentId => _loadedContentId;

    public MailArchiveService()
    {
        Plugin.ClientState.Login += OnLogin;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.Instance.Mailbox.NewLetterObserved += OnNewLetterObserved;
        Plugin.Instance.Mailbox.LetterDetailReceived += OnLetterDetailReceived;
        Plugin.Framework.Update += OnFrameworkUpdate;

        if (Plugin.ClientState.IsLoggedIn)
            EnsureLoaded();
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.Instance.Mailbox.LetterDetailReceived -= OnLetterDetailReceived;
        Plugin.Instance.Mailbox.NewLetterObserved -= OnNewLetterObserved;
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ClientState.Logout -= OnLogout;
        FlushIfDirty();
        WaitForPendingWrite(DisposeFlushTimeoutMs);
    }

    public void RecordTake(LetterSnapshot snapshot)
    {
        if (!Plugin.Config.EnableArchive) return;
        EnsureLoaded();
        if (_loadedContentId == 0) return;
        UpsertFromSnapshot(snapshot);
    }

    public void Reset()
    {
        if (_loadedContentId == 0) return;
        _entries.Clear();
        MarkDirty();
        Save();
    }

    public bool DeleteEntry(string key)
    {
        if (_loadedContentId == 0) return false;
        if (!_entries.Remove(key)) return false;
        MarkDirty();
        Save();
        return true;
    }

    public string CurrentCharacterLabel()
    {
        var lp = Plugin.ObjectTable.LocalPlayer;
        if (lp == null) return "(unknown)";
        var name = lp.Name.TextValue;
        var world = "";
        try { world = lp.HomeWorld.Value.Name.ExtractText(); } catch { }
        return string.IsNullOrEmpty(world) ? name : $"{name} @ {world}";
    }

    public string BuildMarkdownExport(IReadOnlyList<ArchiveEntry> entries, string filterDescription, Func<uint, string> itemNameLookup)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Mogmail Archive Export");
        sb.AppendLine();
        sb.Append("- Character: ").AppendLine(CurrentCharacterLabel());
        sb.Append("- Exported: ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")).AppendLine(" UTC");
        sb.Append("- Entries: ").AppendLine(entries.Count.ToString());
        sb.Append("- Filter: ").AppendLine(string.IsNullOrEmpty(filterDescription) ? "(none)" : filterDescription);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            sb.Append("## ").AppendLine(string.IsNullOrEmpty(entry.Sender) ? "(unknown sender)" : entry.Sender);
            sb.AppendLine();
            sb.Append("- Date: ").AppendLine(FormatTimestamp(entry.Timestamp));
            sb.Append("- Category: ").AppendLine(CategoryLabel(entry.Category));
            if (entry.Gil > 0)
                sb.Append("- Gil: ").AppendLine(entry.Gil.ToString("N0"));
            if (entry.Attachments.Count > 0)
            {
                sb.AppendLine("- Attachments:");
                foreach (var a in entry.Attachments)
                {
                    var name = itemNameLookup(a.ItemId);
                    sb.Append("  - ").Append(name);
                    if (a.Count > 1) sb.Append(" x ").Append(a.Count);
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
            sb.AppendLine("```");
            var cleaned = SanitizeBodyForExport(entry.Body);
            if (string.IsNullOrEmpty(cleaned))
            {
                sb.AppendLine(entry.Read ? "(empty)" : "(unread - body not captured)");
            }
            else
            {
                sb.AppendLine(cleaned);
            }
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string BuildSingleEntryMarkdown(ArchiveEntry entry, Func<uint, string> itemNameLookup)
    {
        var list = new List<ArchiveEntry> { entry };
        return BuildMarkdownExport(list, "single entry", itemNameLookup);
    }

    private static string SanitizeBodyForExport(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch == '\n' || ch == '\r' || ch == '\t') { sb.Append(ch); continue; }
            if (ch < 0x20) continue;
            if (ch == 0x7F) continue;
            if (ch == '�') continue;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private static string FormatTimestamp(uint epochSeconds)
    {
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            return epochSeconds.ToString();
        }
    }

    private static string CategoryLabel(byte category) => category switch
    {
        0 => "Friends",
        1 => "Purchases & Rewards",
        2 => "GM",
        _ => $"Other ({category})",
    };

    private void OnLogin() => EnsureLoaded();

    private void OnLogout(int type, int code)
    {
        FlushIfDirty();
        WaitForPendingWrite(LogoutFlushTimeoutMs);
        _entries.Clear();
        _loadedContentId = 0;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsDirty()) return;
        if (Environment.TickCount64 - Volatile.Read(ref _lastSaveMs) < SaveDebounceMs) return;
        Save();
    }

    private void OnNewLetterObserved(int index)
    {
        if (!Plugin.Config.EnableArchive) return;
        EnsureLoaded();
        if (_loadedContentId == 0) return;
        if (!Plugin.Instance.Mailbox.TrySnapshotLetter(index, out var snapshot)) return;
        UpsertFromSnapshot(snapshot);
    }

    private void OnLetterDetailReceived(string body, ulong senderContentId, uint timestamp)
    {
        if (!Plugin.Config.EnableArchive) return;
        EnsureLoaded();
        if (_loadedContentId == 0) return;
        var key = MakeKey(senderContentId, timestamp);
        if (!_entries.TryGetValue(key, out var entry)) return;
        entry.Body = body;
        entry.BodyCapturedUtc = DateTime.UtcNow.ToString("o");
        entry.Read = true;
        MarkDirty();
    }

    private void EnsureLoaded()
    {
        var cid = CurrentContentId();
        if (cid == 0) return;
        if (_loadedContentId == cid) return;
        FlushIfDirty();
        WaitForPendingWrite(LogoutFlushTimeoutMs);
        _loadedContentId = cid;
        _entries.Clear();
        var loaded = Load(cid);
        if (loaded != null)
            foreach (var entry in loaded)
                _entries[entry.Key] = entry;
    }

    private void UpsertFromSnapshot(LetterSnapshot snapshot)
    {
        var key = MakeKey(snapshot.SenderContentId, snapshot.Timestamp);
        var sanitizedPreview = SanitizePreview(snapshot.Preview);

        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Read = snapshot.Read;
            if (existing.Attachments.Count == 0 && snapshot.Attachments.Count > 0)
                ReplaceAttachments(existing, snapshot);
            if (existing.Gil == 0 && snapshot.Gil > 0)
                existing.Gil = snapshot.Gil;
            if (string.IsNullOrEmpty(existing.Preview) && !string.IsNullOrEmpty(sanitizedPreview))
                existing.Preview = sanitizedPreview;
            MarkDirty();
            return;
        }

        var entry = new ArchiveEntry
        {
            Key = key,
            SenderContentId = snapshot.SenderContentId,
            Timestamp = snapshot.Timestamp,
            Sender = snapshot.Sender,
            Preview = sanitizedPreview,
            Category = snapshot.Category,
            Read = snapshot.Read,
            Gil = snapshot.Gil,
            CapturedUtc = DateTime.UtcNow.ToString("o"),
        };
        ReplaceAttachments(entry, snapshot);
        _entries[key] = entry;
        EnforceRetention();
        MarkDirty();
    }

    private static string SanitizePreview(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch == '\n' || ch == '\r' || ch == '\t') { sb.Append(ch); continue; }
            if (ch < 0x20) continue;
            if (ch == 0x7F) continue;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private static void ReplaceAttachments(ArchiveEntry entry, LetterSnapshot snapshot)
    {
        entry.Attachments.Clear();
        foreach (var a in snapshot.Attachments)
            entry.Attachments.Add(new ArchiveAttachment { ItemId = a.ItemId, Count = a.Count });
    }

    private void EnforceRetention()
    {
        if (_entries.Count <= DefaultRetention) return;
        var excess = _entries.Count - DefaultRetention;
        var ordered = new List<KeyValuePair<string, ArchiveEntry>>(_entries);
        ordered.Sort((a, b) => a.Value.Timestamp.CompareTo(b.Value.Timestamp));
        for (var i = 0; i < excess; i++)
            _entries.Remove(ordered[i].Key);
    }

    private static string MakeKey(ulong contentId, uint timestamp) => $"{contentId:X}:{timestamp}";

    private bool IsDirty() => Volatile.Read(ref _dirty) != 0;
    private void MarkDirty() => Volatile.Write(ref _dirty, 1);
    private void ClearDirty() => Volatile.Write(ref _dirty, 0);

    private static ulong CurrentContentId()
    {
        var state = PlayerState.Instance();
        return state == null ? 0UL : state->ContentId;
    }

    private static string DirectoryPath()
    {
        var dir = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "archive");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FilePath(ulong contentId)
        => Path.Combine(DirectoryPath(), $"{contentId:X}.json");

    private static List<ArchiveEntry>? Load(ulong contentId)
    {
        var path = FilePath(contentId);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ArchiveEntry>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            MogLog.Warning($"[Mogmail] archive load failed for {contentId:X}: {ex.Message}");
            return null;
        }
    }

    private void Save()
    {
        if (_loadedContentId == 0) return;
        string json;
        string path;
        try
        {
            path = FilePath(_loadedContentId);
            var list = new List<ArchiveEntry>(_entries.Values);
            json = JsonSerializer.Serialize(list, JsonOptions);
        }
        catch (Exception ex)
        {
            MogLog.Warning($"[Mogmail] archive serialize failed for {_loadedContentId:X}: {ex.Message}");
            return;
        }

        lock (_writeLock)
        {
            _pendingPayload = json;
            _pendingPath = path;
            ClearDirty();
            if (_writerTask == null || _writerTask.IsCompleted)
                _writerTask = Task.Run(WriteWorker);
        }
    }

    private void WriteWorker()
    {
        while (true)
        {
            string? payload;
            string? path;
            lock (_writeLock)
            {
                payload = _pendingPayload;
                path = _pendingPath;
                _pendingPayload = null;
                _pendingPath = null;
            }

            if (payload == null || path == null) break;

            try
            {
                File.WriteAllText(path, payload);
                Volatile.Write(ref _lastSaveMs, Environment.TickCount64);
            }
            catch (Exception ex)
            {
                MogLog.Warning($"[Mogmail] archive write failed for {path}: {ex.Message}");
                MarkDirty();
            }

            lock (_writeLock)
            {
                if (_pendingPayload == null)
                {
                    _writerTask = null;
                    return;
                }
            }
        }
    }

    private void WaitForPendingWrite(int timeoutMs)
    {
        Task? task;
        lock (_writeLock)
        {
            task = _writerTask;
        }
        if (task == null) return;
        try
        {
            if (!task.Wait(timeoutMs))
                MogLog.Warning($"[Mogmail] archive write did not complete within {timeoutMs}ms. State may lag on disk.");
        }
        catch (Exception ex)
        {
            MogLog.Warning($"[Mogmail] archive flush wait threw: {ex.Message}");
        }
    }

    private void FlushIfDirty()
    {
        if (!IsDirty()) return;
        Save();
    }
}
