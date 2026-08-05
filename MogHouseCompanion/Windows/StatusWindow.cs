using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using MogHouseCompanion.Services;

namespace MogHouseCompanion.Windows;

/// <summary>
/// Read-only status surface opened with /moghouse. Alert configuration deliberately lives on the
/// website only, so the server stays the single source of truth for what gets notified.
/// </summary>
public sealed class StatusWindow : Window, IDisposable
{
    private const float LabelWidth = 130f;

    /// <summary>Past this age the sync is old enough that the player should be told.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private readonly Configuration configuration;
    private readonly TimerSyncService syncService;
    private readonly PairingWindow pairingWindow;

    public StatusWindow(Configuration configuration, TimerSyncService syncService, PairingWindow pairingWindow)
        : base("MogHouse Companion###MogHouseCompanionStatus")
    {
        this.configuration = configuration;
        this.syncService = syncService;
        this.pairingWindow = pairingWindow;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
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

        if (!configuration.IsLinked)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawSync();
    }

    private void DrawAccount()
    {
        Field("Server", configuration.BaseUrl);

        if (!configuration.IsLinked)
        {
            Field("Account", "Not linked", ImGuiColors.DalamudYellow);

            ImGui.Spacing();
            ImGui.TextWrapped("Link this PC to your MogHouse account to sync your in-game timers.");
            ImGui.Spacing();

            if (ImGui.Button("Link account…"))
            {
                pairingWindow.IsOpen = true;
            }

            return;
        }

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
    }

    private static void DrawCharacter()
    {
        var player = Plugin.PlayerState;
        if (!player.IsLoaded)
        {
            Field("Character", "Not logged in", ImGuiColors.DalamudGrey);
            return;
        }

        var world = player.HomeWorld.IsValid ? player.HomeWorld.Value.Name.ToString() : "unknown world";
        Field("Character", $"{player.CharacterName} @ {world}");
    }

    private void DrawSync()
    {
        var status = syncService.Status;

        if (status.PremiumRequired)
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "FFXIV Sync requires an active Mog+ subscription.");
            ImGui.TextWrapped(
                "Your link is kept, so syncing resumes on its own once Mog+ is active again — " +
                "you will not have to pair this device a second time.");

            ImGui.Spacing();

            if (ImGui.Button("Manage Mog+"))
            {
                Util.OpenLink($"{configuration.BaseUrl.TrimEnd('/')}/premium");
            }

            ImGui.Spacing();
        }

        Field("Last sync", DescribeLastSync(status));

        if (status.LastSuccessAt.HasValue)
        {
            Field("Timers sent", status.TimerCount.ToString());
        }

        if (status.Available.Length > 0)
        {
            Field("Reading", string.Join(", ", status.Available));
        }

        if (status.Unavailable.Length > 0)
        {
            Field("No data yet", string.Join(", ", status.Unavailable), ImGuiColors.DalamudGrey);
        }

        if (status.LastError is { Length: > 0 } error)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudRed, error);
        }

        ImGui.Spacing();

        using (ImRaii.Disabled(status.IsSyncing))
        {
            if (ImGui.Button(status.IsSyncing ? "Syncing…" : "Sync now"))
            {
                syncService.RequestSync();
            }
        }

        if (status.Unavailable.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(
                ImGuiColors.DalamudGrey,
                "Voyage timers are only readable inside the company workshop, and venture timers\n" +
                "after opening the retainer bell. This is a limit of the game, not of the plugin.");
        }
    }

    private static string DescribeLastSync(SyncStatus status)
    {
        if (!status.LastSuccessAt.HasValue)
        {
            return status.IsSyncing ? "syncing…" : "never";
        }

        var age = DateTime.UtcNow - status.LastSuccessAt.Value;
        var text = age < TimeSpan.FromMinutes(1)
            ? "just now"
            : age < TimeSpan.FromHours(1)
                ? $"{(int)age.TotalMinutes} min ago"
                : age < TimeSpan.FromDays(1)
                    ? $"{(int)age.TotalHours} h ago"
                    : $"{(int)age.TotalDays} d ago";

        return age > StaleAfter ? $"{text} (stale)" : text;
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
