using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Retainer ventures.
///
/// The game fills these in lazily — a retainer can be present with a venture assigned before its
/// completion timestamp has been populated, which happens around opening the bell. Reporting during
/// that window would overwrite a good deadline with a blank, so the collector reports nothing until
/// every assigned venture has a time.
/// </summary>
public sealed unsafe class VentureCollector : ITimerCollector
{
    public string Name => "Retainer ventures";

    public IReadOnlyList<string> Keys { get; } = [TimerKeys.Venture];

    public bool Collect(TimerSnapshotBuilder builder)
    {
        var manager = RetainerManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var retainers = manager->Retainers;
        var rows = new List<(string Name, DateTime DueAt)>();
        var loaded = 0;
        var incomplete = 0;

        for (var i = 0; i < retainers.Length; i++)
        {
            ref var retainer = ref retainers[i];

            // A zero id is an empty slot; it is also what every slot looks like before the retainer
            // list has loaded, which is the signal that there is nothing to read yet.
            if (retainer.RetainerId == 0)
            {
                continue;
            }

            loaded++;

            // Idle retainer: nothing to notify, and a row would only add noise.
            if (retainer.VentureId == 0)
            {
                continue;
            }

            var dueAt = GameTime.FromUnix(retainer.VentureComplete);
            if (dueAt == null)
            {
                incomplete++;
                continue;
            }

            rows.Add((retainer.NameString, dueAt.Value));
        }

        // Nothing loaded, or a venture whose completion time has not arrived yet: either way,
        // claiming authority now would clear deadlines we already reported correctly.
        if (loaded == 0 || incomplete > 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            builder.AddDue(TimerKeys.Venture, row.DueAt, row.Name);
        }

        return true;
    }
}
