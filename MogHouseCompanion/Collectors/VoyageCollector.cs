using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Submarine and airship voyages, read from the FC workshop.
///
/// Known limitation, inherited from the game itself: these structs are only populated while the
/// player is inside the company workshop. Away from it there is no data to read, which is why the
/// apps show a "last sync" badge rather than pretending the values are live.
/// </summary>
public sealed unsafe class VoyageCollector : ITimerCollector
{
    /// <summary>
    /// Island Sanctuary reuses HousingManager.WorkshopTerritory for unrelated data, so the pointer
    /// being non-null is not enough. Guard learned from SubmarineTracker (MIT).
    /// </summary>
    private const uint IslandSanctuaryIntendedUse = 49;

    public string Name => "Workshop voyages";

    public IReadOnlyList<string> Keys { get; } = [TimerKeys.Submarine, TimerKeys.Airship];

    public bool Collect(TimerSnapshotBuilder builder)
    {
        var housing = HousingManager.Instance();
        if (housing == null || housing->WorkshopTerritory == null || IsIslandSanctuary())
        {
            return false;
        }

        // Taken by pointer: these structs are tens of kilobytes and must not be copied to the stack.
        var submersibles = &housing->WorkshopTerritory->Submersible;
        var subs = submersibles->Data;

        for (var i = 0; i < subs.Length; i++)
        {
            ref var sub = ref subs[i];
            if (sub.RankId == 0)
            {
                continue;
            }

            builder.AddDue(
                TimerKeys.Submarine,
                GameTime.FromUnix(sub.ReturnTime),
                ReadName(sub.Name, i),
                new Dictionary<string, object> { ["rank"] = sub.RankId });
        }

        var airships = &housing->WorkshopTerritory->Airship;
        var ships = airships->Data;

        for (var i = 0; i < ships.Length; i++)
        {
            ref var ship = ref ships[i];
            if (ship.RankId == 0)
            {
                continue;
            }

            builder.AddDue(
                TimerKeys.Airship,
                GameTime.FromUnix(ship.ReturnTime),
                ReadName(ship.Name, i),
                new Dictionary<string, object> { ["rank"] = ship.RankId });
        }

        return true;
    }

    private static bool IsIslandSanctuary()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        return sheet.TryGetRow(Plugin.ClientState.TerritoryType, out var row)
               && row.TerritoryIntendedUse.RowId == IslandSanctuaryIntendedUse;
    }

    /// <summary>
    /// Names are fixed-size, null-terminated UTF-8 buffers. Falls back to a slot label so a blank
    /// name still produces a stable subKey instead of collapsing several vessels into one row.
    /// </summary>
    private static string ReadName(Span<byte> raw, int slot)
    {
        var end = raw.IndexOf((byte)0);
        var text = Encoding.UTF8.GetString(end >= 0 ? raw[..end] : raw).Trim();

        return text.Length > 0 ? text : $"Slot {slot + 1}";
    }
}
