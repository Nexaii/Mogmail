using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mogmail.Models;
using Mogmail.Services;
using Mogmail.UI.Helpers;

namespace Mogmail.UI;

public sealed class ArchiveWindow : Window
{
    private static readonly (string Label, int Value)[] CategoryOptions =
    {
        ("All", -1),
        ("Friends", 0),
        ("GM", 2),
        ("Purchases & Rewards", 1),
    };

    private string _focusedKey = "";
    private readonly HashSet<string> _selected = new();
    private string? _rangeAnchorKey;
    private string? _dragAnchorKey;
    private bool _dragging;
    private bool _confirmReset;
    private bool _confirmEntryDelete;
    private bool _confirmBatchDelete;
    private string _searchFilter = "";
    private int _categoryIndex;

    private string _searchCacheFor = "";
    private int _searchCacheEntryCount = -1;
    private readonly Dictionary<string, string> _searchTextCache = new();

    public ArchiveWindow() : base("Mogmail Archive##MogmailArchive", ImGuiWindowFlags.None)
    {
        Size = new Vector2(800, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 340),
        };
    }

    public override void PreOpenCheck()
    {
        if (!Plugin.Config.EnableArchive)
            IsOpen = false;
    }

    public override void Draw()
    {
        if (!Plugin.Config.EnableArchive) return;

        var footerHeight = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("ArchiveScroll", new Vector2(0, -footerHeight), true))
        {
            DrawTopBar();
            ImGui.Spacing();
            DrawFilterRow();
            ImGui.Spacing();
            DrawSplitView();
        }
        ImGui.EndChild();

        DrawFooter();
    }

    private void DrawFooter()
    {
        var count = Plugin.Instance.Archive.EntryCount;
        if (_selected.Count > 0)
            ImGui.TextUnformatted($"Stored letters: {count:N0}  |  Selected: {_selected.Count:N0}");
        else
            ImGui.TextUnformatted($"Stored letters: {count:N0}");
    }

    private void DrawTopBar()
    {
        if (_confirmReset)
        {
            DrawResetConfirmInline();
            return;
        }

        if (SettingsRows.PrimaryButton("Export All", "Save the currently filtered view to a .md file."))
            OpenExportAllDialog();
        ImGui.SameLine();
        if (SettingsRows.DangerButton("Reset All", "Erase every stored letter for this character."))
            _confirmReset = true;
    }

    private void DrawResetConfirmInline()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorWarning))
            ImGui.TextUnformatted("Reset all for this character?");
        ImGui.SameLine();
        if (SettingsRows.DangerButton("Yes##archive-reset", new Vector2(80, 22)))
        {
            Plugin.Instance.Archive.Reset();
            ClearSelection();
            _confirmReset = false;
        }
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Cancel##archive-reset", new Vector2(80, 22)))
            _confirmReset = false;
    }

    private void DrawFilterRow()
    {
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("##archive-search", "Search sender, date, items, body", ref _searchFilter, 96);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("##archive-category", CategoryOptions[_categoryIndex].Label))
        {
            for (var i = 0; i < CategoryOptions.Length; i++)
            {
                var isSelected = _categoryIndex == i;
                if (ImGui.Selectable(CategoryOptions[i].Label, isSelected))
                    _categoryIndex = i;
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private void DrawSplitView()
    {
        var entries = OrderedEntries();
        var listSize = new Vector2(260f, 0f);
        var detailSize = new Vector2(0f, 0f);

        if (ImGui.BeginChild("ArchiveList", listSize, true))
        {
            DrawListEntries(entries);
            if (entries.Count == 0)
                Theme.HelperText("No entries match the filter.");
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("ArchiveDetail", detailSize, true))
        {
            DrawDetailRegion();
        }
        ImGui.EndChild();
    }

    private void DrawListEntries(List<ArchiveEntry> entries)
    {
        var width = Math.Max(2, entries.Count.ToString().Length);
        var format = "D" + width;
        var io = ImGui.GetIO();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var number = (i + 1).ToString(format);
            var label = $"{number}. {FormatDateShort(entry.Timestamp)} - {entry.Sender}";
            var isSelected = _selected.Contains(entry.Key);

            if (ImGui.Selectable($"{label}##{entry.Key}", isSelected))
                HandleRowClick(entry.Key, entries, io.KeyCtrl, io.KeyShift);

            if (!io.KeyShift && !io.KeyCtrl && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                HandleRowDrag(entry.Key, entries);
        }

        if (_dragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _dragging = false;
    }

    private void HandleRowClick(string key, List<ArchiveEntry> entries, bool ctrl, bool shift)
    {
        _confirmEntryDelete = false;
        _confirmBatchDelete = false;

        if (shift && _rangeAnchorKey != null)
        {
            if (SelectRange(entries, _rangeAnchorKey, key, additive: ctrl))
            {
                _focusedKey = key;
                return;
            }
        }

        if (ctrl)
        {
            if (!_selected.Add(key)) _selected.Remove(key);
            _rangeAnchorKey = key;
            _focusedKey = _selected.Contains(key) ? key : "";
            return;
        }

        _selected.Clear();
        _selected.Add(key);
        _rangeAnchorKey = key;
        _focusedKey = key;
    }

    private void HandleRowDrag(string key, List<ArchiveEntry> entries)
    {
        _confirmEntryDelete = false;
        _confirmBatchDelete = false;

        if (!_dragging)
        {
            _dragging = true;
            _dragAnchorKey = _rangeAnchorKey ?? key;
        }

        if (_dragAnchorKey == null) return;
        if (SelectRange(entries, _dragAnchorKey, key, additive: false))
            _focusedKey = key;
    }

    private bool SelectRange(List<ArchiveEntry> entries, string fromKey, string toKey, bool additive)
    {
        var fromIndex = entries.FindIndex(e => e.Key == fromKey);
        var toIndex = entries.FindIndex(e => e.Key == toKey);
        if (fromIndex < 0 || toIndex < 0) return false;

        var lo = Math.Min(fromIndex, toIndex);
        var hi = Math.Max(fromIndex, toIndex);
        if (!additive) _selected.Clear();
        for (var i = lo; i <= hi; i++)
            _selected.Add(entries[i].Key);
        return true;
    }

    private void DrawDetailRegion()
    {
        if (_selected.Count >= 2)
        {
            DrawBatchPane();
            return;
        }

        if (!Plugin.Instance.Archive.TryGetEntry(_focusedKey, out var selected))
        {
            Theme.HelperText("Pick a letter from the list to view details.");
            return;
        }
        DrawDetailPane(selected);
    }

    private void DrawDetailPane(ArchiveEntry entry)
    {
        ImGui.TextUnformatted($"From: {entry.Sender}");
        ImGui.TextUnformatted($"Date: {FormatTimestamp(entry.Timestamp)}");
        ImGui.TextUnformatted($"Category: {CategoryLabel(entry.Category)}");
        if (entry.Gil > 0)
            ImGui.TextUnformatted($"Gil: {entry.Gil:N0}");

        if (entry.Attachments.Count > 0)
        {
            ImGui.Spacing();
            Theme.HelperText("Attachments");
            foreach (var a in entry.Attachments)
            {
                var name = LookupItemName(a.ItemId);
                ImGui.BulletText(a.Count > 1 ? $"{name} x{a.Count}" : name);
            }
        }

        ImGui.Spacing();
        Theme.HelperText("Body");
        ImGui.TextWrapped(ResolveBodyDisplay(entry));

        ImGui.Spacing();
        Theme.SpacingSeparator();
        DrawEntryActions(entry);
    }

    private void DrawBatchPane()
    {
        var archive = Plugin.Instance.Archive;
        var resolved = _selected
            .Select(k => archive.TryGetEntry(k, out var e) ? e : null)
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        if (resolved.Count == 0)
        {
            ClearSelection();
            Theme.HelperText("Pick a letter from the list to view details.");
            return;
        }

        var visibleKeys = new HashSet<string>(OrderedEntries().Select(e => e.Key));
        var hidden = resolved.Count(e => !visibleKeys.Contains(e.Key));

        ImGui.TextUnformatted($"{resolved.Count:N0} letters selected");
        if (hidden > 0)
            Theme.HelperText($"{hidden:N0} hidden by current filter");

        ImGui.Spacing();
        var totalGil = resolved.Sum(e => (long)e.Gil);
        var totalAttachments = resolved.Sum(e => e.Attachments.Sum(a => (long)a.Count));
        var senderCount = resolved.Select(e => e.Sender).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var minTs = resolved.Min(e => e.Timestamp);
        var maxTs = resolved.Max(e => e.Timestamp);

        if (totalGil > 0)
            ImGui.TextUnformatted($"Total gil: {totalGil:N0}");
        if (totalAttachments > 0)
            ImGui.TextUnformatted($"Total attachments: {totalAttachments:N0}");
        ImGui.TextUnformatted($"Senders: {senderCount:N0}");
        ImGui.TextUnformatted($"Range: {FormatDateShort(minTs)} to {FormatDateShort(maxTs)}");

        ImGui.Spacing();
        Theme.SpacingSeparator();

        if (SettingsRows.PrimaryButton("Export Selected", "Save selected letters to a .md file."))
            OpenExportSelectedDialog(resolved);
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Clear Selection"))
            ClearSelection();
        ImGui.Spacing();
        DrawBatchDeleteControls(resolved.Count);
    }

    private void DrawBatchDeleteControls(int count)
    {
        if (!_confirmBatchDelete)
        {
            if (SettingsRows.DangerButton("Delete Selected", "Remove all selected entries from the local archive."))
                _confirmBatchDelete = true;
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorWarning))
            ImGui.TextWrapped($"Delete {count:N0} selected entries?");

        if (SettingsRows.DangerButton("Yes##batch-del", new Vector2(80, 24)))
        {
            var keys = _selected.ToArray();
            Plugin.Instance.Archive.DeleteEntries(keys);
            ClearSelection();
            _confirmBatchDelete = false;
        }
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Cancel##batch-del", new Vector2(80, 24)))
            _confirmBatchDelete = false;
    }

    private void DrawEntryActions(ArchiveEntry entry)
    {
        if (SettingsRows.PrimaryButton("Export", "Save this single letter to a .md file."))
            OpenSingleExportDialog(entry);
        ImGui.Spacing();
        DrawEntryDeleteControls(entry.Key);
    }

    private void DrawEntryDeleteControls(string key)
    {
        if (!_confirmEntryDelete)
        {
            if (SettingsRows.DangerButton("Delete from archive", "Remove this entry from the local archive file."))
                _confirmEntryDelete = true;
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorWarning))
            ImGui.TextWrapped("Delete this entry?");

        if (SettingsRows.DangerButton("Yes##entry-del", new Vector2(80, 24)))
        {
            Plugin.Instance.Archive.DeleteEntry(key);
            _selected.Remove(key);
            if (_focusedKey == key) _focusedKey = "";
            if (_rangeAnchorKey == key) _rangeAnchorKey = null;
            _confirmEntryDelete = false;
        }
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Cancel##entry-del", new Vector2(80, 24)))
            _confirmEntryDelete = false;
    }

    private void ClearSelection()
    {
        _selected.Clear();
        _focusedKey = "";
        _rangeAnchorKey = null;
        _dragAnchorKey = null;
        _dragging = false;
        _confirmEntryDelete = false;
        _confirmBatchDelete = false;
    }

    private static string ResolveBodyDisplay(ArchiveEntry entry)
    {
        var cleaned = SanitizeForDisplay(entry.Body);
        if (!string.IsNullOrEmpty(cleaned)) return cleaned;
        if (entry.Read) return "(empty)";
        return "(unread - open letter in game to capture body)";
    }

    private static string SanitizeForDisplay(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new System.Text.StringBuilder(raw.Length);
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

    private List<ArchiveEntry> OrderedEntries()
    {
        var filter = _searchFilter.Trim();
        var lowered = filter.ToLowerInvariant();
        var categoryFilter = CategoryOptions[_categoryIndex].Value;

        EnsureSearchCacheCurrent();

        IEnumerable<ArchiveEntry> source = Plugin.Instance.Archive.SnapshotEntries();
        if (categoryFilter >= 0)
            source = source.Where(e => e.Category == categoryFilter);
        if (lowered.Length > 0)
            source = source.Where(e => _searchTextCache.TryGetValue(e.Key, out var text) && text.Contains(lowered));
        return source.OrderByDescending(e => e.Timestamp).ToList();
    }

    private void EnsureSearchCacheCurrent()
    {
        var entries = Plugin.Instance.Archive.SnapshotEntries();
        var characterKey = Plugin.Instance.Archive.LoadedContentId.ToString("X");
        if (_searchCacheFor == characterKey && _searchCacheEntryCount == entries.Count) return;
        _searchTextCache.Clear();
        foreach (var entry in entries)
            _searchTextCache[entry.Key] = BuildSearchableText(entry);
        _searchCacheFor = characterKey;
        _searchCacheEntryCount = entries.Count;
    }

    private static string BuildSearchableText(ArchiveEntry entry)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(entry.Sender).Append(' ');
        sb.Append(entry.Body).Append(' ');
        sb.Append(FormatTimestamp(entry.Timestamp)).Append(' ');
        foreach (var a in entry.Attachments)
        {
            sb.Append(LookupItemName(a.ItemId)).Append(' ');
        }
        return sb.ToString().ToLowerInvariant();
    }

    private void OpenExportAllDialog()
    {
        var character = SanitizeFileNameComponent(Plugin.Instance.Archive.CurrentCharacterLabel());
        var defaultName = $"mogmail-archive-{character}-{DateTime.Now:yyyy-MM-dd}";
        Plugin.FileDialogManager.SaveFileDialog(
            "Export Mogmail Archive",
            "Markdown{.md}",
            defaultName,
            ".md",
            (success, path) =>
            {
                if (!success || string.IsNullOrEmpty(path)) return;
                WriteExportAll(EnsureMarkdownExtension(path));
            });
    }

    private void OpenSingleExportDialog(ArchiveEntry entry)
    {
        var character = SanitizeFileNameComponent(Plugin.Instance.Archive.CurrentCharacterLabel());
        var senderTag = SanitizeFileNameComponent(entry.Sender);
        var stamp = FormatTimestampForFilename(entry.Timestamp);
        var defaultName = $"mogmail-letter-{character}-{senderTag}-{stamp}";
        var key = entry.Key;
        Plugin.FileDialogManager.SaveFileDialog(
            "Export Letter",
            "Markdown{.md}",
            defaultName,
            ".md",
            (success, path) =>
            {
                if (!success || string.IsNullOrEmpty(path)) return;
                WriteSingleExport(key, EnsureMarkdownExtension(path));
            });
    }

    private void OpenExportSelectedDialog(List<ArchiveEntry> selected)
    {
        var character = SanitizeFileNameComponent(Plugin.Instance.Archive.CurrentCharacterLabel());
        var defaultName = $"mogmail-selection-{character}-{DateTime.Now:yyyy-MM-dd}-{selected.Count}entries";
        var snapshot = selected.OrderByDescending(e => e.Timestamp).ToList();
        Plugin.FileDialogManager.SaveFileDialog(
            "Export Selected Letters",
            "Markdown{.md}",
            defaultName,
            ".md",
            (success, path) =>
            {
                if (!success || string.IsNullOrEmpty(path)) return;
                WriteSelectedExport(snapshot, EnsureMarkdownExtension(path));
            });
    }

    private void WriteExportAll(string path)
    {
        var entries = OrderedEntries();
        if (entries.Count == 0)
        {
            Plugin.Chat.Print("[Mogmail] no entries to export.");
            return;
        }

        var filterDescription = BuildFilterDescription();
        var markdown = Plugin.Instance.Archive.BuildMarkdownExport(entries, filterDescription, LookupItemName);
        try
        {
            File.WriteAllText(path, markdown);
            Plugin.Chat.Print($"[Mogmail] exported {entries.Count} entries to {path}");
        }
        catch (Exception ex)
        {
            Plugin.Chat.Print($"[Mogmail] export failed: {ex.Message}");
        }
    }

    private void WriteSingleExport(string key, string path)
    {
        if (!Plugin.Instance.Archive.TryGetEntry(key, out var entry))
        {
            Plugin.Chat.Print("[Mogmail] export failed: entry not found.");
            return;
        }
        var markdown = Plugin.Instance.Archive.BuildSingleEntryMarkdown(entry, LookupItemName);
        try
        {
            File.WriteAllText(path, markdown);
            Plugin.Chat.Print($"[Mogmail] exported 1 entry to {path}");
        }
        catch (Exception ex)
        {
            Plugin.Chat.Print($"[Mogmail] export failed: {ex.Message}");
        }
    }

    private void WriteSelectedExport(List<ArchiveEntry> entries, string path)
    {
        if (entries.Count == 0)
        {
            Plugin.Chat.Print("[Mogmail] no entries to export.");
            return;
        }
        var markdown = Plugin.Instance.Archive.BuildMarkdownExport(entries, $"selection ({entries.Count})", LookupItemName);
        try
        {
            File.WriteAllText(path, markdown);
            Plugin.Chat.Print($"[Mogmail] exported {entries.Count} entries to {path}");
        }
        catch (Exception ex)
        {
            Plugin.Chat.Print($"[Mogmail] export failed: {ex.Message}");
        }
    }

    private static string EnsureMarkdownExtension(string path)
        => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path : path + ".md";

    private static string FormatTimestampForFilename(uint epochSeconds)
    {
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).LocalDateTime;
            return dt.ToString("yyyy-MM-dd-HHmm");
        }
        catch
        {
            return epochSeconds.ToString();
        }
    }

    private string BuildFilterDescription()
    {
        var parts = new List<string>();
        var category = CategoryOptions[_categoryIndex];
        if (category.Value >= 0) parts.Add($"category={category.Label}");
        var search = _searchFilter.Trim();
        if (search.Length > 0) parts.Add($"search=\"{search}\"");
        return string.Join(", ", parts);
    }

    private static string SanitizeFileNameComponent(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "character";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (Array.IndexOf(invalid, ch) >= 0) { sb.Append('_'); continue; }
            if (ch == ' ' || ch == '@') { sb.Append('_'); continue; }
            sb.Append(ch);
        }
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? "character" : result;
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

    private static string FormatDateShort(uint epochSeconds)
    {
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).LocalDateTime;
            return dt.ToString("dd/MM/yy HH:mm");
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

    private static string LookupItemName(uint itemId) => ItemNameResolver.Resolve(itemId);
}
