using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using MogHouseCompanion.Collectors;
using MogHouseCompanion.Dto;
using MogHouseCompanion.Services;

namespace MogHouseCompanion.Windows;

/// <summary>
/// The timers themselves, at a glance.
///
/// The plugin already reads all of this to send it away; making the player open a website to see
/// numbers the game handed over a second ago was the wrong shape. Small and quiet on purpose — it is
/// meant to be left open next to the game, not read.
///
/// Deadlines are absolute, so this recomputes what is left every frame from the last reading rather
/// than waiting on the sync poll. A minute-old reading still shows a correct countdown.
/// </summary>
public sealed class TimersWindow : Window, IDisposable
{
    /// <summary>Under this, a row is close enough to be worth catching the eye.</summary>
    private static readonly TimeSpan SoonThreshold = TimeSpan.FromMinutes(30);

    /// <summary>The order timers are listed in, matching the settings window.</summary>
    private static readonly string[] Order = TimerKeys.All;

    private readonly Configuration configuration;
    private readonly TimerSyncService syncService;

    public TimersWindow(Configuration configuration, TimerSyncService syncService)
        : base("Timers###MogHouseCompanionTimers")
    {
        this.configuration = configuration;
        this.syncService = syncService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 140),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    /// <summary>Closing it by the title bar X is a preference, not a one-off, so it is remembered.</summary>
    public override void OnClose()
    {
        if (configuration.ShowTimersWindow)
        {
            configuration.ShowTimersWindow = false;
            configuration.Save();
        }
    }

    public override void Draw()
    {
        if (!configuration.IsLinked)
        {
            ImGui.TextColored(Theme.Muted, "Link your account to start reading timers.");
            return;
        }

        var reading = syncService.LastReading;

        if (reading.Timers.Count == 0)
        {
            ImGui.TextColored(
                Theme.Muted,
                reading.At.HasValue
                    ? "Nothing readable yet."
                    : "Waiting for the first reading…");

            Theme.Hint(
                "Voyages are only readable inside the company workshop, and ventures after opening " +
                "the retainer bell.");
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var key in Order.Where(configuration.IsTimerEnabled))
        {
            var rows = reading.Timers.Where(t => t.Key == key).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            DrawGroup(key, rows, now);
        }

        DrawRoulettes();
    }

    /// <summary>
    /// What is left of today's roulettes. Read straight from the game each frame rather than from
    /// the sync reading: it costs a sheet scan and a byte lookup each, and it means the tick appears
    /// the moment you walk out of the duty rather than at the next poll.
    /// </summary>
    private void DrawRoulettes()
    {
        var tracked = RouletteReader.Read()
            .Where(r => configuration.IsRouletteTracked(r.RowId, r.IsExtra))
            .ToList();

        if (tracked.Count == 0)
        {
            return;
        }

        var left = tracked.Count(r => !r.Complete);

        ImGui.Spacing();
        ImGui.TextColored(Theme.GoldSoft, "Daily roulettes");

        using (ImRaii.PushIndent(12f))
        {
            // The count first: on a day when everything is done this is the only line worth reading.
            DrawRow(
                "Remaining",
                left == 0 ? "all done" : $"{left} of {tracked.Count}",
                left == 0 ? Theme.Good : Theme.Gold);

            foreach (var roulette in tracked.Where(r => !r.Complete))
            {
                DrawRow(roulette.Name, "available", Theme.Muted);
            }
        }
    }

    private void DrawGroup(string key, List<SnapshotTimer> rows, DateTime now)
    {
        // A single unnamed entry is its own row: "Treasure map / Treasure map" reads like a bug.
        var single = rows.Count == 1 && rows[0].SubKey == null;

        if (single)
        {
            DrawRow(TimerLabels.Group(key), rows[0], now);
            return;
        }

        ImGui.TextColored(Theme.GoldSoft, TimerLabels.Group(key));

        // Soonest first: the one you are waiting on is the one you are looking for.
        foreach (var row in rows.OrderBy(r => r.DueAt ?? DateTime.MaxValue))
        {
            using (ImRaii.PushIndent(12f))
            {
                DrawRow(row.SubKey ?? TimerLabels.One(key), row, now);
            }
        }
    }

    private static void DrawRow(string label, SnapshotTimer timer, DateTime now)
    {
        var (text, color) = Describe(timer, now);
        DrawRow(label, text, color);
    }

    /// <summary>Label on the left, value right-aligned so the column of values scans vertically.</summary>
    private static void DrawRow(string label, string value, Vector4 color)
    {
        ImGui.TextUnformatted(label);

        var width = ImGui.CalcTextSize(value).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - width);
        ImGui.TextColored(color, value);
    }

    private static (string Text, Vector4 Color) Describe(SnapshotTimer timer, DateTime now)
    {
        if (timer.Count.HasValue)
        {
            // An allowance at zero is not urgent, it is spent — the interesting one is a full stack
            // about to stop accruing, and that is the map timer rather than this count.
            return ($"{timer.Count} left", timer.Count.Value > 0 ? Theme.GoldSoft : Theme.Muted);
        }

        if (!timer.DueAt.HasValue)
        {
            return ("idle", Theme.Muted);
        }

        var left = timer.DueAt.Value - now;

        if (left <= TimeSpan.Zero)
        {
            return ("ready", Theme.Good);
        }

        return (TimerLabels.Remaining(left), left <= SoonThreshold ? Theme.Gold : Theme.Muted);
    }

    /// <summary>Kept in step with the setting, so it reopens on the next login if it was left open.</summary>
    public void SyncOpenState()
    {
        IsOpen = configuration.ShowTimersWindow;
    }
}
