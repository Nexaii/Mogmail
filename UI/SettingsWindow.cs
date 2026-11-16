using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mogmail.UI.Tabs;

namespace Mogmail.UI;

public sealed class SettingsWindow : Window
{
    private const float FrameRounding = 6f;
    private const float FrameBorderSize = 1f;
    private const float ChildRounding = 6f;

    private readonly ISettingsTab[] _tabs;
    private readonly TitleBarButton _supportButton;
    private readonly TitleBarButton _infoButton;

    public SettingsWindow(InfoWindow infoWindow) : base("Mogmail##MogmailSettings")
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
        ];

        _supportButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.Heart,
            ShowTooltip = () => ImGui.SetTooltip("Support Plugin"),
            Priority = int.MinValue,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ => InfoWindow.OpenLink(InfoWindow.SupportUrl),
            AvailableClickthrough = true,
        };

        _infoButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.InfoCircle,
            ShowTooltip = () => ImGui.SetTooltip("Changelog, logs, and credits"),
            Priority = 2,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ => infoWindow.Toggle(),
        };

        TitleBarButtons.Add(_supportButton);
        TitleBarButtons.Add(_infoButton);
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
}
