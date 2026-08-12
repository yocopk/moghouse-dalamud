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
    private readonly TimersWindow timersWindow;

    public ConfigWindow(Configuration configuration, TimerSyncService syncService, TimersWindow timersWindow)
        : base("MogHouse Companion — Settings###MogHouseCompanionConfig")
    {
        this.configuration = configuration;
        this.syncService = syncService;
        this.timersWindow = timersWindow;

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

        ImGui.Spacing();

        // Tabs rather than stacked sections: with four of them the window had grown past a screenful,
        // and scrolling to find a checkbox is how a setting stops being found at all.
        using var tabs = ImRaii.TabBar("##MogHouseCompanionTabs");
        if (!tabs)
        {
            return;
        }

        DrawTab("Timers", FontAwesomeIcon.Hourglass, "##timers", DrawTimers);
        DrawTab("Roulettes", FontAwesomeIcon.ClipboardCheck, "##roulettes", DrawRoulettes);
        DrawTab("Duty Finder", FontAwesomeIcon.Dungeon, "##duty", DrawDuty);
        DrawTab("In game", FontAwesomeIcon.Comments, "##ingame", DrawInGame);
    }

    private static void DrawTab(string label, FontAwesomeIcon icon, string id, Action body)
    {
        using var tab = ImRaii.TabItem(label);
        if (!tab)
        {
            return;
        }

        ImGui.Spacing();
        Theme.Heading(icon, label);
        Theme.Card(id, body);
    }

    /// <summary>
    /// What the plugin says to you at the keyboard, as opposed to what it sends to your phone.
    /// Nothing here leaves the machine, which is why it sits apart from the two sections above.
    /// </summary>
    private void DrawInGame()
    {
        var showTimers = configuration.ShowTimersWindow;

        if (ImGui.Checkbox("Show the timers window###MogHouseCompanionShowTimers", ref showTimers))
        {
            configuration.ShowTimersWindow = showTimers;
            configuration.Save();
            timersWindow.IsOpen = showTimers;
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(
                Theme.Muted,
                "A small readout of your own timers, opened alongside the main window.");
        }

        ImGui.Spacing();

        var announceSync = configuration.AnnounceSyncInChat;

        if (ImGui.Checkbox("Say when a sync goes through###MogHouseCompanionAnnounceSync", ref announceSync))
        {
            configuration.AnnounceSyncInChat = announceSync;
            configuration.Save();
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(Theme.Muted, "One line in chat each time a snapshot reaches MogHouse.");
        }

        ImGui.Spacing();

        var announceDone = configuration.AnnounceFinishedTimersInChat;

        if (ImGui.Checkbox("Say when a timer finishes###MogHouseCompanionAnnounceDone", ref announceDone))
        {
            configuration.AnnounceFinishedTimersInChat = announceDone;
            configuration.Save();
        }

        using (ImRaii.PushIndent(26f))
        {
            ImGui.TextColored(
                Theme.Muted,
                "Only while you are playing. Anything that finished before you logged in stays\n" +
                "quiet — the push already told you, and a login should not replay the night.");
        }
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

    /// <summary>
    /// The daily roulette checklist. Read live from the game rather than from a hardcoded list, so a
    /// roulette added in a patch turns up on its own instead of waiting for a plugin release.
    /// </summary>
    private void DrawRoulettes()
    {
        var roulettes = RouletteReader.Read();

        if (roulettes.Count == 0)
        {
            ImGui.TextColored(Theme.Muted, "Log in to read the roulette list.");
            return;
        }

        Theme.Hint("Ticked roulettes appear in the checklist and count towards what is left today.");

        ImGui.Spacing();

        if (ImGui.Button("Everyday ones"))
        {
            foreach (var roulette in roulettes)
            {
                configuration.SetRouletteTracked(roulette.RowId, !roulette.IsExtra);
            }

            configuration.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button("None"))
        {
            foreach (var roulette in roulettes)
            {
                configuration.SetRouletteTracked(roulette.RowId, false);
            }

            configuration.Save();
        }

        ImGui.Spacing();

        foreach (var roulette in roulettes)
        {
            var tracked = configuration.IsRouletteTracked(roulette.RowId, roulette.IsExtra);

            if (ImGui.Checkbox($"{roulette.Name}###MogHouseCompanionRoulette{roulette.RowId}", ref tracked))
            {
                configuration.SetRouletteTracked(roulette.RowId, tracked);
                configuration.Save();
            }

            // Today's state, right there while you are choosing — it is the fastest way to tell
            // which of two similarly named roulettes is the one you actually run.
            ImGui.SameLine();
            ImGui.TextColored(
                roulette.Complete ? Theme.Muted : Theme.Good,
                roulette.Complete ? "done" : "available");
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
