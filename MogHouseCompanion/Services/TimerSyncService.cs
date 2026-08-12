using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MogHouseCompanion.Collectors;
using MogHouseCompanion.Dto;

namespace MogHouseCompanion.Services;

/// <summary>
/// The last thing the collectors saw, whether or not it was worth uploading.
///
/// Separate from <see cref="SyncStatus"/>, which is about the health of the connection: this is the
/// data itself, kept so the plugin can show the player their own timers without a round trip to the
/// website. Deadlines are absolute, so a reading stays correct as it ages — only a change made in
/// game since the last poll is missing from it.
/// </summary>
public sealed record TimerReading
{
    public static readonly TimerReading Empty = new();

    public DateTime? At { get; init; }

    public IReadOnlyList<SnapshotTimer> Timers { get; init; } = [];
}

/// <summary>Immutable view of the sync state, swapped atomically so the UI never reads a torn value.</summary>
public sealed record SyncStatus
{
    public static readonly SyncStatus Idle = new();

    public DateTime? LastSuccessAt { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public string? LastError { get; init; }
    public bool IsSyncing { get; init; }
    public bool PremiumRequired { get; init; }
    public int TimerCount { get; init; }
    public string[] Available { get; init; } = [];
    public string[] Unavailable { get; init; } = [];
}

/// <summary>
/// Decides when to read the game and when to upload.
///
/// Reading is polled rather than hooked: the collectors are cheap, and comparing readings catches
/// every way a timer can change without tying the plugin to addon internals that shift between
/// patches. What <see cref="ActivityWatcher"/> supplies on top is only a *hint* about when to look
/// harder — see <see cref="WatchForChanges"/>.
/// </summary>
public sealed class TimerSyncService : IDisposable
{
    /// <summary>How often the game structs are read. Cheap; the upload rules below are the gate.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a hinted window stays open, and how fast it reads while it is.
    ///
    /// Dispatching a voyage is the case that needs it: the game only fills in the new return time a
    /// moment after the panel closes, and the workshop structs go unreadable as soon as you walk
    /// out — so a thirty-second cadence can miss the change entirely and leave the site showing the
    /// previous voyage until you next set foot in there.
    /// </summary>
    private static readonly TimeSpan WatchWindow = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan WatchPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Upload floor inside a watched window. Low enough to feel immediate, high enough to
    /// still coalesce a burst of four submarines being sent out one after another.</summary>
    private static readonly TimeSpan WatchUploadInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The heartbeat: upload at least this often even when nothing changed, so the site's freshness
    /// badge stays honest. It is a floor on staleness, not the only reason to sync.
    /// </summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Floor between two uploads. Changes are reported as they happen — dispatching a voyage or a
    /// venture should show up on the site within about a minute, not at the next heartbeat — and
    /// this is what keeps a burst of them to a single request.
    ///
    /// An earlier version only uploaded on the hour, plus early if a deadline landed inside it.
    /// That looked equivalent and was not: data that had merely *become readable* — the workshop
    /// filling in the moment you walk in — was never urgent, so leaving again before the hour was
    /// up meant it never reached the server at all.
    /// </summary>
    private static readonly TimeSpan MinUploadInterval = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan RetryBase = TimeSpan.FromSeconds(30);
    private const int MaxRetryExponent = 5;

    private readonly Configuration configuration;
    private readonly MogHouseApi api;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly ITimerCollector[] collectors;

    private DateTime lastPollAt = DateTime.MinValue;
    private DateTime lastAttemptAt = DateTime.MinValue;
    private DateTime lastSuccessAt = DateTime.MinValue;
    private DateTime retryAfter = DateTime.MinValue;
    private DateTime watchUntil = DateTime.MinValue;
    private string lastUploadedSignature = string.Empty;
    private int consecutiveFailures;

    private volatile bool forceRequested;
    private int syncing;

    // Read by the UI thread every frame, written by the upload task: volatile so the reader cannot
    // cache a stale reference.
    private volatile SyncStatus status = SyncStatus.Idle;

    // Written on the framework thread by Collect, read by the windows on the same thread; volatile
    // for the same reason as above, since the reference is swapped rather than mutated.
    private volatile TimerReading reading = TimerReading.Empty;

    public TimerSyncService(
        Configuration configuration,
        MogHouseApi api,
        IFramework framework,
        IClientState clientState)
    {
        this.configuration = configuration;
        this.api = api;
        this.framework = framework;
        this.clientState = clientState;

        collectors =
        [
            new VoyageCollector(),
            new VentureCollector(),
            new AllowanceCollector(),
        ];

        this.framework.Update += OnFrameworkUpdate;
        this.clientState.Login += OnLogin;
    }

    public SyncStatus Status => status;

    public TimerReading LastReading => reading;

    /// <summary>Asks for an upload on the next poll, bypassing the change check.</summary>
    public void RequestSync()
    {
        forceRequested = true;
        lastPollAt = DateTime.MinValue;
        retryAfter = DateTime.MinValue;
    }

