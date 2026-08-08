using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using MogHouseCompanion.Services;

namespace MogHouseCompanion.Windows;

/// <summary>
/// What the plugin is doing right now: who it is linked to, what it can read, when it last synced.
/// Everything you can change lives in the settings window; this one only reports.
///
/// Laid out as one card per module, so the answer to "is this working" is the pill at the top and
/// the detail is underneath it rather than the other way round.
/// </summary>
public sealed class StatusWindow : Window, IDisposable
{
    /// <summary>Past this age the sync is old enough that the player should be told.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private readonly Configuration configuration;
    private readonly TimerSyncService syncService;
    private readonly PairingWindow pairingWindow;
    private readonly ConfigWindow configWindow;

    public StatusWindow(
        Configuration configuration,
        TimerSyncService syncService,
        PairingWindow pairingWindow,
        ConfigWindow configWindow)
        : base("MogHouse Companion###MogHouseCompanionStatus")
    {
        this.configuration = configuration;
        this.syncService = syncService;
        this.pairingWindow = pairingWindow;
        this.configWindow = configWindow;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(470, 340),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawHeader();

        if (!configuration.IsLinked)
        {
            DrawNotLinked();
            return;
        }

        Theme.Heading(FontAwesomeIcon.Hourglass, "Timers");
        Theme.Card("##timers", DrawSync);

        Theme.Heading(FontAwesomeIcon.Dungeon, "Duty Finder");
        Theme.Card("##duty", DrawDuty);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFooter();
    }

