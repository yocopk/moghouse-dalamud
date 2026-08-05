using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace MogHouseCompanion.Windows;

/// <summary>
/// Read-only status surface opened with /moghouse. Alert configuration deliberately lives on the
/// website only, so the server stays the single source of truth for what gets notified.
/// </summary>
public sealed class StatusWindow : Window, IDisposable
{
    private const float LabelWidth = 110f;

    private readonly Configuration configuration;
    private readonly PairingWindow pairingWindow;

    public StatusWindow(Configuration configuration, PairingWindow pairingWindow)
        : base("MogHouse Companion###MogHouseCompanionStatus")
    {
        this.configuration = configuration;
        this.pairingWindow = pairingWindow;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 250),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawAccount();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawCharacter();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(
            ImGuiColors.DalamudGrey,
            "Timer collection is not implemented yet — this build only links your account.");
    }

    private void DrawAccount()
    {
        Field("Server", configuration.BaseUrl);

        if (configuration.IsLinked)
        {
            Field("Account", "Linked", ImGuiColors.HealerGreen);
            Field("Device", configuration.TokenLabel);

            ImGui.Spacing();

            if (ImGui.Button("Open FFXIV Sync settings"))
            {
                Util.OpenLink(configuration.SettingsUrl);
            }

            ImGui.SameLine();

            if (ImGui.Button("Unlink"))
            {
                Unlink();
            }

            ImGui.Spacing();
            ImGui.TextColored(
                ImGuiColors.DalamudGrey,
                "Unlinking only forgets the token on this PC. Revoke the device on the website to\n" +
                "invalidate it everywhere.");

            return;
        }

        Field("Account", "Not linked", ImGuiColors.DalamudYellow);

        ImGui.Spacing();
        ImGui.TextWrapped("Link this PC to your MogHouse account to sync your in-game timers.");
        ImGui.Spacing();

        if (ImGui.Button("Link account…"))
        {
            pairingWindow.IsOpen = true;
        }
    }

    private static void DrawCharacter()
    {
        var player = Plugin.ClientState.LocalPlayer;
        if (player == null)
        {
            Field("Character", "Not logged in", ImGuiColors.DalamudGrey);
            return;
        }

        var world = player.HomeWorld.Value.Name.ToString();
        Field("Character", $"{player.Name.TextValue} @ {world}");
    }

    private static void Field(string label, string value)
    {
        Field(label, value, null);
    }

    private static void Field(string label, string value, Vector4? color)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudGrey, label);
        ImGui.SameLine(LabelWidth * ImGuiHelpers.GlobalScale);

        if (color.HasValue)
        {
            ImGui.TextColored(color.Value, value);
        }
        else
        {
            ImGui.Text(value);
        }
    }

    private void Unlink()
    {
        configuration.Token = string.Empty;
        configuration.Save();
        Plugin.Log.Information("Unlinked from MogHouse");
    }
}
