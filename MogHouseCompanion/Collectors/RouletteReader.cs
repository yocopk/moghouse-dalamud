using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ContentRouletteSheet = Lumina.Excel.Sheets.ContentRoulette;

namespace MogHouseCompanion.Collectors;

/// <summary>One roulette and whether today's bonus is still on the table.</summary>
public sealed record RouletteState(uint RowId, string Name, bool Complete, bool IsExtra);

/// <summary>
/// Which duty roulettes still have their daily bonus.
///
/// The index into the game's completion array is <see cref="ContentRouletteSheet.CompletionArrayIndex"/>,
/// *not* the row id — they differ, and rows the game does not track daily carry -1. Reading it with
/// the row id returns answers that look plausible and are about a different roulette entirely.
///
/// Read on demand rather than polled: it is a sheet scan plus a byte lookup each, and the state only
/// moves when the player finishes a duty.
/// </summary>
public static unsafe class RouletteReader
{
    public static IReadOnlyList<RouletteState> Read()
    {
        var instance = InstanceContent.Instance();
        if (instance == null)
        {
            return [];
        }

        var sheet = Plugin.DataManager.GetExcelSheet<ContentRouletteSheet>();
        if (sheet == null)
        {
            return [];
        }

        var rows = new List<(RouletteState State, byte Sort)>();

        foreach (var row in sheet)
        {
            // Not offered in the Duty Finder, or not one the game keeps a daily flag for.
            if (!row.IsInDutyFinder || row.CompletionArrayIndex < 0)
            {
                continue;
            }

            var name = row.Name.ToString().Trim();
            if (name.Length == 0)
            {
                continue;
            }

            rows.Add((
                new RouletteState(
                    row.RowId,
                    name,
                    instance->IsRouletteComplete((byte)row.CompletionArrayIndex),
                    // PvP and the Gold Saucer are daily too, but most players never touch them, so
                    // they start switched off rather than padding everyone's checklist.
                    row.IsPvP || row.IsGoldSaucer),
                row.SortKey));
        }

        // The order the game itself lists them in, so the checklist matches the Duty Finder.
        return rows.OrderBy(r => r.Sort).Select(r => r.State).ToList();
    }
}
