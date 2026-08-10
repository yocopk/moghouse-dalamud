using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
/// data being uploaded at all, without trusting a website to honour the request. Whether uploaded
/// data then produces a push is a separate, server-side choice.
///
/// Laid out as one section per tool rather than a flat list of switches, because the plugin is a
/// set of quality-of-life features that happens to have started with timers — a third one should be
/// a new section here, not a reason to rearrange the window.
/// </summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private readonly record struct TimerRow(string Key, string Label, string Description);

    private static readonly TimerRow[] Rows =
    [
        new(TimerKeys.Submarine, "Submarine voyages", "Read in the company workshop. Timed to the last one back."),
        new(TimerKeys.Airship, "Airship voyages", "Read in the company workshop. Timed to the last one back."),
        new(TimerKeys.Venture, "Retainer ventures", "Completion times, read at the retainer bell."),
        new(TimerKeys.MapAllowance, "Treasure maps", "When the next map allowance is up."),
        new(TimerKeys.LeveAllowance, "Leve allowances", "How many you are holding."),
        new(TimerKeys.CustomDeliveries, "Custom deliveries", "Allowances left this week."),
        new(TimerKeys.AlliedDailies, "Allied society", "Daily allowances left."),
    ];

    private readonly Configuration configuration;
    private readonly TimerSyncService syncService;

    public ConfigWindow(Configuration configuration, TimerSyncService syncService)
        : base("MogHouse Companion — Settings###MogHouseCompanionConfig")
    {
        this.configuration = configuration;
        this.syncService = syncService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        Theme.Icon(FontAwesomeIcon.Lock, Theme.Gold);
        ImGui.SameLine(0, 8 * ImGuiHelpers.GlobalScale);
        ImGui.TextWrapped("Nothing leaves the game unless it is switched on here.");

        Theme.Heading(FontAwesomeIcon.Hourglass, "Timers");
        Theme.Card("##timers", DrawTimers);

        Theme.Heading(FontAwesomeIcon.Dungeon, "Duty Finder");
        Theme.Card("##duty", DrawDuty);
    }

    private void DrawTimers()
    {
        Theme.Hint("Switching one off also clears it from your account on the next sync.");

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

        foreach (var row in Rows)
        {
            DrawTimerRow(row);
        }

        ImGui.Spacing();

        Theme.Hint(
            "Grand Company and Fashion Report reminders run off fixed resets, so they need no data " +
            "from the game and are switched on from the website.");

        ImGui.Spacing();

        if (Theme.PrimaryButton("Choose which ones notify me"))
        {
            Util.OpenLink(configuration.TimersUrl);
        }
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
            ImGui.TextColored(Theme.Muted, row.Description);
        }
    }

    private void DrawDuty()
    {
        var push = configuration.DutyFinderPush;

        if (ImGui.Checkbox("Tell MogHouse when a duty pops###MogHouseCompanionDutyPush", ref push))
        {
            configuration.DutyFinderPush = push;
            configuration.Save();
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(
                Theme.Muted,
                "Sends the duty or roulette name so the push can say what popped.\n" +
                "The plugin never presses anything: commencing is yours to do, at the keyboard.");
        }

        ImGui.Spacing();

        using (ImRaii.Disabled(!push))
        {
            // Two radios rather than one checkbox: this is a choice between two behaviours the
            // player can reasonably want, and "only when away" phrased as a single tickbox reads
            // like a caveat on the feature above rather than a setting of its own.
            ImGui.TextColored(Theme.Muted, "When it pops while the game is the window in front of you:");

            using (ImRaii.PushIndent(26f))
            {
                DrawAwayChoice();
            }
        }
    }

    private void DrawAwayChoice()
    {
        var onlyAway = configuration.DutyPushOnlyWhenAway;

        if (ImGui.RadioButton("Stay quiet###MogHouseCompanionDutyAway", onlyAway) && !onlyAway)
        {
            SetOnlyWhenAway(true);
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(Theme.Muted, "The game has already made a noise at you.");
        }

        if (ImGui.RadioButton("Push anyway###MogHouseCompanionDutyAlways", !onlyAway) && onlyAway)
        {
            SetOnlyWhenAway(false);
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(
                Theme.Muted,
                "For a game running muted, or focused on a screen you are not watching.");
        }
    }

    private void SetOnlyWhenAway(bool onlyAway)
    {
        configuration.DutyPushOnlyWhenAway = onlyAway;
        configuration.Save();
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
}
