namespace Mogmail.IPC;

public static class IPCNames
{
    public const string Prefix = "Mogmail";

    public const string IsAvailable     = Prefix + ".IsAvailable";
    public const string IsBusy          = Prefix + ".IsBusy";
    public const string IsMailboxOpen   = Prefix + ".IsMailboxOpen";
    public const string LetterCount     = Prefix + ".LetterCount";

    public const string ClaimAll        = Prefix + ".ClaimAll";
    public const string ClaimAndDelete  = Prefix + ".ClaimAndDelete";
    public const string ReadAll         = Prefix + ".ReadAll";
    public const string ReadAllAndDelete = Prefix + ".ReadAllAndDelete";
    public const string DeleteReadEmpty = Prefix + ".DeleteReadEmpty";
    public const string Pop             = Prefix + ".Pop";
    public const string Stop            = Prefix + ".Stop";
}
