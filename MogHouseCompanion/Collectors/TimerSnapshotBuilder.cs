using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Accumulates timer rows and enforces the ingest guardrails locally, so a malformed reading is
/// dropped here instead of being rejected by the server.
/// </summary>
public sealed class TimerSnapshotBuilder
{
    /// <summary>Server-side cap on rows per snapshot.</summary>
    public const int MaxTimers = 64;

    private static readonly TimeSpan MaxPast = TimeSpan.FromDays(2);
    private static readonly TimeSpan MaxFuture = TimeSpan.FromDays(45);

    private const int MaxSubKeyLength = 64;

    private readonly List<Dto.SnapshotTimer> timers = [];
    private readonly HashSet<string> declared = new(StringComparer.Ordinal);
    private readonly DateTime now;
    private readonly Func<string, bool> isEnabled;

    /// <param name="isEnabled">
    /// Whether a timer key may be uploaded at all. Rows for a disabled key are dropped here, so a
    /// collector cannot leak one by forgetting to check.
    /// </param>
    public TimerSnapshotBuilder(DateTime utcNow, Func<string, bool> isEnabled)
    {
        now = utcNow;
        this.isEnabled = isEnabled;
    }

    public IReadOnlyList<Dto.SnapshotTimer> Timers => timers;

    /// <summary>Keys this snapshot speaks for; the server replaces exactly these.</summary>
    public IReadOnlyCollection<string> DeclaredKeys => declared;

    public bool Truncated { get; private set; }

    /// <summary>
    /// Claims authority over a key. Called even when nothing was added for it: that is what tells
    /// the server to clear a timer the player switched off, as opposed to one that simply could
    /// not be read right now.
    /// </summary>
    public void Declare(string key)
    {
        declared.Add(key);
    }

    /// <summary>
    /// Adds a deadline-shaped timer. A null <paramref name="dueAt"/> records an entity that exists
    /// but is idle (a docked submarine), which the apps render without scheduling a notification.
    /// </summary>
    public void AddDue(string key, DateTime? dueAt, string? subKey = null, Dictionary<string, object>? payload = null)
    {
        if (dueAt.HasValue && !IsPlausible(dueAt.Value))
        {
            // Outside the window the server accepts: a stale reading, or the game struct was not
            // populated yet. Dropping it beats poisoning the row.
            return;
        }

        Add(new Dto.SnapshotTimer
        {
            Key = key,
            SubKey = Normalize(subKey),
            DueAt = dueAt,
            Payload = payload,
        });
    }

    /// <summary>Adds a counter-shaped timer, ignoring readings outside the plausible range.</summary>
    public void AddCount(string key, int count, int max)
    {
        if (count < 0 || count > max)
        {
            return;
        }

        Add(new Dto.SnapshotTimer { Key = key, Count = count });
    }

    private void Add(Dto.SnapshotTimer timer)
    {
        if (!isEnabled(timer.Key))
        {
            return;
        }

        if (timers.Count >= MaxTimers)
        {
            Truncated = true;
            return;
        }

        timers.Add(timer);
    }

    private bool IsPlausible(DateTime dueAt)
    {
        return dueAt >= now - MaxPast && dueAt <= now + MaxFuture;
    }

    private static string? Normalize(string? subKey)
    {
        if (string.IsNullOrWhiteSpace(subKey))
        {
            return null;
        }

        var trimmed = subKey.Trim();
        return trimmed.Length > MaxSubKeyLength ? trimmed[..MaxSubKeyLength] : trimmed;
    }

    /// <summary>
    /// Stable representation of the collected rows, used to skip uploads when nothing changed.
    /// Deliberately not a hash: the strings are short and an exact compare cannot collide.
    /// </summary>
    public string BuildSignature()
    {
        var sb = new StringBuilder();

        // Declared keys are part of the identity: a subsystem becoming readable, or a timer being
        // switched off, changes what the server should store even when no row moved.
        foreach (var key in declared.Order(StringComparer.Ordinal))
        {
            sb.Append(key).Append(',');
        }

        sb.Append('#');

        foreach (var t in timers)
        {
            sb.Append(t.Key).Append('|')
              .Append(t.SubKey).Append('|')
              .Append(t.DueAt?.Ticks).Append('|')
              .Append(t.Count).Append(';');
        }

        return sb.ToString();
    }
}