    /// <summary>
    /// Says that something just happened which is *likely* to have moved a timer, so read closely
    /// for a while and report the moment a reading actually differs.
    ///
    /// Deliberately not "upload now": the game has usually not applied the change yet at the point
    /// the UI closes, and an immediate snapshot would faithfully record the old value. It is also
    /// only a hint — the ordinary cadence still catches everything, so a caller passing an event
    /// that turns out to mean nothing costs a handful of struct reads and no request.
    /// </summary>
    /// <param name="reason">What triggered it. Log-only, so a misfiring trigger can be identified.</param>
    public void WatchForChanges(string reason)
    {
        var now = DateTime.UtcNow;
        var opening = now >= watchUntil;

        watchUntil = now + WatchWindow;
        lastPollAt = DateTime.MinValue;

        // Only the first of a run is logged: a dispatch re-arms this several times as the panels
        // open and close, and a line per panel would bury everything else.
        if (opening)
        {
            Plugin.Log.Debug($"Watching for timer changes: {reason}.");
        }
    }

    /// <summary>
    /// Forgets everything learned about the current link: the last uploaded readings, the retry
    /// backoff and the reported status. Called when the device is unlinked or pointed at another
    /// server, where none of it means anything any more.
    /// </summary>
    public void ResetState()
    {
        lastUploadedSignature = string.Empty;
        lastSuccessAt = DateTime.MinValue;
        lastAttemptAt = DateTime.MinValue;
        retryAfter = DateTime.MinValue;
        watchUntil = DateTime.MinValue;
        consecutiveFailures = 0;
        forceRequested = false;
        status = SyncStatus.Idle;
        reading = TimerReading.Empty;
    }

    private void OnLogin()
    {
        RequestSync();
    }

    private void OnFrameworkUpdate(IFramework tick)
    {
        if (!configuration.IsLinked || !Plugin.PlayerState.IsLoaded)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var watching = now < watchUntil;

        if (now - lastPollAt < (watching ? WatchPollInterval : PollInterval))
        {
            return;
        }

        lastPollAt = now;

        if (Volatile.Read(ref syncing) == 1 || now < retryAfter)
        {
            return;
        }

        var collected = Collect(now);
        if (collected == null)
        {
            return;
        }

        var (snapshot, signature, available, unavailable) = collected.Value;

        var force = forceRequested;

        // Anything new to say — a dispatched voyage, a subsystem that just became readable, a timer
        // switched off — or the heartbeat coming round.
        var changed = signature != lastUploadedSignature;
        var due = now - lastSuccessAt >= SyncInterval;

        if (!force && !changed && !due)
        {
            return;
        }

        if (!force && now - lastAttemptAt < (watching ? WatchUploadInterval : MinUploadInterval))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref syncing, 1, 0) == 1)
        {
            return;
        }

        forceRequested = false;
        lastAttemptAt = now;

        Publish(isSyncing: true, snapshot.Timers.Count, available, unavailable);

