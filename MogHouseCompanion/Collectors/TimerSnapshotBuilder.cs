using System;
using System.Collections.Generic;
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
    private readonly DateTime now;

    public TimerSnapshotBuilder(DateTime utcNow)
    {
        now = utcNow;
    }

    public IReadOnlyList<Dto.SnapshotTimer> Timers => timers;

    public bool Truncated { get; private set; }

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
