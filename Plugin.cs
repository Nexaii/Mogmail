using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Mogmail.IPC;
using Mogmail.Managers;
using Mogmail.Services;
using Mogmail.UI;

namespace Mogmail;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Mogmail";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IUnlockState UnlockState { get; private set; } = null!;

    public static Configuration Config { get; private set; } = null!;
    public static Plugin Instance { get; private set; } = null!;
    public static FileDialogManager FileDialogManager { get; } = new();

    public MailboxService Mailbox { get; }
    public MailRejectionWatcher MailRejectionWatcher { get; }
    public ClaimQueueManager ClaimQueue { get; }
    public AttachmentPopManager PopQueue { get; }
    public ReadAllManager ReadAll { get; }
    public PopConfirmAutoclicker PopConfirmAutoclicker { get; }
    public MailFullPopupAutocloser MailFullPopupAutocloser { get; }
    public UseActionDiagnosticHook UseActionDiagnosticHook { get; }
    public PopAddonTraceLogger PopAddonTraceLogger { get; }
    public IPCProvider IPC { get; }

    private const string CommandName = "/mogmail";

    private readonly WindowSystem _windowSystem = new("Mogmail");
    private readonly SettingsWindow _settingsWindow;
    private readonly ConfirmDialog _confirmDialog;
    private readonly ToolbarWindow _toolbarWindow;
    private readonly ProcessingOverlay _processingOverlay;
    private readonly PopProgressOverlay _popProgressOverlay;
    private readonly SensitivePopConfirm _sensitivePopConfirm;
    private bool _disposed;

    public Plugin()
    {
        Instance = this;
        Config = LoadAndSanitizeConfig();

        Mailbox = new MailboxService();
        MailRejectionWatcher = new MailRejectionWatcher(Chat);
        ClaimQueue = new ClaimQueueManager();
        PopQueue = new AttachmentPopManager();
        ReadAll = new ReadAllManager();
        PopConfirmAutoclicker = new PopConfirmAutoclicker();
        MailFullPopupAutocloser = new MailFullPopupAutocloser();
        UseActionDiagnosticHook = new UseActionDiagnosticHook();
        PopAddonTraceLogger = new PopAddonTraceLogger();
        IPC = new IPCProvider();

        _settingsWindow = new SettingsWindow();
        _confirmDialog = new ConfirmDialog();
        _toolbarWindow = new ToolbarWindow(_confirmDialog);
        _processingOverlay = new ProcessingOverlay();
        _popProgressOverlay = new PopProgressOverlay();
        _sensitivePopConfirm = new SensitivePopConfirm();

        _windowSystem.AddWindow(_settingsWindow);
        _windowSystem.AddWindow(_confirmDialog);
        _windowSystem.AddWindow(_toolbarWindow);
        _windowSystem.AddWindow(_processingOverlay);
        _windowSystem.AddWindow(_popProgressOverlay);
        _windowSystem.AddWindow(_sensitivePopConfirm);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi += OpenSettings;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings. Subcommands: pop (use inventory items), stop (abort runs).",
        });

        Log.Info("[Mogmail] loaded");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IPC.Dispose();
        PopAddonTraceLogger.Dispose();
        UseActionDiagnosticHook.Dispose();
        MailFullPopupAutocloser.Dispose();
        PopConfirmAutoclicker.Dispose();
        ReadAll.Dispose();
        PopQueue.Dispose();
        ClaimQueue.Dispose();
        MailRejectionWatcher.Dispose();
        Mailbox.Dispose();

        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi -= OpenSettings;
        _windowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(CommandName);

        Config.Save();

        Log.Info("[Mogmail] unloaded");
        Services.MogLog.Shutdown();
    }

    private static Configuration LoadAndSanitizeConfig()
    {
        var cfg = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var dirty = false;
        if (cfg.ToolbarLayout != ToolbarLayout.Vertical)
        {
            Log.Information("[Mogmail] migrating ToolbarLayout to Vertical. Horizontal layout was removed in 0.4.3.");
            cfg.ToolbarLayout = ToolbarLayout.Vertical;
            dirty = true;
        }
        if (cfg.ToolbarAttach == ToolbarAttach.SnappedTop)
        {
            Log.Information("[Mogmail] migrating ToolbarAttach from SnappedTop to SnappedLeft. Top anchor was removed in 0.4.3.");
            cfg.ToolbarAttach = ToolbarAttach.SnappedLeft;
            dirty = true;
        }
        if (cfg.ToolbarAttach == ToolbarAttach.Free)
        {
            Log.Information("[Mogmail] migrating ToolbarAttach from Free to SnappedLeft. Free anchor was removed in 0.4.7.");
            cfg.ToolbarAttach = ToolbarAttach.SnappedLeft;
            dirty = true;
        }
        if (cfg.ButtonDisplayMode != ButtonDisplayMode.Iconic)
        {
            Log.Information("[Mogmail] migrating ButtonDisplayMode to Iconic. Text-only mode was removed in 0.4.5.");
            cfg.ButtonDisplayMode = ButtonDisplayMode.Iconic;
            dirty = true;
        }
        if (dirty) cfg.Save();
        return cfg;
    }

    private void OnDraw()
    {
        _windowSystem.Draw();
        _toolbarWindow.DrawTooltipsOverlay();
        FileDialogManager.Draw();
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            OpenSettings();
            return;
        }

        var firstSpace = trimmed.IndexOf(' ');
        var sub = firstSpace < 0 ? trimmed : trimmed[..firstSpace];

        switch (sub.ToLowerInvariant())
        {
            case "pop":
                StartPopInteractive();
                return;
            case "stop":
                PopQueue.Disarm("user command");
                PopQueue.Abort("user command");
                ClaimQueue.Abort("user command");
                return;
            default:
                Chat.Print($"[Mogmail] unknown subcommand \"{sub}\". Try: pop, stop.");
                return;
        }
    }

    public void OpenSettings() => _settingsWindow.IsOpen = true;

    private void StartPopInteractive()
    {
        var eligible = Services.AttachmentPopManager.CollectAllowedSensitiveInInventory();
        if (eligible.Count > 0)
        {
            _sensitivePopConfirm.Show(
                eligible,
                onYes: () => PopQueue.Start(allowSensitive: true),
                onNo: () => PopQueue.Start(allowSensitive: false));
            return;
        }
        PopQueue.Start(allowSensitive: false);
    }
}
