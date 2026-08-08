using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace MogHouseCompanion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const string DevBaseUrl = "https://dev.mog-house.com";
    public const string ProdBaseUrl = "https://mog-house.com";

    public int Version { get; set; } = 1;

    /// <summary>
    /// MogHouse instance this plugin talks to.
    /// Defaults to the dev server for the closed beta; switch to <see cref="ProdBaseUrl"/> before the public release.
    /// </summary>
    public string BaseUrl { get; set; } = DevBaseUrl;

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

    [JsonIgnore]
    public bool IsLinked => !string.IsNullOrEmpty(Token);

    /// <summary>Where the pairing code is generated.</summary>
    [JsonIgnore]
    public string SettingsUrl => $"{BaseUrl.TrimEnd('/')}/settings/ffxiv";

    /// <summary>The timers page: what has been synced, and which of it sends a push.</summary>
    [JsonIgnore]
    public string TimersUrl => $"{BaseUrl.TrimEnd('/')}/ffxiv";

    /// <summary>
    /// Validates a server address typed by hand and strips a trailing slash, or returns null when
    /// it is not usable. Paths are allowed: request paths are appended to this value, so an
    /// instance hosted under a sub-path still works.
    /// </summary>
    public static string? NormalizeBaseUrl(string input)
    {
        var text = input.Trim().TrimEnd('/');
        if (text.Length == 0)
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return text;
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
