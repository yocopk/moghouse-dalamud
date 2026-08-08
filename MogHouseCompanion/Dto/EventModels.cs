using System;
using System.Text.Json.Serialization;

namespace MogHouseCompanion.Dto;

/// <summary>
/// Body of POST /api/plugin/v1/events — something that has just happened and is worth a push now.
///
/// Deliberately not a snapshot. A snapshot describes state and can be re-sent; an event describes a
/// moment, and re-sending one is a duplicate notification rather than a retry.
/// </summary>
public sealed class GameEventRequest
{
    public const string DutyReady = "duty_ready";

    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("data")] public object? Data { get; set; }
}

/// <summary>Payload for <see cref="GameEventRequest.DutyReady"/>.</summary>
public sealed class DutyReadyData
{
    /// <summary>What the game itself names on the confirm window: a duty, or a roulette.</summary>
    [JsonPropertyName("duty")] public string Duty { get; set; } = string.Empty;

    /// <summary>
    /// When the queue popped, taken from the game rather than the clock, so the server can say how
    /// much of the confirm window is actually left by the time the push is built.
    /// </summary>
    [JsonPropertyName("readyAt")] public DateTime ReadyAt { get; set; }
}

/// <summary>Payload of a delivered event.</summary>
public sealed class GameEventResult
{
    [JsonPropertyName("delivered")] public bool Delivered { get; set; }

    /// <summary>Set when the server chose not to deliver, e.g. the same pop reported twice.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}
