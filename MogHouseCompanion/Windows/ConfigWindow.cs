using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using MogHouseCompanion.Collectors;
using MogHouseCompanion.Services;

namespace MogHouseCompanion.Windows;

/// <summary>
/// Decides what leaves the game.
///
/// This is the privacy boundary and it lives in-game on purpose: the player should be able to stop
/// a timer being uploaded at all, without trusting a website to honour the request. Whether an
/// uploaded timer then produces a push is a separate, server-side choice.
/// </summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private readonly record struct TimerRow(string Key, string Label, string Description);

    private static readonly TimerRow[] Rows =
    [
        new(TimerKeys.Submarine, "Submarine voyages", "Return times, read in the company workshop."),
        new(TimerKeys.Airship, "Airship voyages", "Return times, read in the company workshop."),
        new(TimerKeys.Venture, "Retainer ventures", "Completion times, read at the retainer bell."),
        new(TimerKeys.MapAllowance, "Treasure maps", "When the next map allowance is up."),
        new(TimerKeys.LeveAllowance, "Leve allowances", "How many you are holding."),
        new(TimerKeys.CustomDeliveries, "Custom deliveries", "Allowances left this week."),
        new(TimerKeys.AlliedDailies, "Allied society", "Daily allowances left."),
    ];

    private readonly Configuration configuration;
    private readonly TimerSyncService syncService;

    private string serverDraft;

    public ConfigWindow(Configuration configuration, TimerSyncService syncService)
        : base("MogHouse Companion — Settings###MogHouseCompanionConfig")
    {
        this.configuration = configuration;
        this.syncService = syncService;
        serverDraft = configuration.BaseUrl;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(470, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped("Choose which timers this plugin sends to MogHouse.");
        ImGui.TextColored(
            ImGuiColors.DalamudGrey,
            "Switching one off also clears it from your account on the next sync.");

        ImGui.Spacing();

        if (ImGui.Button("Enable all"))
        {
            SetAll(true);
        }

        ImGui.SameLine();

        if (ImGui.Button("Disable all"))
        {
            SetAll(false);
        }

        ImGui.Spacing();
        ImGui.Separator();

        foreach (var row in Rows)
        {
            DrawTimerRow(row);
        }

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(
            ImGuiColors.DalamudGrey,
            "Grand Company and Fashion Report reminders run off fixed resets, so they need no data\n" +
            "from the game and are switched on from the website.");

        ImGui.Spacing();

        if (ImGui.Button("Choose which ones notify me"))
        {
            Util.OpenLink(configuration.TimersUrl);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawAdvanced();
    }

    private void DrawTimerRow(TimerRow row)
    {
        var enabled = configuration.IsTimerEnabled(row.Key);

        ImGui.Spacing();

        if (ImGui.Checkbox($"{row.Label}###MogHouseCompanionTimer{row.Key}", ref enabled))
        {
            configuration.SetTimerEnabled(row.Key, enabled);
            configuration.Save();

            // Upload straight away so the change is visible on the site without waiting an hour.
            syncService.RequestSync();
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, row.Description);
        }
    }

    private void SetAll(bool enabled)
    {
        foreach (var row in Rows)
        {
            configuration.SetTimerEnabled(row.Key, enabled);
        }

        configuration.Save();
        syncService.RequestSync();
    }

    /// <summary>
    /// Server selection. Collapsed and worded as a warning, because pointing a normal user at the
    /// wrong instance would silently stop their notifications.
    /// </summary>
    private void DrawAdvanced()
    {
        if (!ImGui.CollapsingHeader("Advanced"))
        {
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
}
