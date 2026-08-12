using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MogHouseCompanion.Dto;

namespace MogHouseCompanion.Services;

/// <summary>
/// Thin async client for the MogHouse plugin API. Every call returns an <see cref="ApiResponse{T}"/>
/// instead of throwing, so callers can render the failure without try/catch around the UI code.
/// Nothing here may ever be awaited from the game thread.
/// </summary>
public sealed class MogHouseApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Ceiling for time-sensitive calls; see <see cref="PostEventAsync"/>.</summary>
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly Configuration configuration;
    private readonly HttpClient http;

    public MogHouseApi(Configuration configuration)
    {
        this.configuration = configuration;

        var version = typeof(MogHouseApi).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        http = new HttpClient { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MogHouseCompanion/{version}");
    }

    /// <summary>
    /// Redeems a single-use pairing code for a bearer token. Unauthenticated by design — the code
    /// itself is the credential. The caller is responsible for persisting the returned token.
    /// </summary>
    public Task<ApiResponse<PairResult>> PairAsync(string code, CancellationToken ct = default)
    {
        var payload = new PairRequest
        {
            Code = NormalizeCode(code),
            Name = configuration.TokenLabel,
        };

        return PostAsync<PairRequest, PairResult>("/api/plugin/v1/pair", payload, authenticated: false, ct);
    }

    /// <summary>Pairing codes are uppercase and drawn from an unambiguous alphabet.</summary>
    public static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Uploads a timer snapshot. Keys present in the payload replace the stored rows for that key;
    /// keys left out are untouched, which is how a partial reading stays safe.
    /// </summary>
    public Task<ApiResponse<SnapshotResult>> PostSnapshotAsync(SnapshotRequest snapshot, CancellationToken ct = default)
    {
        return PostAsync<SnapshotRequest, SnapshotResult>(
            "/api/plugin/v1/timers/snapshot",
            snapshot,
            authenticated: true,
            ct);
    }

    /// <summary>
    /// Reports something that just happened, for immediate delivery.
    ///
    /// Given its own short timeout: the only event today is a duty pop, whose confirm window is 45
    /// seconds, so a request still in flight after ten of them has already lost the race and should
    /// free the slot rather than keep trying.
    /// </summary>
    public Task<ApiResponse<GameEventResult>> PostEventAsync(GameEventRequest gameEvent, CancellationToken ct = default)
    {
        return PostAsync<GameEventRequest, GameEventResult>(
            "/api/plugin/v1/events",
            gameEvent,
            authenticated: true,
            ct,
            timeout: EventTimeout);
    }

    /// <summary>
    /// Notifications raised on MogHouse since <paramref name="after"/>, oldest first.
    ///
    /// Read-only by design on both ends: the server does not mark anything read and neither does
    /// this. Seeing a toast in the corner of a game is not the same as having read a message, and
    /// clearing the badge on someone's phone from here would be a lie about what they have seen.
    /// </summary>
    public Task<ApiResponse<NotificationFeed>> GetNotificationsAsync(DateTime after, CancellationToken ct = default)
    {
        var cursor = Uri.EscapeDataString(after.ToUniversalTime().ToString("O"));
        return GetAsync<NotificationFeed>($"/api/plugin/v1/notifications?after={cursor}", ct);
    }

    private Task<ApiResponse<TOut>> PostAsync<TIn, TOut>(
        string path,
        TIn body,
        bool authenticated,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        return SendAsync<TOut>(
            () => new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions),
                    Encoding.UTF8,
                    "application/json"),
            },
            path,
            authenticated,
            ct,
            timeout);
    }

    /// <summary>Authenticated GET. Query strings belong in <paramref name="path"/>, already escaped.</summary>
    private Task<ApiResponse<TOut>> GetAsync<TOut>(string path, CancellationToken ct, TimeSpan? timeout = null)
    {
        return SendAsync<TOut>(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(path)),
            path,
            authenticated: true,
            ct,
            timeout);
    }

    /// <summary>
    /// The one place a request is actually sent. Both verbs share the per-call deadline, the
    /// envelope parsing and the failure shaping, so a new endpoint cannot accidentally acquire
    /// different timeout or error behaviour by being written slightly differently.
    /// </summary>
    private async Task<ApiResponse<TOut>> SendAsync<TOut>(
        Func<HttpRequestMessage> build,
        string path,
        bool authenticated,
        CancellationToken ct,
        TimeSpan? timeout)
    {
        // A per-call deadline on top of the client's, linked to the caller's token so cancelling
        // either one cancels the request.
        using var deadline = timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        deadline?.CancelAfter(timeout!.Value);
        var token = deadline?.Token ?? ct;

        try
        {
            using var request = build();

            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);
            }

            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            var parsed = TryDeserialize<TOut>(raw);
            if (parsed != null)
            {
                return parsed;
            }

            Plugin.Log.Warning($"MogHouse {path} returned an unreadable body (HTTP {(int)response.StatusCode})");
            return Failure<TOut>(
                ApiErrorCode.Internal,
                $"Unexpected response from the server (HTTP {(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Also covers the HttpClient timeout, which surfaces as a cancellation we did not request.
            Plugin.Log.Warning(ex, $"MogHouse {path} request failed");
            return Failure<TOut>(
                ApiErrorCode.Network,
                "Could not reach MogHouse. Check your connection and the configured server address.");
        }
    }

    private static ApiResponse<T>? TryDeserialize<T>(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ApiResponse<T>>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            // A proxy or error page rather than our envelope.
            return null;
        }
    }

    private static ApiResponse<T> Failure<T>(string code, string message)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = new ApiError { Code = code, Message = message },
        };
    }

    private Uri BuildUri(string path)
    {
        return new Uri(configuration.BaseUrl.TrimEnd('/') + path);
    }

    public void Dispose()
    {
        http.Dispose();
    }
}
