using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace MogHouseCompanion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const string ProdBaseUrl = "https://mog-house.com";

    public int Version { get; set; } = 1;

    /// <summary>
    /// MogHouse instance this plugin talks to. There is no in-game control for this: everyone who
    /// installs the plugin wants the live site, and a server picker in the settings window is a
    /// button whose only effect, for a real player, is to silently stop their notifications.
    ///
    /// Still a stored field rather than a constant so a development build can be pointed elsewhere
    /// by editing the saved config — see the README. Deserialization means an existing install keeps
    /// whatever it was already set to, which it must: a bearer token is only valid on the instance
    /// that issued it, so nothing here may move a linked plugin to another server behind its back.
    /// </summary>
    public string BaseUrl { get; set; } = ProdBaseUrl;

    /// <summary>
    /// Bearer token ("mgp_…") obtained by redeeming a pairing code. Empty when the plugin is not linked.
    /// Stored in plaintext: it is scoped to a single MogHouse account and can be revoked from the website at any time.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Label shown for this device in the account's device list on the website.</summary>
    public string TokenLabel { get; set; } = "FFXIV Plugin";

    /// <summary>
    /// Which timers may leave the game, keyed by timer key. This is the privacy control: a timer
    /// switched off here is never uploaded, and the server is told to forget what it already has.
    /// Whether an uploaded timer also sends a push is a separate choice, made on the website.
    ///
    /// Missing entries count as enabled, so a key added by a later version starts on rather than
    /// silently doing nothing.
    /// </summary>
    public Dictionary<string, bool> TimerUploads { get; set; } = new();

    public bool IsTimerEnabled(string key)
    {
        return !TimerUploads.TryGetValue(key, out var enabled) || enabled;
    }

    public void SetTimerEnabled(string key, bool enabled)
    {
        TimerUploads[key] = enabled;
    }

    /// <summary>
    /// Which duty roulettes appear in the daily checklist, keyed by the sheet's row id.
    ///
    /// Row id rather than name because names are localised, and rather than the completion index
    /// because that is a position in a game array and positions move. Missing entries fall back to
    /// <c>!isExtra</c>, so the everyday roulettes are on out of the box while PvP and the Gold
    /// Saucer stay out of the way until someone asks for them.
    /// </summary>
    public Dictionary<string, bool> Roulettes { get; set; } = new();

    public bool IsRouletteTracked(uint rowId, bool isExtra)
    {
        return Roulettes.TryGetValue(rowId.ToString(), out var tracked) ? tracked : !isExtra;
    }

    public void SetRouletteTracked(uint rowId, bool tracked)
    {
        Roulettes[rowId.ToString()] = tracked;
    }

    /// <summary>Whether a Duty Finder pop is reported to MogHouse so it can push to your phone.</summary>
    public bool DutyFinderPush { get; set; } = true;

    /// <summary>
    /// Hold the duty push back while the game is the window you are actually using.
    ///
    /// On by default because the alternative is a phone buzzing in your pocket a second after the
    /// game has already made a noise at you — the notification is for when you have walked away,
    /// and the game window being in the background is the closest thing to a signal for that.
    /// </summary>
    public bool DutyPushOnlyWhenAway { get; set; } = true;

    /// <summary>
    /// Whether the timers readout opens alongside the main window.
    ///
    /// On by default: the plugin reads these timers anyway, and having to open the website to see
    /// numbers the game just handed over is the kind of thing that makes a tool feel like a chore.
    /// It can also be opened on its own and left open, in which case it comes back on the next login.
    /// </summary>
    public bool ShowTimersWindow { get; set; } = true;

    /// <summary>Show MogHouse notifications — messages, matches, announcements — inside the game.</summary>
    public bool ShowMogHouseNotifications { get; set; } = true;

    /// <summary>
    /// Whether the notification's body is shown, or only who it is from.
    ///
    /// Off by default. This paints over the game, plenty of people play with a stream running, and a
    /// private message spelling itself out on screen is not something to opt *out* of.
    /// </summary>
    public bool ShowNotificationContent { get; set; }

    /// <summary>Also echo each notification into the game chat, where it stays in the log.</summary>
    public bool AnnounceNotificationsInChat { get; set; }

    /// <summary>
    /// Cursor for the notification feed, issued by the server rather than measured locally so a
    /// client clock running fast cannot skip past notifications it never showed. Null means "start
    /// from the next poll", which is what a fresh install should do.
    /// </summary>
    public DateTime? LastNotificationAt { get; set; }

    /// <summary>Print a line in the game chat when a snapshot reaches MogHouse.</summary>
    public bool AnnounceSyncInChat { get; set; } = true;

    /// <summary>
    /// Print a line when a voyage or venture comes back while you are playing.
    ///
    /// Deliberately only for deadlines that pass *while you are watching*: anything already finished
    /// when the plugin starts reading is seeded as announced, because the push notification covered
    /// that case and logging in should not dump a wall of text about things you were told hours ago.
    /// </summary>
    public bool AnnounceFinishedTimersInChat { get; set; } = true;

    [JsonIgnore]
    public bool IsLinked => !string.IsNullOrEmpty(Token);

    /// <summary>Where the pairing code is generated.</summary>
    [JsonIgnore]
    public string SettingsUrl => $"{BaseUrl.TrimEnd('/')}/settings/companion";

    /// <summary>
    /// The Companion page: what has been synced, and which of it sends a push.
    ///
    /// Both paths were /ffxiv until the feature stopped being called FFXIV Sync. The server keeps
    /// permanent redirects from the old ones, so a plugin build older than the rename still lands
    /// in the right place.
    /// </summary>
    [JsonIgnore]
    public string TimersUrl => $"{BaseUrl.TrimEnd('/')}/companion";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
