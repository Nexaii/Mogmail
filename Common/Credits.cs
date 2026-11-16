namespace Mogmail.Common;

public static class Credits
{
    public readonly record struct Row(string To, string Subject, string Note);

    public static readonly Row[] Rows =
    {
        new("Friends", "Thank you, kupo",
            "Kupo! Thanks to all my friends for testing and critiquing, and to LF for kicking me to ship it... I sat on it for several months before making it public!"),
    };
}
