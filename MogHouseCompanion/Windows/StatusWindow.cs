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

    /// <summary>Edited copy of the server address; only written back to the config on Apply.</summary>
    private string serverDraft;

    public StatusWindow(Configuration configuration, TimerSyncService syncService, PairingWindow pairingWindow)
        : base("MogHouse Companion###MogHouseCompanionStatus")
    {
        this.configuration = configuration;
        this.syncService = syncService;
        this.pairingWindow = pairingWindow;
        serverDraft = configuration.BaseUrl;

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

        if (configuration.IsLinked)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawSync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawAdvanced();
    }

    /// <summary>
    /// Server selection. Collapsed by default and worded as a warning, because pointing a normal
    /// user at the wrong instance would silently stop their notifications.
    /// </summary>
    private void DrawAdvanced()
    {
        if (!ImGui.CollapsingHeader("Advanced"))
        {
            // Re-sync while closed so reopening never shows an abandoned edit.
            serverDraft = configuration.BaseUrl;
            return;
        }

        ImGui.TextWrapped(
            "Which MogHouse instance this plugin talks to. Leave it alone unless you are testing " +
            "against a development server.");

        ImGui.Spacing();

        if (ImGui.Button("Production"))
        {
            serverDraft = Configuration.ProdBaseUrl;
        }

        ImGui.SameLine();

        if (ImGui.Button("Development"))
        {
            serverDraft = Configuration.DevBaseUrl;
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(320 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("Server###MogHouseCompanionBaseUrl", ref serverDraft, 256);

        var normalized = Configuration.NormalizeBaseUrl(serverDraft);
        var changed = normalized != null && normalized != configuration.BaseUrl;

        using (ImRaii.Disabled(!changed))
        {
            if (ImGui.Button("Apply") && normalized != null)
            {
                ApplyServer(normalized);
            }
        }

        if (normalized == null)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "Enter a full http:// or https:// address.");
        }
        else if (changed && configuration.IsLinked)
        {
            ImGui.TextColored(
                ImGuiColors.DalamudYellow,
                "This will unlink the device: a token only works on the server that issued it.");
        }
    }

    private void ApplyServer(string baseUrl)
    {
        var wasLinked = configuration.IsLinked;

        configuration.BaseUrl = baseUrl;

        // The bearer token is issued by one instance and meaningless on another, so moving server
        // has to drop it rather than leave a token that will only ever return 401.
        configuration.Token = string.Empty;
        configuration.Save();

        syncService.ResetState();
        serverDraft = baseUrl;

        Plugin.Log.Information(
            wasLinked
                ? $"Server set to {baseUrl}; the previous link was dropped."
                : $"Server set to {baseUrl}.");
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

        // Otherwise the window would keep reporting the old link's last sync and error state.
        syncService.ResetState();

        Plugin.Log.Information("Unlinked from MogHouse");
    }
}
