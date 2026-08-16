using System.Text.Json.Serialization;

namespace MogHouseCompanion.Dto;

/// <summary>
/// What the account is allowed to do, straight from the server.
///
/// Read-only as far as this plugin is concerned. Every limit below is enforced again on the route
/// that would exceed it, so editing any of it changes nothing except what this window says — which
/// is the only sane arrangement for a client whose source anyone can rebuild. It is here so the
/// player can be told the rule instead of running into it.
/// </summary>
public sealed class PlanInfo
{
    [JsonPropertyName("plan")] public string Plan { get; set; } = "free";

    [JsonPropertyName("limits")] public PlanLimits Limits { get; set; } = new();

    [JsonPropertyName("usage")] public PlanUsage Usage { get; set; } = new();

    /// <summary>
    /// The character a capped account follows — the oldest one linked, which is how the server
    /// picks it too. Null before anything has synced.
    /// </summary>
    [JsonPropertyName("primaryCharacter")] public PlanCharacter? PrimaryCharacter { get; set; }

    public bool IsPlus => Plan == "plus";
}

public sealed class PlanLimits
{
    /// <summary>Null means no ceiling, which is what Mog+ is.</summary>
    [JsonPropertyName("characters")] public int? Characters { get; set; }

    [JsonPropertyName("alerts")] public int? Alerts { get; set; }

    /// <summary>Whether an alert may fire before its deadline rather than on it.</summary>
    [JsonPropertyName("leadMinutes")] public bool LeadMinutes { get; set; }
}

public sealed class PlanUsage
{
    [JsonPropertyName("characters")] public int Characters { get; set; }

    [JsonPropertyName("alerts")] public int Alerts { get; set; }
}

public sealed class PlanCharacter
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("world")] public string World { get; set; } = string.Empty;

    public override string ToString()
    {
        return World.Length > 0 ? $"{Name} @ {World}" : Name;
    }
}
