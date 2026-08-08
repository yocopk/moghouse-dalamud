using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace MogHouseCompanion.Services;

/// <summary>
/// Notices when the player has been somewhere that changes a timer, and asks the sync service to
/// look closely for a while.
///
/// The two subsystems this covers are the ones whose data is only readable in one place. Voyages
/// live in the FC workshop and vanish from memory the moment you leave it, and the game applies a
/// dispatch a beat after the panel closes — so sending four submarines out and walking straight to
/// the aetheryte can slip between two polls and leave the site showing the previous voyage until
/// your next visit. Retainer ventures have the same shape around the summoning bell.
///
/// This is a hint, not a command: <see cref="TimerSyncService.WatchForChanges"/> still only uploads
/// when a reading has actually changed, and the ordinary cadence still catches everything on its
/// own. That keeps the plugin honest about how little it knows about these addons — it reads none
/// of their contents, only that the player was in one.
/// </summary>
public sealed class ActivityWatcher : IDisposable
{
    /// <summary>
    /// Addons that mean "a voyage or a venture may have just moved".
    ///
    /// Names the game does not use are never dispatched to, so covering the whole flow costs
    /// nothing if a panel is renamed in a patch — the plugin quietly falls back to polling.
    /// </summary>
    private static readonly string[] Addons =
    [
        // Company workshop: the voyage list, the per-vessel panel and the sector map it opens, and
        // the results screen you get on return.
        "AirShipExploration",
        "AirShipExplorationDetail",
        "AirShipExplorationMap",
        "AirShipExplorationResult",

        // Summoning bell: the retainer list, the venture picker, the "assign this venture?" prompt
        // and the completion screen you reassign from.
        "RetainerList",
        "RetainerTaskAsk",
        "RetainerTaskList",
        "RetainerTaskResult",
    ];

    private readonly IAddonLifecycle addonLifecycle;
    private readonly TimerSyncService syncService;

    public ActivityWatcher(IAddonLifecycle addonLifecycle, TimerSyncService syncService)
    {
        this.addonLifecycle = addonLifecycle;
        this.syncService = syncService;

        // Opening one of these means the data behind it has just been loaded; closing one means the
        // player has finished doing whatever they came to do. Both are worth a closer look, and the
        // pair keeps the window armed across a multi-step dispatch.
        this.addonLifecycle.RegisterListener(AddonEvent.PostSetup, Addons, OnAddonEvent);
        this.addonLifecycle.RegisterListener(AddonEvent.PreFinalize, Addons, OnAddonEvent);
    }

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        syncService.WatchForChanges($"{args.AddonName} {type}");
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, Addons, OnAddonEvent);
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, Addons, OnAddonEvent);
    }
}
