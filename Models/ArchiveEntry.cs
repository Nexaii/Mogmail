using System.Collections.Generic;

namespace Mogmail.Models;

public sealed class ArchiveAttachment
{
    public uint ItemId { get; set; }
    public uint Count { get; set; }
}

public sealed class ArchiveEntry
{
    public string Key { get; set; } = "";
    public ulong SenderContentId { get; set; }
    public uint Timestamp { get; set; }
    public string Sender { get; set; } = "";
    public string Preview { get; set; } = "";
    public string Body { get; set; } = "";
    public byte Category { get; set; }
    public bool Read { get; set; }
    public uint Gil { get; set; }
    public List<ArchiveAttachment> Attachments { get; set; } = new();
    public string CapturedUtc { get; set; } = "";
    public string BodyCapturedUtc { get; set; } = "";
}
