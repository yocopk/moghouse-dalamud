using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Allowance-shaped timers. Unlike voyages and ventures these live in state that is loaded for the
/// whole session, so they are readable as soon as the character is logged in.
/// </summary>
public sealed unsafe class AllowanceCollector : ITimerCollector
{
    // Upper bounds used as sanity checks: a reading above these means the struct was not populated,
    // so the row is dropped rather than uploaded as a bogus count.
    private const int MaxLeveAllowances = 100;
    private const int MaxCustomDeliveryAllowances = 12;
    private const int MaxAlliedSocietyAllowances = 12;

    public string Name => "Allowances";

    public bool Collect(TimerSnapshotBuilder builder)
    {
        var collected = false;

        var uiState = UIState.Instance();
        if (uiState != null)
        {
            builder.AddDue(TimerKeys.MapAllowance, GameTime.FromUnix(uiState->GetNextMapAllowanceTimestamp()));
            collected = true;
        }

        var quests = QuestManager.Instance();
        if (quests != null)
        {
            builder.AddCount(TimerKeys.LeveAllowance, quests->NumLeveAllowances, MaxLeveAllowances);
            builder.AddCount(TimerKeys.AlliedDailies, (int)quests->GetBeastTribeAllowance(), MaxAlliedSocietyAllowances);
            collected = true;
        }

        var supply = SatisfactionSupplyManager.Instance();
        if (supply != null)
        {
            builder.AddCount(
                TimerKeys.CustomDeliveries,
                supply->GetRemainingAllowances(),
                MaxCustomDeliveryAllowances);
            collected = true;
        }

        return collected;
    }
}