    /// <summary>Who this is and whether it is working — the two things worth reading first.</summary>
    private void DrawHeader()
    {
        Theme.Icon(FontAwesomeIcon.WandMagicSparkles, Theme.Gold);
        ImGui.SameLine(0, 8 * ImGuiHelpers.GlobalScale);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.GoldSoft, "MogHouse Companion");

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - PillWidth());
        DrawStatusPill();

        var player = Plugin.PlayerState;
        var character = player.IsLoaded
            ? $"{player.CharacterName} @ {(player.HomeWorld.IsValid ? player.HomeWorld.Value.Name.ToString() : "unknown world")}"
            : "Not logged in";

        ImGui.TextColored(Theme.Muted, $"{character}  ·  {Host()}");

        ImGui.Spacing();
    }

    private void DrawStatusPill()
    {
        var status = syncService.Status;

        if (!configuration.IsLinked)
        {
            Theme.Pill("Not linked", Theme.Muted);
            return;
        }

        if (status.PremiumRequired)
        {
            Theme.Pill("Mog+ needed", Theme.Premium);
            return;
        }

        if (status.LastError is { Length: > 0 })
        {
            Theme.Pill("Error", Theme.Bad);
            return;
        }

        Theme.Pill("Linked", Theme.Good);
    }

    private void DrawNotLinked()
    {
        Theme.Card("##link", () =>
        {
            Theme.Hint(
                "Link this PC to your MogHouse account and your in-game timers turn into " +
                "notifications on your phone — including while the game is closed.");

            ImGui.Spacing();

            if (Theme.PrimaryButton("Link account…"))
            {
                pairingWindow.IsOpen = true;
            }
        });
    }

    private void DrawSync()
    {
        var status = syncService.Status;

        if (status.PremiumRequired)
        {
            ImGui.TextColored(Theme.Premium, "Syncing is paused: Mog+ is not active on this account.");
            Theme.Hint(
                "Your link is kept, so it resumes on its own once Mog+ is active again — you will " +
                "not have to pair this device a second time.");

            ImGui.Spacing();

            if (Theme.PrimaryButton("Manage Mog+"))
            {
                Util.OpenLink($"{configuration.BaseUrl.TrimEnd('/')}/mogplus");
            }

            return;
        }

        Theme.Field("Last sync", DescribeLastSync(status), StaleColor(status));

        if (status.LastSuccessAt.HasValue)
        {
            Theme.Field("Timers sent", status.TimerCount.ToString());
        }

        if (status.Available.Length > 0)
        {
            Theme.Field("Reading", string.Join(", ", status.Available), Theme.Good);
        }

        if (status.Unavailable.Length > 0)
        {
            Theme.Field("No data yet", string.Join(", ", status.Unavailable), Theme.Muted);
        }

        if (status.LastError is { Length: > 0 } error)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.Bad, error);
        }

        ImGui.Spacing();

        using (ImRaii.Disabled(status.IsSyncing))
        {
            if (Theme.PrimaryButton(status.IsSyncing ? "Syncing…" : "Sync now"))
            {
                syncService.RequestSync();
            }
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, "Uploads as soon as something changes");

        if (status.Unavailable.Length > 0)
        {
            ImGui.Spacing();
            Theme.Hint(
                "Voyage timers are only readable inside the company workshop, and venture timers " +
                "after opening the retainer bell. That is a limit of the game, not of the plugin.");
        }
    }

    private void DrawDuty()
    {
        if (!configuration.DutyFinderPush)
        {
            Theme.Field("Duty pop push", "Off", Theme.Muted);
            Theme.Hint("Switch it on in Settings to get a buzz when the Duty Finder pops.");
            return;
        }

        Theme.Field("Duty pop push", "On", Theme.Good);
        Theme.Hint(
            configuration.DutyPushOnlyWhenAway
                ? "Sent the moment the confirm window appears, and only while the game is not the " +
                  "window you are looking at. You have about 45 seconds to commence."
                : "Sent the moment the confirm window appears, every time — including while you are " +
                  "looking straight at the game.");
    }

    private void DrawFooter()
    {
        if (Theme.PrimaryButton("My Companion page"))
        {
            Util.OpenLink(configuration.TimersUrl);
        }

        ImGui.SameLine();

        if (ImGui.Button("Settings"))
        {
            configWindow.IsOpen = true;
        }

        ImGui.SameLine();

        if (ImGui.Button("Unlink"))
        {
            Unlink();
        }
    }

    private Vector4? StaleColor(SyncStatus status)
    {
        if (!status.LastSuccessAt.HasValue)
        {
            return Theme.Muted;
        }

        return DateTime.UtcNow - status.LastSuccessAt.Value > StaleAfter ? Theme.Bad : null;
    }

    private static string DescribeLastSync(SyncStatus status)
    {
        if (!status.LastSuccessAt.HasValue)
        {
            return status.IsSyncing ? "syncing…" : "never";
        }

        var age = DateTime.UtcNow - status.LastSuccessAt.Value;
        var text = age < TimeSpan.FromMinutes(1)
            ? "just now"
            : age < TimeSpan.FromHours(1)
                ? $"{(int)age.TotalMinutes} min ago"
                : age < TimeSpan.FromDays(1)
                    ? $"{(int)age.TotalHours} h ago"
                    : $"{(int)age.TotalDays} d ago";

        return age > StaleAfter ? $"{text} (stale)" : text;
    }

    /// <summary>Host only: the full URL is settings detail, and this line is identity.</summary>
    private string Host()
    {
        return Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out var uri)
            ? uri.Host
            : configuration.BaseUrl;
    }

    /// <summary>Right-aligning the pill needs its width before it is drawn.</summary>
    private float PillWidth()
    {
        var status = syncService.Status;

        var text = !configuration.IsLinked ? "Not linked"
            : status.PremiumRequired ? "Mog+ needed"
            : status.LastError is { Length: > 0 } ? "Error"
            : "Linked";

        return ImGui.CalcTextSize(text).X + (16 * ImGuiHelpers.GlobalScale);
    }

    private void Unlink()
    {
        configuration.Token = string.Empty;
        configuration.Save();

        // Otherwise the window would keep reporting the old link's last sync and error state.
        syncService.ResetState();

        Plugin.Log.Information("Unlinked from MogHouse");
    }
}
