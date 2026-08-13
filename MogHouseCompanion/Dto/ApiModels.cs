using System.Text.Json.Serialization;

namespace MogHouseCompanion.Dto;

/// <summary>
/// Envelope returned by every /api/plugin/v1/* route. Mirrors ApiResponse&lt;T&gt; on the server
/// (src/lib/plugin/response.ts): exactly one of <see cref="Data"/> / <see cref="Error"/> is set.
/// </summary>
public sealed class ApiResponse<T>
{
    [JsonPropertyName("success")] public bool Success { get; set; }

    [JsonPropertyName("data")] public T? Data { get; set; }

    [JsonPropertyName("error")] public ApiError? Error { get; set; }
}

public sealed class ApiError
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>Error codes the server can return. Anything else falls back to the raw message.</summary>
public static class ApiErrorCode
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string InvalidCode = "INVALID_CODE";
    public const string Validation = "VALIDATION";
    public const string Blocked = "BLOCKED";
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>
    /// The account may use the plugin, but this particular thing is over its free ceiling — a
    /// second character, for instance. Distinct from <see cref="PremiumRequired"/>, which means the
    /// endpoint is closed to the account entirely.
    /// </summary>
    public const string PlanLimit = "PLAN_LIMIT";

    public const string PremiumRequired = "PREMIUM_REQUIRED";
    public const string Internal = "INTERNAL";

    /// <summary>Client-side only: the request never reached the server.</summary>
    public const string Network = "NETWORK";
}

/// <summary>Body of POST /api/plugin/v1/pair.</summary>
public sealed class PairRequest
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Payload of POST /api/plugin/v1/pair. This is the only time the raw bearer token is ever returned.
/// </summary>
public sealed class PairResult
{
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;

    [JsonPropertyName("tokenId")] public string TokenId { get; set; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}
