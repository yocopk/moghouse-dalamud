using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MogHouseCompanion.Dto;

/// <summary>Body of POST /api/plugin/v1/timers/snapshot.</summary>
public sealed class SnapshotRequest
{
    [JsonPropertyName("character")] public SnapshotCharacter Character { get; set; } = new();

    /// <summary>Used server-side to warn about clock skew. UTC.</summary>
    [JsonPropertyName("clientTime")] public DateTime ClientTime { get; set; }

    [JsonPropertyName("timers")] public List<SnapshotTimer> Timers { get; set; } = [];
}

public sealed class SnapshotCharacter
{
    /// <summary>ContentId is a ulong; sent as a string so it survives JSON number precision.</summary>
    [JsonPropertyName("contentId")] public string ContentId { get; set; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("world")] public string World { get; set; } = string.Empty;
}

public sealed class SnapshotTimer
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

    /// <summary>Sub / retainer name for timers that repeat per entity; omitted for single timers.</summary>
    [JsonPropertyName("subKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubKey { get; set; }

    /// <summary>Absolute UTC deadline. Null for an entity that exists but is idle.</summary>
    [JsonPropertyName("dueAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? DueAt { get; set; }

    [JsonPropertyName("count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; set; }

    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Payload { get; set; }
}

/// <summary>Payload of a successful snapshot upload.</summary>
public sealed class SnapshotResult
{
    [JsonPropertyName("characterId")] public string CharacterId { get; set; } = string.Empty;

    [JsonPropertyName("accepted")] public int Accepted { get; set; }

    [JsonPropertyName("rejected")] public List<string> Rejected { get; set; } = [];

    [JsonPropertyName("serverTime")] public DateTime? ServerTime { get; set; }
}
