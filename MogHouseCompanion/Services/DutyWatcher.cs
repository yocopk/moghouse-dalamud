using System;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using MogHouseCompanion.Collectors;
using MogHouseCompanion.Dto;

// Both FFXIVClientStructs and Lumina define a ContentRoulette; the sheet is the one with the names.
using ContentFinderConditionSheet = Lumina.Excel.Sheets.ContentFinderCondition;
using ContentRouletteSheet = Lumina.Excel.Sheets.ContentRoulette;

namespace MogHouseCompanion.Services;

/// <summary>
/// Tells MogHouse the moment the Duty Finder pops, so the push can reach a phone while the confirm
/// window is still open.
///
/// This is the one part of the plugin that cannot go through the timer pipeline. A timer is a
/// deadline the server can work out for itself and notify about at leisure; a duty pop is an event
/// that dies 45 seconds later, so it is reported directly and delivered on that request.
///
/// It reads and reports. It does not press anything: commencing a duty from a phone would be the
/// client acting without the player at it, which is both against the Dalamud plugin guidelines and
/// a good way to drop an AFK body into someone else's party.
/// </summary>
public sealed class DutyWatcher : IDisposable
{
    private const string ConfirmAddon = "ContentsFinderConfirm";

    /// <summary>Last thing to say when the game will not tell us which duty popped.</summary>
    private const string UnknownDuty = "Your duty";

    private readonly Configuration configuration;
    private readonly MogHouseApi api;
    private readonly IAddonLifecycle addonLifecycle;

    /// <summary>
    /// The pop already reported. The addon can be set up more than once for a single pop, and one
    /// notification per pop is the whole contract.
    /// </summary>
    private DateTime? lastReported;

    public DutyWatcher(Configuration configuration, MogHouseApi api, IAddonLifecycle addonLifecycle)
    {
        this.configuration = configuration;
        this.api = api;
        this.addonLifecycle = addonLifecycle;

        this.addonLifecycle.RegisterListener(AddonEvent.PostSetup, ConfirmAddon, OnConfirmShown);
    }

    private void OnConfirmShown(AddonEvent type, AddonArgs args)
    {
        try
        {
            Report();
        }
        catch (Exception ex)
        {
            // A duty pop is not worth taking the plugin down for.
            Plugin.Log.Error(ex, "Duty pop notification failed");
        }
    }

    /// <summary>Reads the pop off the game structs. Pointer work only; delivery is <see cref="Send"/>.</summary>
    private unsafe void Report()
    {
        if (!configuration.IsLinked || !configuration.DutyFinderPush)
        {
            return;
        }

        // You are looking straight at the popup. The game already made a noise; a second one on the
        // phone in your pocket is not news.
        if (configuration.DutyPushOnlyWhenAway && GameWindow.IsInForeground())
        {
            return;
        }

        var info = ContentsFinder.Instance()->GetQueueInfo();
        if (info == null)
        {
            return;
        }

        var readyAt = GameTime.FromUnix(info->QueueReadyTimestamp) ?? DateTime.UtcNow;

        if (lastReported == readyAt)
        {
            return;
        }

        lastReported = readyAt;

        Send(new GameEventRequest
        {
            Type = GameEventRequest.DutyReady,
            Data = new DutyReadyData { Duty = DescribeDuty(info), ReadyAt = readyAt },
        });
    }

    /// <summary>
    /// Fire and forget, and deliberately never retried: a duty notification that arrives late sends
    /// the player running for a slot they have already lost.
    /// </summary>
    private void Send(GameEventRequest payload)
    {
        _ = Task.Run(async () =>
        {
            var result = await api.PostEventAsync(payload).ConfigureAwait(false);

            if (!result.Success)
            {
                Plugin.Log.Warning($"Duty pop not delivered: {result.Error?.Code} — {result.Error?.Message}");
            }
        });
    }

    /// <summary>
    /// What the game itself is showing on the confirm window: the roulette for a roulette queue,
    /// the duty for a direct one. Roulettes deliberately do not name the duty until you commence,
    /// and the notification must not either.
    /// </summary>
    private static unsafe string DescribeDuty(ContentsFinderQueueInfo* info)
    {
        var popped = info->PoppedQueueEntry;

        switch (popped.ContentType)
        {
            case ContentsType.Roulette:
                var roulettes = Plugin.DataManager.GetExcelSheet<ContentRouletteSheet>();
                return roulettes.TryGetRow(popped.Id, out var roulette)
                    ? Text(roulette.Name.ToString())
                    : UnknownDuty;

            case ContentsType.Regular:
                var duties = Plugin.DataManager.GetExcelSheet<ContentFinderConditionSheet>();
                return duties.TryGetRow(popped.Id, out var duty)
                    ? Text(duty.Name.ToString())
                    : UnknownDuty;

            default:
                return UnknownDuty;
        }
    }

    /// <summary>Sheet names arrive lowercase for most duties; the game title-cases them on display.</summary>
    private static string Text(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return UnknownDuty;
        }

        return char.IsLower(trimmed[0])
            ? char.ToUpperInvariant(trimmed[0]) + trimmed[1..]
            : trimmed;
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, ConfirmAddon, OnConfirmShown);
    }
}
