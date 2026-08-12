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
    /// Arrivals seen while they were still running, and therefore worth a word when they land.
    ///
    /// Announcing is gated on having watched something count down rather than on merely finding it
    /// finished: a voyage is usually already back the first time it can be read — at login, or on
    /// walking into the company workshop hours later — and the push reported that one long ago.
    /// </summary>
    private readonly HashSet<string> pending = [];

    /// <summary>
    /// How often the checks actually run. They are cheap, but this sits in the game's frame loop and
    /// nothing here needs sub-second precision — a voyage announced a beat late is still on time.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private DateTime lastCheckAt = DateTime.MinValue;
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
            // Unlinked, or pointed at another server. Nothing being watched belongs to this link
            // any more.
            pending.Clear();
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var timer in reading.Timers)
        {
            if (timer.DueAt == null)
            {
                continue;
            }

            // The deadline is part of the identity, so a vessel sent out again is watched afresh,
            // while the same completed voyage read twenty times is only ever spoken once.
            var id = Identify(timer);

            if (timer.DueAt > now)
            {
                pending.Add(id);
                continue;
            }

            if (!pending.Remove(id))
            {
                continue;
            }

            // Dropped from the watch list either way, so switching the setting on later starts from
            // the next arrival rather than replaying the ones that landed while it was off.
            if (!configuration.AnnounceFinishedTimersInChat)
            {
                continue;
            }

            Print(new SeStringBuilder()
                .AddText(TimerLabels.Finished(timer.Key, timer.SubKey))
                .Build());
        }
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
