using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MogHouseCompanion.Windows;

/// <summary>
/// The plugin's look, in one place.
///
/// Dalamud's defaults are grey on grey and every plugin wearing them looks like every other one.
/// MogHouse already has an identity on the website — lantern gold on a warm dark ground — and the
/// in-game half should be recognisably the same product, without fighting the game's own UI for
/// attention. So: gold reserved for headings and the one action that matters on a screen, colour
/// used to mean something (linked, stale, broken) rather than to decorate, and everything else
/// muted.
///
/// The values are lifted from the site's design tokens so the two stay in step.
/// </summary>
public static class Theme
{
    /// <summary>--color-hue-gold. The brand accent; headings and primary actions only.</summary>
    public static readonly Vector4 Gold = new(0.792f, 0.616f, 0.200f, 1f);

    /// <summary>--color-hue-gold-fg (dark theme). Gold that stays readable as body text.</summary>
    public static readonly Vector4 GoldSoft = new(0.890f, 0.784f, 0.471f, 1f);

    /// <summary>--color-hue-green. "This is working."</summary>
    public static readonly Vector4 Good = new(0.384f, 0.733f, 0.471f, 1f);

    /// <summary>--color-hue-coral. "This needs you." Warmer than Dalamud's red, and less alarming.</summary>
    public static readonly Vector4 Bad = new(0.922f, 0.510f, 0.482f, 1f);

    /// <summary>--color-hue-violet. Mog+, matching the diamond on the site.</summary>
    public static readonly Vector4 Premium = new(0.694f, 0.569f, 0.918f, 1f);

    /// <summary>--color-text-dim (dark theme). Everything that is not the point of the line.</summary>
    public static readonly Vector4 Muted = new(0.541f, 0.541f, 0.624f, 1f);

    private const float LabelWidth = 132f;
    private const float PillRounding = 8f;

    /// <summary>
    /// Names a group of controls. Gold and spaced-out rather than bold, because ImGui's bold is the
    /// same weight as its regular and the letter-spacing is what actually reads as a heading.
    /// </summary>
    public static void Heading(FontAwesomeIcon icon, string label)
    {
        ImGui.Spacing();

        Icon(icon, Gold);
        ImGui.SameLine(0, 8 * ImGuiHelpers.GlobalScale);
        ImGui.TextColored(Gold, Spaced(label.ToUpperInvariant()));

        ImGui.Spacing();
    }

    /// <summary>A label and its value on one line, with every window's labels in the same column.</summary>
    public static void Field(string label, string value, Vector4? color = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Muted, label);
        ImGui.SameLine(LabelWidth * ImGuiHelpers.GlobalScale);
        ImGui.TextColored(color ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text], value);
    }

    /// <summary>
    /// A state, as a filled chip. Loud enough to be the thing you look at first when you open the
    /// window, which for a sync plugin is the only question worth answering that fast.
    /// </summary>
    public static void Pill(string text, Vector4 color)
    {
        var padding = new Vector2(8, 3) * ImGuiHelpers.GlobalScale;
        var size = ImGui.CalcTextSize(text);
        var origin = ImGui.GetCursorScreenPos();
        var box = size + (padding * 2);

        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + box, ImGui.ColorConvertFloat4ToU32(color with { W = 0.16f }), PillRounding);
        draw.AddRect(origin, origin + box, ImGui.ColorConvertFloat4ToU32(color with { W = 0.45f }), PillRounding);

        ImGui.SetCursorScreenPos(origin + padding);
        ImGui.TextColored(color, text);

        // Put the cursor back where a normal widget of this size would have left it, so callers can
        // keep using SameLine and Spacing without knowing a chip was drawn.
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(box);
    }

    public static void Icon(FontAwesomeIcon icon, Vector4? color = null)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            if (color.HasValue)
            {
                ImGui.TextColored(color.Value, icon.ToIconString());
            }
            else
            {
                ImGui.TextUnformatted(icon.ToIconString());
            }
        }
    }

    /// <summary>Secondary text: explanations, caveats, the reason a control is disabled.</summary>
    public static void Hint(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(text);
        }
    }

    /// <summary>The one button on a screen you are meant to press.</summary>
    public static bool PrimaryButton(string label)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Gold with { W = 0.22f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Gold with { W = 0.35f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, Gold with { W = 0.50f }))
        using (ImRaii.PushColor(ImGuiCol.Text, GoldSoft))
        {
            return ImGui.Button(label);
        }
    }

    /// <summary>
    /// Groups related rows on a slightly raised ground, the way the website's cards do. Height is
    /// measured from the content, so callers never have to guess a pixel value.
    /// </summary>
    public static void Card(string id, Action body)
    {
        var padding = new Vector2(10, 8) * ImGuiHelpers.GlobalScale;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        // The background has to be drawn before the content to sit behind it, but its height is only
        // known after — so the draw list is split, the content goes on the upper channel, and the
        // rectangle is filled in underneath once the height is known.
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);

        float height;

        try
        {
            ImGui.SetCursorScreenPos(origin + padding);
            using (ImRaii.PushIndent(padding.X))
            using (ImRaii.Group())
            {
                ImGui.Dummy(new Vector2(width - (padding.X * 3), 0));
                body();
            }
        }
        finally
        {
            // Merged whatever happened: leaving the draw list split would corrupt the rest of the
            // frame, turning a broken card into a broken window.
            height = ImGui.GetCursorScreenPos().Y - origin.Y + padding.Y;

            draw.ChannelsSetCurrent(0);
            draw.AddRectFilled(
                origin,
                origin + new Vector2(width, height),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.03f)),
                6f);
            draw.ChannelsMerge();
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    /// <summary>Letter-spacing, faked. ImGui has no tracking, and headings need it to read as headings.</summary>
    private static string Spaced(string text)
    {
        return string.Join(" ", text.ToCharArray());
    }
}
