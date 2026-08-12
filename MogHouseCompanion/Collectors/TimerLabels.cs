using System;

namespace MogHouseCompanion.Collectors;

/// <summary>
/// Turning timer data into words, shared by the readout window and the chat announcements so the
/// two never disagree about what something is called or how long is left.
/// </summary>
public static class TimerLabels
{
    /// <summary>Plural, because these head a list. <see cref="One"/> is the singular form.</summary>
    public static string Group(string key) => key switch
    {
        TimerKeys.Submarine => "Submarines",
        TimerKeys.Airship => "Airships",
        TimerKeys.Venture => "Ventures",
        TimerKeys.MapAllowance => "Treasure map",
        TimerKeys.LeveAllowance => "Leve allowances",
        TimerKeys.CustomDeliveries => "Custom deliveries",
        TimerKeys.AlliedDailies => "Allied society",
        _ => key,
    };

    /// <summary>What to call a single entry, for a sentence rather than a list.</summary>
    public static string One(string key) => key switch
    {
        TimerKeys.Submarine => "Submarine",
        TimerKeys.Airship => "Airship",
        TimerKeys.Venture => "Venture",
        TimerKeys.MapAllowance => "Treasure map allowance",
        _ => Group(key),
    };

    /// <summary>
    /// What to say when a deadline passes. Phrased per timer because one sentence does not fit all
    /// of them: a submarine comes back, a venture is finished by someone who never left, and an
    /// allowance was never away in the first place.
    /// </summary>
    public static string Finished(string key, string? subKey) => key switch
    {
        TimerKeys.Submarine or TimerKeys.Airship => subKey is { Length: > 0 }
            ? $"{subKey} has returned."
            : $"{One(key)} has returned.",

        TimerKeys.Venture => subKey is { Length: > 0 }
            ? $"{subKey} has finished a venture."
            : "A venture has finished.",

        TimerKeys.MapAllowance => "Your next treasure map allowance is ready.",

        _ => $"{One(key)} is ready.",
    };

    /// <summary>
    /// Time left, at the precision a player actually reads. Hours and minutes while it matters,
    /// minutes on their own under an hour, and no seconds anywhere: a ticking second counter invites
    /// you to watch it, and nothing here is worth watching.
    /// </summary>
    public static string Remaining(TimeSpan left)
    {
        if (left <= TimeSpan.Zero)
        {
            return "ready";
        }

        if (left.TotalDays >= 1)
        {
            return $"{(int)left.TotalDays}d {left.Hours}h";
        }

        if (left.TotalHours >= 1)
        {
            return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        }

        // Under a minute still reads as a minute rather than "0m", which looks like a bug.
        return $"{Math.Max(1, (int)left.TotalMinutes)}m";
    }
}
