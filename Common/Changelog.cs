namespace Mogmail.Common;

public enum ChangeKind
{
    Fixed,
    Added,
    Changed
}

public static class Changelog
{
    public const int Revision = 0;

    public readonly record struct Entry(ChangeKind Kind, string Text);

    public readonly record struct Version(string Label, string Date, Entry[] Entries);

    public static readonly Version[] Versions =
    {
        new("1.3.0.0", "2026-11-16", new[]
        {
            new Entry(ChangeKind.Added, "Info button in the settings title bar. Opens Changelog, Logs, and Credits."),
            new Entry(ChangeKind.Added, "Support for the new Wrapped Gift item."),
        }),
        new("1.2.6.0", "2026-08-22", new[]
        {
            new Entry(ChangeKind.Changed, "Auto Pop now confirms its own dialog."),
        }),
        new("1.2.5.0", "2026-08-03", new[]
        {
            new Entry(ChangeKind.Fixed, "Tooltips."),
        }),
        new("1.2.4.0", "2026-07-28", new[]
        {
            new Entry(ChangeKind.Changed, "Updated for FFXIV 7.55."),
        }),
        new("1.2.3.0", "2026-07-26", new[]
        {
            new Entry(ChangeKind.Changed, "Removed the repo migrator."),
        }),
        new("1.2.2.0", "2026-07-07", new[]
        {
            new Entry(ChangeKind.Fixed, "Toolbar no longer covers an open letter."),
            new Entry(ChangeKind.Changed, "Donation link moved to Patreon."),
        }),
        new("1.2.1.0", "2026-06-04", new[]
        {
            new Entry(ChangeKind.Added, "Repo migration."),
        }),
        new("1.2.0.0", "2026-05-31", new[]
        {
            new Entry(ChangeKind.Added, "Smarter pop and rejection backoff."),
            new Entry(ChangeKind.Fixed, "Thread-safe archive and clean unload."),
        }),
        new("1.1.0.0", "2026-05-27", new[]
        {
            new Entry(ChangeKind.Added, "Archive: per-character letter log with search, filter, and export."),
            new Entry(ChangeKind.Added, "Gift sender info echoed on Take for Purchase and Reward letters."),
            new Entry(ChangeKind.Fixed, "System delete scope matching zero letters."),
        }),
        new("1.0.1.0", "2026-05-26", new[]
        {
            new Entry(ChangeKind.Added, "Icon and punchline."),
            new Entry(ChangeKind.Changed, "Dropped the diagnostic toggle."),
        }),
        new("1.0.0.0", "2026-05-24", new[]
        {
            new Entry(ChangeKind.Added, "First release."),
        }),
    };
}
