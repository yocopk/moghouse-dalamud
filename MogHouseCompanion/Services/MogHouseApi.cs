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

    private async Task<ApiResponse<TOut>> PostAsync<TIn, TOut>(
        string path,
        TIn body,
        bool authenticated,
        CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);
            }

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

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
