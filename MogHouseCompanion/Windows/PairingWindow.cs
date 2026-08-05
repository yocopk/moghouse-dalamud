using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using MogHouseCompanion.Dto;
using MogHouseCompanion.Services;

namespace MogHouseCompanion.Windows;

/// <summary>
/// Redeems a pairing code generated on the website. This is the only place the plugin writes
/// <see cref="Configuration.Token"/>.
/// </summary>
public sealed class PairingWindow : Window, IDisposable
{
    private const int CodeLength = 8;

    // ImGui counts the terminator in the buffer size; leave room so the field never truncates a valid code.
    private const int CodeInputCapacity = 32;

    private readonly Configuration configuration;
    private readonly MogHouseApi api;
    private readonly TimerSyncService syncService;

    private string codeInput = string.Empty;

    // Written from the pairing task, read by Draw on the framework thread.
    private volatile bool isPairing;
    private volatile string statusMessage = string.Empty;
    private volatile bool statusIsError;

    public PairingWindow(Configuration configuration, MogHouseApi api, TimerSyncService syncService)
        : base("MogHouse — Link account###MogHouseCompanionPairing")
    {
        this.configuration = configuration;
        this.api = api;
        this.syncService = syncService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 210),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped(
            "Open the FFXIV Sync settings on MogHouse, generate a pairing code, and paste it here. " +
            "A code lasts 5 minutes and works once.");

        ImGui.Spacing();

        if (ImGui.Button("Open FFXIV Sync settings"))
        {
            Util.OpenLink(configuration.SettingsUrl);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(isPairing))
        {
            ImGui.SetNextItemWidth(180 * ImGuiHelpers.GlobalScale);

            var submitted = ImGui.InputText(
                "Pairing code###MogHouseCompanionPairingCode",
                ref codeInput,
                CodeInputCapacity,
                ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.CharsNoBlank |
                ImGuiInputTextFlags.EnterReturnsTrue);

            ImGui.SameLine();

            if (ImGui.Button("Link") || submitted)
            {
                Submit();
            }
        }

        if (statusMessage.Length == 0)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(statusIsError ? ImGuiColors.DalamudRed : ImGuiColors.HealerGreen, statusMessage);
    }

    private void Submit()
    {
        if (isPairing)
        {
            return;
        }

        var code = MogHouseApi.NormalizeCode(codeInput);
        if (code.Length != CodeLength)
        {
            SetStatus($"A pairing code is {CodeLength} characters long.", isError: true);
            return;
        }

        isPairing = true;
        SetStatus("Linking…", isError: false);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await api.PairAsync(code).ConfigureAwait(false);
                OnPairCompleted(result);
            }
            catch (Exception ex)
            {
                isPairing = false;
                Plugin.Log.Error(ex, "Pairing task faulted");
                SetStatus("Pairing failed unexpectedly. Check /xllog for details.", isError: true);
            }
        });
    }

    private void OnPairCompleted(ApiResponse<PairResult> result)
    {
        isPairing = false;

        var data = result.Data;
        if (result.Success && data != null && data.Token.Length > 0)
        {
            configuration.Token = data.Token;

            if (!string.IsNullOrWhiteSpace(data.Name))
            {
                configuration.TokenLabel = data.Name;
            }

            configuration.Save();

            codeInput = string.Empty;
            SetStatus("Linked. You can close this window.", isError: false);
            Plugin.Log.Information($"Linked to MogHouse as device '{configuration.TokenLabel}'");

            // Send what we can read right now so the website is not empty on first visit.
            syncService.RequestSync();
            return;
        }

        SetStatus(Describe(result.Error), isError: true);
        Plugin.Log.Warning($"Pairing rejected: {result.Error?.Code} — {result.Error?.Message}");
    }

    private static string Describe(ApiError? error) => error?.Code switch
    {
        ApiErrorCode.InvalidCode or ApiErrorCode.Validation =>
            "That code is invalid, expired, or already used. Generate a new one on the website.",
        ApiErrorCode.PremiumRequired =>
            "FFXIV Sync requires an active Mog+ subscription on your MogHouse account.",
        ApiErrorCode.RateLimited =>
            "Too many attempts. Wait a few minutes before trying again.",
        ApiErrorCode.Blocked or ApiErrorCode.Forbidden =>
            "This account cannot use the plugin API.",
        _ => error is not null && !string.IsNullOrWhiteSpace(error.Message)
            ? error.Message
            : "Pairing failed. Please try again.",
    };

    private void SetStatus(string message, bool isError)
    {
        statusMessage = message;
        statusIsError = isError;
    }
}