        _ = Task.Run(() => UploadAsync(snapshot, signature, available, unavailable));
    }

    /// <summary>Reads every collector on the framework thread. One broken collector must not stop the rest.</summary>
    private (SnapshotRequest Snapshot, string Signature, string[] Available, string[] Unavailable)? Collect(DateTime now)
    {
        var player = Plugin.PlayerState;
        if (!player.IsLoaded || !player.HomeWorld.IsValid)
        {
            return null;
        }

        var builder = new TimerSnapshotBuilder(now, configuration.IsTimerEnabled);
        var available = new List<string>();
        var unavailable = new List<string>();

        foreach (var collector in collectors)
        {
            var read = false;

            try
            {
                read = collector.Collect(builder);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, $"Collector '{collector.Name}' failed");
            }

            foreach (var key in collector.Keys)
            {
                // A switched-off timer is always declared, so the server clears whatever it still
                // holds. An enabled one is only declared when it was actually readable — otherwise
                // standing outside the workshop would wipe the voyages we reported earlier.
                if (!configuration.IsTimerEnabled(key) || read)
                {
                    builder.Declare(key);
                }
            }

            (read ? available : unavailable).Add(collector.Name);
        }

        if (builder.Truncated)
        {
            Plugin.Log.Warning($"Timer snapshot hit the {TimerSnapshotBuilder.MaxTimers}-row cap; extra rows dropped.");
        }

        // Merged rather than replaced, using the same rule the server applies to a snapshot: a key
        // this poll is authoritative for is replaced, one it could not read is left alone.
        //
        // Without this the readout was empty of exactly the timers people care most about. Voyages
        // are only readable inside the company workshop, so every poll taken anywhere else reported
        // no submarines — and a window rendering the raw reading would show none, while the website
        // happily showed them because the *server* had been preserving them all along.
        //
        // Safe because deadlines are absolute: a preserved row keeps counting down correctly, and
        // the only thing that could invalidate it — sending the vessel out again — cannot happen
        // without standing in the workshop, which refreshes the reading anyway.
        reading = Merge(reading, builder.Timers, builder.DeclaredKeys, now);

        // Published before the early return below, so the readout and the chat announcements keep
        // working on a poll that produced nothing worth uploading.

        // Nothing read and nothing to clear: an upload would only bump the sync timestamp.
        if (builder.Timers.Count == 0 && builder.DeclaredKeys.Count == 0)
        {
            return null;
        }

        var enabled = TimerKeys.All.Where(configuration.IsTimerEnabled).ToList();

        var snapshot = new SnapshotRequest
        {
            Character = new SnapshotCharacter
            {
                ContentId = player.ContentId.ToString(),
                Name = player.CharacterName,
                World = player.HomeWorld.Value.Name.ToString(),
            },
            ClientTime = now,
            Keys = builder.DeclaredKeys.ToList(),
            Enabled = enabled,
            Timers = builder.Timers.ToList(),
        };

        // The enabled set joins the signature: switching a timer off that was never readable here
        // changes nothing the builder can see, but the site still needs to stop offering it.
        var signature = $"{string.Join(',', enabled)}${builder.BuildSignature()}";

        return (snapshot, signature, available.ToArray(), unavailable.ToArray());
    }

    /// <summary>
    /// Last known state per timer key: fresh rows for the keys just read, previously held rows for
    /// the keys that could not be. Mirrors the ingest contract, so the plugin's own readout and the
    /// website cannot disagree about what you have running.
    /// </summary>
    private static TimerReading Merge(
        TimerReading previous,
        IReadOnlyList<SnapshotTimer> fresh,
        IReadOnlyCollection<string> declared,
        DateTime now)
    {
        var kept = previous.Timers.Where(t => !declared.Contains(t.Key));

        return new TimerReading
        {
            At = now,
            Timers = fresh.Concat(kept).ToList(),
        };
    }

    private async Task UploadAsync(SnapshotRequest snapshot, string signature, string[] available, string[] unavailable)
    {
        try
        {
            var result = await api.PostSnapshotAsync(snapshot).ConfigureAwait(false);

            if (result.Success && result.Data != null)
            {
                lastUploadedSignature = signature;
                lastSuccessAt = DateTime.UtcNow;
                consecutiveFailures = 0;
                retryAfter = DateTime.MinValue;

                Publish(isSyncing: false, snapshot.Timers.Count, available, unavailable, error: null);

                if (result.Data.Rejected.Count > 0)
                {
                    Plugin.Log.Warning($"Server rejected timers: {string.Join(", ", result.Data.Rejected)}");
                }

                foreach (var warning in result.Data.Warnings)
                {
                    Plugin.Log.Warning($"Server warning: {warning}");
                }

                Plugin.Log.Debug($"Snapshot accepted: {result.Data.Accepted} timers");
                return;
            }

            HandleFailure(result.Error, snapshot.Timers.Count, available, unavailable);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Snapshot upload faulted");
            HandleFailure(
                new ApiError { Code = ApiErrorCode.Internal, Message = "Upload failed unexpectedly." },
                snapshot.Timers.Count,
                available,
                unavailable);
        }
        finally
        {
            Volatile.Write(ref syncing, 0);
        }
    }

    private void HandleFailure(ApiError? error, int timerCount, string[] available, string[] unavailable)
    {
        var code = error?.Code ?? ApiErrorCode.Internal;

        switch (code)
        {
            case ApiErrorCode.Unauthorized:
                // The device was revoked from the website: drop the dead token and stop retrying.
                Plugin.Log.Information("Token rejected; clearing the local link.");
                configuration.Token = string.Empty;
                configuration.Save();
                Publish(false, timerCount, available, unavailable, "This device was unlinked from the website.");
                return;

            case ApiErrorCode.PremiumRequired:
                // Keep the token: Mog+ can lapse and come back, and re-pairing would be busywork.
                retryAfter = DateTime.UtcNow + SyncInterval;
                Publish(false, timerCount, available, unavailable, "FFXIV Sync needs an active Mog+ subscription.", premiumRequired: true);
                return;
        }

        consecutiveFailures = Math.Min(consecutiveFailures + 1, MaxRetryExponent);
        retryAfter = DateTime.UtcNow + RetryBase * Math.Pow(2, consecutiveFailures - 1);

        var message = error?.Message is { Length: > 0 } m ? m : "Sync failed.";
        Plugin.Log.Warning($"Snapshot upload failed ({code}): {message}");
        Publish(false, timerCount, available, unavailable, message);
    }

    private void Publish(
        bool isSyncing,
        int timerCount,
        string[] available,
        string[] unavailable,
        string? error = null,
        bool premiumRequired = false)
    {
        status = new SyncStatus
        {
            LastSuccessAt = lastSuccessAt == DateTime.MinValue ? null : lastSuccessAt,
            LastAttemptAt = lastAttemptAt == DateTime.MinValue ? null : lastAttemptAt,
            LastError = error,
            IsSyncing = isSyncing,
            PremiumRequired = premiumRequired,
            TimerCount = timerCount,
            Available = available,
            Unavailable = unavailable,
        };
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.Login -= OnLogin;
    }
}
