using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using MogHouseCompanion.Dto;

namespace MogHouseCompanion.Services;

/// <summary>
/// Brings MogHouse notifications into the game.
///
/// The other direction of everything else in the plugin: this is the only thing that reads from the
/// site rather than writing to it. It shows a Dalamud toast and, optionally, a line in chat.
///
/// Two rules shape it. It only polls while the game has focus — if you are alt-tabbed the phone push
/// already has you covered, and an in-game toast for a window you are not looking at is a request
/// nobody needed. And it never marks anything read: the cursor is local, so the badge on your phone
/// still reflects what you have actually opened.
/// </summary>
public sealed class NotificationPoller : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Backoff after a failure, so a server that is down is not hammered once a minute.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a toast stays up. Long enough to read a sender and a line, short enough that a
    /// handful arriving together do not become a wall.
    /// </summary>
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    private readonly Configuration configuration;
    private readonly MogHouseApi api;
    private readonly IFramework framework;
    private readonly INotificationManager notifications;
    private readonly IChatGui chat;

    private DateTime nextPollAt = DateTime.MinValue;
    private int polling;

    public NotificationPoller(
        Configuration configuration,
        MogHouseApi api,
        IFramework framework,
        INotificationManager notifications,
        IChatGui chat)
    {
        this.configuration = configuration;
        this.api = api;
        this.framework = framework;
        this.notifications = notifications;
        this.chat = chat;

        this.framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework tick)
    {
        if (!configuration.IsLinked || !configuration.ShowMogHouseNotifications)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (now < nextPollAt || !GameWindow.IsInForeground())
        {
            return;
        }

        // Set before the request rather than after it, so a slow response cannot queue a second one.
        nextPollAt = now + PollInterval;

        if (Interlocked.CompareExchange(ref polling, 1, 0) == 1)
        {
            return;
        }

        // First run has no cursor: adopt now and show nothing. A plugin that opened with a toast for
        // every notification of the past day would be a worse experience than one that stayed quiet.
        var after = configuration.LastNotificationAt ?? now;

        _ = Task.Run(() => PollAsync(after));
    }

    private async Task PollAsync(DateTime after)
    {
        try
        {
            var result = await api.GetNotificationsAsync(after).ConfigureAwait(false);

            if (!result.Success || result.Data == null)
            {
                // Quietly: an unreachable site is the ordinary state of a laptop on a train, and the
                // status window already reports connection health.
                nextPollAt = DateTime.UtcNow + FailureBackoff;
                return;
            }

            var feed = result.Data;

            // Framework thread for anything that touches the game or Dalamud's UI. Awaited so the
            // in-flight flag below is only cleared once they are actually on screen, and so a throw
            // in there surfaces in the catch rather than in an unobserved task.
            await framework.RunOnFrameworkThread(() => Show(feed)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Notification poll failed");
            nextPollAt = DateTime.UtcNow + FailureBackoff;
        }
        finally
        {
            Volatile.Write(ref polling, 0);
        }
    }

    private void Show(NotificationFeed feed)
    {
        foreach (var item in feed.Notifications)
        {
            // Already opened elsewhere — on the phone, or in a browser — while this poll was in
            // flight. Repeating it in game would be telling you something you just dealt with.
            if (item.Read)
            {
                continue;
            }

            notifications.AddNotification(new Notification
            {
                Title = item.Title,
                Content = Content(item),
                Type = NotificationType.Info,
                InitialDuration = ToastDuration,
                MinimizedText = "MogHouse",
            });

            if (configuration.AnnounceNotificationsInChat)
            {
                chat.Print(
                    new SeStringBuilder().AddText(Line(item)).Build(),
                    "MogHouse",
                    541);
            }
        }

        // Advanced even when nothing arrived, and only from the server's own clock: a client whose
        // clock runs fast would otherwise skip past notifications it never saw.
        if (feed.ServerTime.HasValue)
        {
            configuration.LastNotificationAt = feed.ServerTime.Value;
            configuration.Save();
        }
    }

    /// <summary>
    /// The body, or nothing at all. Off by default because this renders over the game: plenty of
    /// people play with a stream running, and a private message painting itself across the screen is
    /// not a feature. The title already names the sender, which is the part you act on.
    /// </summary>
    private string Content(MogHouseNotification item)
    {
        return configuration.ShowNotificationContent ? item.Body : string.Empty;
    }

    private string Line(MogHouseNotification item)
    {
        return configuration.ShowNotificationContent && item.Body.Length > 0
            ? $"{item.Title} — {item.Body}"
            : item.Title;
    }
}
