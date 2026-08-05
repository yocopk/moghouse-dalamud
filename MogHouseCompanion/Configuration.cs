using System;
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

    [JsonIgnore]
    public bool IsLinked => !string.IsNullOrEmpty(Token);

    [JsonIgnore]
    public string SettingsUrl => $"{BaseUrl.TrimEnd('/')}/settings/ffxiv";

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
