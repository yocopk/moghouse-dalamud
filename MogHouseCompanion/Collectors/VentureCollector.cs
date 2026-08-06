using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Retainer ventures. Venture timers only become readable after the retainer bell has been opened
/// at least once in the session, so an empty result before that is expected, not a failure.
/// </summary>
public sealed unsafe class VentureCollector : ITimerCollector
{
    public string Name => "Retainer ventures";

    public IReadOnlyList<string> Keys { get; } = [TimerKeys.Venture];

    public bool Collect(TimerSnapshotBuilder builder)
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
        {
            return false;
        }

        var retainers = manager->Retainers;
        var seen = 0;

        for (var i = 0; i < retainers.Length; i++)
        {
            ref var retainer = ref retainers[i];
            if (retainer.RetainerId == 0)
            {
                continue;
            }

            seen++;

            // No venture assigned: nothing to notify, and emitting a row would only add noise.
            if (retainer.VentureId == 0)
            {
                continue;
            }

            builder.AddDue(
                TimerKeys.Venture,
                GameTime.FromUnix(retainer.VentureComplete),
                retainer.NameString);
        }

        return seen > 0;
    }
}
