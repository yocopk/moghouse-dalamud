using System;
using System.Threading;
using System.Threading.Tasks;
using MogHouseCompanion.Dto;

namespace MogHouseCompanion.Services;

/// <summary>
/// Keeps the account's plan around so the windows can show it.
///
/// Fetched lazily, when something is about to draw it, rather than on a background timer. A plugin
/// nobody has open has no reason to ask, and a plan that changes maybe twice a year does not earn
/// its own polling loop.
///
/// Deliberately not authoritative. Every ceiling in here is enforced again server-side on the route
/// that would exceed it, so a stale or edited copy costs a misleading sentence in a window and
/// nothing else.
/// </summary>
public sealed class PlanService
{
    /// <summary>How long a fetched plan is trusted before the next look refetches it.</summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(15);

    /// <summary>Backoff after a failure, so an unreachable server is not asked once per frame.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(2);

    private readonly Configuration configuration;
    private readonly MogHouseApi api;

    private PlanInfo? plan;
    private DateTime nextFetchAt = DateTime.MinValue;
    private int fetching;

    /// <summary>
    /// The token the current answer belongs to. Pairing to a different account has to invalidate
    /// what we know, and the token is the only thing here that identifies which account that is —
    /// without this, re-linking would leave the other account's plan on screen until the freshness
    /// window elapsed.
    /// </summary>
    private string fetchedFor = string.Empty;

    public PlanService(Configuration configuration, MogHouseApi api)
    {
        this.configuration = configuration;
        this.api = api;
    }

    /// <summary>The last plan seen, or null if one has never been fetched successfully.</summary>
    public PlanInfo? Plan => plan;

    /// <summary>
    /// Called from the draw loop, so it runs many times a second and must stay cheap: everything
    /// below is a comparison until the freshness window has actually elapsed.
    /// </summary>
    public void EnsureFresh()
    {
        if (!configuration.IsLinked)
        {
            return;
        }

        var token = configuration.Token;

        if (!string.Equals(fetchedFor, token, StringComparison.Ordinal))
        {
            plan = null;
            nextFetchAt = DateTime.MinValue;
        }

        if (DateTime.UtcNow < nextFetchAt)
        {
            return;
        }

        // Both set before the request rather than after, so a slow reply cannot queue a second one.
        nextFetchAt = DateTime.UtcNow + Freshness;
        fetchedFor = token;

        if (Interlocked.CompareExchange(ref fetching, 1, 0) == 1)
        {
            return;
        }

        _ = Task.Run(() => FetchAsync(token));
    }

    private async Task FetchAsync(string token)
    {
        try
        {
            var result = await api.GetPlanAsync().ConfigureAwait(false);

            // Re-linked while this was in flight: the answer describes an account that is no longer
            // the one on screen, so it is dropped and the token check above asks again.
            if (!string.Equals(configuration.Token, token, StringComparison.Ordinal))
            {
                return;
            }

            if (result.Success && result.Data != null)
            {
                plan = result.Data;
                return;
            }

            // Quietly: an unreachable site is the ordinary state of a laptop on a train, and the
            // sync status right above this already reports connection health.
            nextFetchAt = DateTime.UtcNow + FailureBackoff;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Plan lookup failed");
            nextFetchAt = DateTime.UtcNow + FailureBackoff;
        }
        finally
        {
            Volatile.Write(ref fetching, 0);
        }
    }
}
