using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// The Adventurer Squadron's current mission.
///
/// Read from <c>PlayerState</c>, which is resident for the whole session, so unlike voyages this
/// needs no visit to the barracks — the deadline is available from anywhere, which is also why the
/// game's own Timers window can show it while you are stood at a hunt board.
///
/// Worth recording where this is *not* read from, because the obvious place is wrong twice over.
/// <c>GcArmyManager</c> holds the squadron roster and looks like the natural home, but it has no
/// deadline field at all, and its data is loaded on demand inside the barracks. Older plugins reach
/// a static container through a byte-pattern scan; that container is now mapped into PlayerState by
/// name, which survives a patch that shifts the surrounding code and fails loudly at compile time
/// rather than silently at runtime.
/// </summary>
public sealed unsafe class SquadronCollector : ITimerCollector
{
    public string Name => "Squadron";

    public IReadOnlyList<string> Keys { get; } = [TimerKeys.SquadronMission];

    public bool Collect(TimerSnapshotBuilder builder)
    {
        var player = PlayerState.Instance();
        if (player == null)
        {
            return false;
        }

        // Nothing deployed. The key is still declared by the caller, so a mission that has since
        // been collected clears itself instead of leaving its old deadline on the site forever.
        if (player->ActiveGcArmyExpedition == 0)
        {
            return true;
        }

        builder.AddDue(
            TimerKeys.SquadronMission,
            GameTime.FromUnix(player->SquadronMissionCompletionTimestamp));

        return true;
    }
}
