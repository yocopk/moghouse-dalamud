using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MogHouseCompanion.Collectors;
using MogHouseCompanion.Dto;

namespace MogHouseCompanion.Services;

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
/// Rather than hooking every addon that could change a timer (retainer bell, voyage dispatch), it
/// polls the collectors cheaply and uploads only when the readings actually differ. That covers any
/// trigger without tying the plugin to addon internals that shift between patches.
/// </summary>
public sealed class TimerSyncService : IDisposable
{
    /// <summary>How often the game structs are read. Cheap; the upload rules below are the gate.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

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
    private string lastUploadedSignature = string.Empty;
    private int consecutiveFailures;

    private volatile bool forceRequested;
    private int syncing;

    // Read by the UI thread every frame, written by the upload task: volatile so the reader cannot
    // cache a stale reference.
    private volatile SyncStatus status = SyncStatus.Idle;

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

    /// <summary>Asks for an upload on the next poll, bypassing the change check.</summary>
    public void RequestSync()
    {
        forceRequested = true;
        lastPollAt = DateTime.MinValue;
        retryAfter = DateTime.MinValue;
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
        consecutiveFailures = 0;
        forceRequested = false;
        status = SyncStatus.Idle;
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
        if (now - lastPollAt < PollInterval)
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

        if (!force && now - lastAttemptAt < MinUploadInterval)
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

        // Nothing read and nothing to clear: an upload would only bump the sync timestamp.
        if (builder.Timers.Count == 0 && builder.DeclaredKeys.Count == 0)
        {
            return null;
        }

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
            Timers = builder.Timers.ToList(),
        };

        return (snapshot, builder.BuildSignature(), available.ToArray(), unavailable.ToArray());
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
