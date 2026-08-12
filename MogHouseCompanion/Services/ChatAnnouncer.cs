using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MogHouseCompanion.Collectors;
using MogHouseCompanion.Dto;

namespace MogHouseCompanion.Services;

/// <summary>
/// Says things in the game chat, so the player at the keyboard finds out the same things the phone
/// would have told them if they had walked away.
///
/// Everything runs on the framework thread by polling the sync service, rather than by subscribing
/// to it: the upload finishes on a background task, and printing to the game's chat from there would
/// be reaching into the game off-thread for the sake of saving a poll.
/// </summary>
public sealed class ChatAnnouncer : IDisposable
{
    /// <summary>Tag Dalamud renders in front of every line. Gold, to match the plugin.</summary>
    private const string Tag = "MogHouse";
    private const ushort TagColor = 541;

    private readonly Configuration configuration;
    private readonly TimerSyncService syncService;
    private readonly IFramework framework;
    private readonly IChatGui chat;

    /// <summary>
    /// Deadlines already spoken for, keyed by timer and entity. Seeded from the first reading so a
    /// login does not replay every voyage that docked overnight — the push already covered those,
    /// and a wall of text about yesterday is worse than silence.
    /// </summary>
    private readonly HashSet<string> announced = [];

    /// <summary>
    /// How often the checks actually run. They are cheap, but this sits in the game's frame loop and
    /// nothing here needs sub-second precision — a voyage announced a beat late is still on time.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private DateTime lastCheckAt = DateTime.MinValue;
    private bool seeded;
    private DateTime? lastAnnouncedSync;

    public ChatAnnouncer(
        Configuration configuration,
        TimerSyncService syncService,
        IFramework framework,
        IChatGui chat)
    {
        this.configuration = configuration;
        this.syncService = syncService;
        this.framework = framework;
        this.chat = chat;

        this.framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework tick)
    {
        var now = DateTime.UtcNow;
        if (now - lastCheckAt < CheckInterval)
        {
            return;
        }

        lastCheckAt = now;

        try
        {
            AnnounceSync();
            AnnounceFinished();
        }
        catch (Exception ex)
        {
            // Chat is a nicety; it must never take the frame down with it.
            Plugin.Log.Error(ex, "Chat announcement failed");
        }
    }

    private void AnnounceSync()
    {
        var at = syncService.Status.LastSuccessAt;
        if (!at.HasValue)
        {
            return;
        }

        if (at == lastAnnouncedSync)
        {
            return;
        }

        // Recorded before the opt-out is consulted, so switching the setting on mid-session picks up
        // from the next sync rather than announcing one that already happened.
        lastAnnouncedSync = at;

        if (!configuration.AnnounceSyncInChat)
        {
            return;
        }

        var count = syncService.Status.TimerCount;
        Print(new SeStringBuilder()
            .AddText("Synced ")
            .AddUiForeground(count.ToString(), TagColor)
            .AddText(count == 1 ? " timer." : " timers.")
            .Build());
    }

    private void AnnounceFinished()
    {
        var reading = syncService.LastReading;

        if (reading.At == null)
        {
            // Unlinked, or pointed at another server. Whatever was said belonged to the old link,
            // and the next reading should start a fresh session rather than a continued one.
            announced.Clear();
            seeded = false;
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var timer in reading.Timers)
        {
            if (timer.DueAt == null || timer.DueAt > now)
            {
                continue;
            }

            // The deadline is part of the identity, so a vessel sent out again earns a new line when
            // it docks, while the same completed voyage read twenty times stays quiet.
            if (!announced.Add(Identify(timer)))
            {
                continue;
            }

            // Everything already finished at the first reading is *recorded* as said without being
            // said: the push covered it, and a login should not replay the night.
            if (!seeded || !configuration.AnnounceFinishedTimersInChat)
            {
                continue;
            }

            Print(new SeStringBuilder()
                .AddText(TimerLabels.Finished(timer.Key, timer.SubKey))
                .Build());
        }

        seeded = true;
    }

    /// <summary>One arrival, across readings.</summary>
    private static string Identify(SnapshotTimer timer)
    {
        return $"{timer.Key}|{timer.SubKey}|{timer.DueAt:O}";
    }

    private void Print(SeString message)
    {
        chat.Print(message, Tag, TagColor);
    }
}
