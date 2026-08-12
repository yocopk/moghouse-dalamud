using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MogHouseCompanion.Dto;

/// <summary>Payload of GET /api/plugin/v1/notifications.</summary>
public sealed class NotificationFeed
{
    /// <summary>Oldest first, so they can be shown in the order they happened.</summary>
    [JsonPropertyName("notifications")] public List<MogHouseNotification> Notifications { get; set; } = [];

    /// <summary>
    /// The cursor for the next poll. Server-issued rather than taken from the newest item, so a
    /// quiet poll still moves it forward and a skewed client clock cannot strand the feed.
    /// </summary>
    [JsonPropertyName("serverTime")] public DateTime? ServerTime { get; set; }
}

public sealed class MogHouseNotification
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    /// <summary>"message", "match", "party_join_request", … Used to decide what to show, not what to say.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("read")] public bool Read { get; set; }

    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
}
