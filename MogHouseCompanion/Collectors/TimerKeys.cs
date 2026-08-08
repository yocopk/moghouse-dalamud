namespace MogHouseCompanion.Collectors;

/// <summary>
/// Timer keys the server accepts. Anything not in this list is rejected by the ingest endpoint,
/// so keep it in sync with the catalogue in the backend plan (§3).
/// </summary>
public static class TimerKeys
{
    public const string Submarine = "submarine";
    public const string Airship = "airship";
    public const string Venture = "venture";
    public const string MapAllowance = "map_allowance";
    public const string LeveAllowance = "leve_allowance";
    public const string CustomDeliveries = "custom_deliveries";
    public const string AlliedDailies = "allied_dailies";

    // gc_mission and fashion_report are clock-based: the server derives them from fixed UTC resets,
    // so the plugin has nothing to collect for them.

    /// <summary>
    /// Every key the player can switch on or off in-game, in the order the settings window lists
    /// them. Also what the snapshot reports as its enabled set, so the apps can hide a timer that
    /// is never going to arrive instead of showing an alert switch that could never fire.
    /// </summary>
    public static readonly string[] All =
    [
        Submarine,
        Airship,
        Venture,
        MapAllowance,
        LeveAllowance,
        CustomDeliveries,
        AlliedDailies,
    ];
}
