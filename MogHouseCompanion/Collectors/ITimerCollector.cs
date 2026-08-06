using System;
using System.Collections.Generic;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Reads one game subsystem. Collectors run on the framework thread and are isolated on purpose:
/// game structs move around every patch, so a collector that breaks must not take the others with it.
/// </summary>
public interface ITimerCollector
{
    /// <summary>Label shown in the status window.</summary>
    string Name { get; }

    /// <summary>
    /// Timer keys this collector is responsible for. Declared up front so the sync can tell the
    /// server which timers it is authoritative for, including the ones it deliberately sent
    /// nothing for.
    /// </summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>
    /// Appends rows for this subsystem.
    /// Returns false when the data is not available yet — the FC workshop has not been visited,
    /// the retainer bell has not been opened — so the status window can say so instead of implying
    /// the player owns nothing. Keys omitted from a snapshot are left untouched server-side.
    /// </summary>
    bool Collect(TimerSnapshotBuilder builder);
}

public static class GameTime
{
    /// <summary>
    /// Game structs store deadlines as unix seconds, with 0 meaning "not set". Converting here keeps
    /// every collector honest about sending absolute UTC.
    /// </summary>
    public static DateTime? FromUnix(long seconds)
    {
        if (seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }
}
