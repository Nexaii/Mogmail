using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mogmail.UI.Tabs;

namespace Mogmail.UI;

public sealed class SettingsWindow : Window
{
    private const string KoFiUrl = "https://ko-fi.com/nexai";
    private const float FrameRounding = 6f;
    private const float FrameBorderSize = 1f;
    private const float ChildRounding = 6f;

    private readonly ISettingsTab[] _tabs;
    private readonly TitleBarButton _kofiButton;

    public SettingsWindow() : base("Mogmail##MogmailSettings")
    {
        Size = new Vector2(520, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 320),
            MaximumSize = new Vector2(720, 900),
        };

        _tabs =
        [
            new GeneralTab(),
            new PopTab(),
            new DiagnosticsTab(),
        ];

        _kofiButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.Heart,
            ShowTooltip = () => ImGui.SetTooltip("Support on Ko-Fi"),
            Priority = int.MinValue,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ => OpenKoFiLink(),
            AvailableClickthrough = true,
        };

        TitleBarButtons.Add(_kofiButton);
    }

    public override void Draw()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, FrameRounding)
                                .Push(ImGuiStyleVar.FrameBorderSize, FrameBorderSize)
                                .Push(ImGuiStyleVar.ChildRounding, ChildRounding)
                                .Push(ImGuiStyleVar.GrabRounding, FrameRounding)
                                .Push(ImGuiStyleVar.TabRounding, FrameRounding);

        if (!ImGui.BeginTabBar("MogmailSettingsTabs")) return;

        foreach (var tab in _tabs)
        {
            if (ImGui.BeginTabItem(tab.Name))
            {
                tab.Draw();
                ImGui.EndTabItem();
            }
        }

        ImGui.EndTabBar();
    }

    private static void OpenKoFiLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = KoFiUrl,
                UseShellExecute = true,
                Verb = string.Empty,
            });
        }
        catch
        {
        }
    }
}
