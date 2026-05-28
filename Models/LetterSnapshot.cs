using System.Collections.Generic;

namespace Mogmail.Models;

public sealed record AttachmentSnapshot(uint ItemId, uint Count);

public sealed class LetterSnapshot
{
    public ulong SenderContentId { get; set; }
    public uint Timestamp { get; set; }
    public string Sender { get; set; } = "";
    public string Preview { get; set; } = "";
    public byte Category { get; set; }
    public bool Read { get; set; }
    public uint Gil { get; set; }
    public List<AttachmentSnapshot> Attachments { get; set; } = new();
}
