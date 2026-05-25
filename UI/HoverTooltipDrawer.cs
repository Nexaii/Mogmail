using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Mogmail.UI;

public sealed class HoverTooltipDrawer
{
    private readonly Dictionary<string, HoverState> _states = new();

    public void TrackHover(string key, bool isHovered)
    {
        if (!_states.TryGetValue(key, out var state))
        {
            state = new HoverState();
            _states[key] = state;
        }
        var now = (float)ImGui.GetTime() * 1000f;
        if (isHovered && !state.IsHovered)
        {
            state.IsHovered = true;
            state.EnterTime = now;
        }
        else if (!isHovered && state.IsHovered)
        {
            state.IsHovered = false;
            state.ExitTime = now;
        }
    }

    public void DrawTooltip(string key, string label, string description, Vector2 anchor, Vector2 pivot, TooltipSide side, float fixedWidth)
    {
        if (!_states.TryGetValue(key, out var state)) return;

        var now = (float)ImGui.GetTime() * 1000f;
        var t = ComputeProgress(state, now);
        if (t <= 0f) return;

        var ease = EaseOutCubic(t);
        var alpha = ease * Theme.TooltipBgAlpha;
        var slide = (1f - ease) * 12f;
        var offset = ComputeSlideOffset(side, slide);
        var pos = anchor + offset;

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
        ImGui.SetNextWindowSizeConstraints(new Vector2(fixedWidth, 0), new Vector2(fixedWidth, float.MaxValue));
        ImGui.SetNextWindowBgAlpha(alpha);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoInputs
                  | ImGuiWindowFlags.NoSavedSettings
                  | ImGuiWindowFlags.NoFocusOnAppearing
                  | ImGuiWindowFlags.NoNav;

        if (ImGui.Begin($"##MogmailTip_{key}", flags))
        {
            var textAlpha = alpha / Theme.TooltipBgAlpha;
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, textAlpha)))
                ImGui.TextUnformatted(label);
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(Theme.ColorSubdued.X, Theme.ColorSubdued.Y, Theme.ColorSubdued.Z, textAlpha)))
                ImGui.TextWrapped(description);
        }
        ImGui.End();
    }

    public void Reset()
    {
        _states.Clear();
    }

    private static float ComputeProgress(HoverState state, float now)
    {
        if (state.IsHovered)
        {
            var elapsed = now - state.EnterTime;
            return Math.Clamp(elapsed / Theme.TooltipAnimMs, 0f, 1f);
        }
        var elapsedOut = now - state.ExitTime;
        return 1f - Math.Clamp(elapsedOut / Theme.TooltipExitAnimMs, 0f, 1f);
    }

    private static float EaseOutCubic(float t)
    {
        var inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private static Vector2 ComputeSlideOffset(TooltipSide side, float slide) => side switch
    {
        TooltipSide.Left => new Vector2(-slide, 0f),
        TooltipSide.Right => new Vector2(slide, 0f),
        TooltipSide.Above => new Vector2(0f, -slide),
        TooltipSide.Below => new Vector2(0f, slide),
        _ => Vector2.Zero,
    };

    private sealed class HoverState
    {
        public bool IsHovered;
        public float EnterTime;
        public float ExitTime;
    }
}

public enum TooltipSide { Left, Right, Above, Below }
