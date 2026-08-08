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

    private readonly record struct Vessel(string Name, DateTime? DueAt, byte Rank);

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
        var submarines = new List<Vessel>();

        for (var i = 0; i < subs.Length; i++)
        {
            ref var sub = ref subs[i];
            if (sub.RankId == 0)
            {
                continue;
            }

            submarines.Add(new Vessel(ReadName(sub.Name, i), GameTime.FromUnix(sub.ReturnTime), sub.RankId));
        }

        var airships = &housing->WorkshopTerritory->Airship;
        var ships = airships->Data;
        var fleet = new List<Vessel>();

        for (var i = 0; i < ships.Length; i++)
        {
            ref var ship = ref ships[i];
            if (ship.RankId == 0)
            {
                continue;
            }

            fleet.Add(new Vessel(ReadName(ship.Name, i), GameTime.FromUnix(ship.ReturnTime), ship.RankId));
        }

        Emit(builder, TimerKeys.Submarine, submarines);
        Emit(builder, TimerKeys.Airship, fleet);

        return true;
    }

    /// <summary>
    /// One row per fleet rather than one per vessel, with the deadline set to the **last** vessel
    /// due back. A notification then means every one of them is home, which is the point at which
    /// there is something to do — being told about the first of four is just noise, and you would
    /// have to go back three more times.
    ///
    /// The individual vessels ride along in the payload, so the apps can still list them.
    /// </summary>
    private static void Emit(TimerSnapshotBuilder builder, string key, List<Vessel> vessels)
    {
        if (vessels.Count == 0)
        {
            return;
        }

        DateTime? lastBack = null;
        foreach (var vessel in vessels)
        {
            if (vessel.DueAt.HasValue && (lastBack == null || vessel.DueAt.Value > lastBack.Value))
            {
                lastBack = vessel.DueAt.Value;
            }
        }

        var detail = new List<object>(vessels.Count);
        foreach (var vessel in vessels)
        {
            detail.Add(new Dictionary<string, object?>
            {
                ["name"] = vessel.Name,
                ["dueAt"] = vessel.DueAt?.ToString("o"),
                ["rank"] = vessel.Rank,
            });
        }

        builder.AddDue(key, lastBack, payload: new Dictionary<string, object>
        {
            ["count"] = vessels.Count,
            ["vessels"] = detail,
        });
    }

    private static bool IsIslandSanctuary()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        return sheet.TryGetRow(Plugin.ClientState.TerritoryType, out var row)
               && row.TerritoryIntendedUse.RowId == IslandSanctuaryIntendedUse;
    }

    /// <summary>
    /// Names are fixed-size, null-terminated UTF-8 buffers. Falls back to a slot label so a blank
    /// name still identifies the vessel instead of showing as empty.
    /// </summary>
    private static string ReadName(Span<byte> raw, int slot)
    {
        var end = raw.IndexOf((byte)0);
        var text = Encoding.UTF8.GetString(end >= 0 ? raw[..end] : raw).Trim();

        return text.Length > 0 ? text : $"Slot {slot + 1}";
    }
}
