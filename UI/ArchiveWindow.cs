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

    private string _selectedKey = "";
    private bool _confirmReset;
    private bool _confirmEntryDelete;
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

    private static void DrawFooter()
    {
        var count = Plugin.Instance.Archive.Entries.Count;
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
            _selectedKey = "";
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
            foreach (var entry in entries)
            {
                var label = $"{FormatTimestamp(entry.Timestamp)} - {entry.Sender}";
                if (ImGui.Selectable($"{label}##{entry.Key}", _selectedKey == entry.Key))
                {
                    _selectedKey = entry.Key;
                    _confirmEntryDelete = false;
                }
            }
            if (entries.Count == 0)
                Theme.HelperText("No entries match the filter.");
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("ArchiveDetail", detailSize, true))
        {
            if (!Plugin.Instance.Archive.Entries.TryGetValue(_selectedKey, out var selected))
                Theme.HelperText("Pick a letter from the list to view details.");
            else
                DrawDetailPane(selected);
        }
        ImGui.EndChild();
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
            _selectedKey = "";
            _confirmEntryDelete = false;
        }
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Cancel##entry-del", new Vector2(80, 24)))
            _confirmEntryDelete = false;
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

        IEnumerable<ArchiveEntry> source = Plugin.Instance.Archive.Entries.Values;
        if (categoryFilter >= 0)
            source = source.Where(e => e.Category == categoryFilter);
        if (lowered.Length > 0)
            source = source.Where(e => _searchTextCache.TryGetValue(e.Key, out var text) && text.Contains(lowered));
        return source.OrderByDescending(e => e.Timestamp).ToList();
    }

    private void EnsureSearchCacheCurrent()
    {
        var entries = Plugin.Instance.Archive.Entries;
        var characterKey = Plugin.Instance.Archive.LoadedContentId.ToString("X");
        if (_searchCacheFor == characterKey && _searchCacheEntryCount == entries.Count) return;
        _searchTextCache.Clear();
        foreach (var entry in entries.Values)
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
        if (!Plugin.Instance.Archive.Entries.TryGetValue(key, out var entry))
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

    private static string CategoryLabel(byte category) => category switch
    {
        0 => "Friends",
        1 => "Purchases & Rewards",
        2 => "GM",
        _ => $"Other ({category})",
    };

    private static string LookupItemName(uint itemId) => ItemNameResolver.Resolve(itemId);
}
