using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MogHouseCompanion.Services;
using MogHouseCompanion.Windows;

namespace MogHouseCompanion;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/moghouse";

    private readonly WindowSystem windowSystem = new("MogHouseCompanion");

    private Configuration Configuration { get; }
    private MogHouseApi Api { get; }
    private TimerSyncService SyncService { get; }
    private ActivityWatcher ActivityWatcher { get; }
    private DutyWatcher DutyWatcher { get; }
    private PairingWindow PairingWindow { get; }
    private ConfigWindow ConfigWindow { get; }
    private StatusWindow StatusWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Api = new MogHouseApi(Configuration);
        SyncService = new TimerSyncService(Configuration, Api, Framework, ClientState);
        ActivityWatcher = new ActivityWatcher(AddonLifecycle, SyncService);
        DutyWatcher = new DutyWatcher(Configuration, Api, AddonLifecycle);

        PairingWindow = new PairingWindow(Configuration, Api, SyncService);
        ConfigWindow = new ConfigWindow(Configuration, SyncService);
        StatusWindow = new StatusWindow(Configuration, SyncService, PairingWindow, ConfigWindow);

        windowSystem.AddWindow(PairingWindow);
        windowSystem.AddWindow(ConfigWindow);
        windowSystem.AddWindow(StatusWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the MogHouse Companion window.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        // The cog in the plugin installer should land on the settings, not the status readout.
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleStatusUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleStatusUi;

        windowSystem.RemoveAllWindows();

        StatusWindow.Dispose();
        ConfigWindow.Dispose();
        PairingWindow.Dispose();
        DutyWatcher.Dispose();
        ActivityWatcher.Dispose();
        SyncService.Dispose();
        Api.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        ToggleStatusUi();
    }

    private void ToggleStatusUi()
    {
        StatusWindow.Toggle();
    }

    private void ToggleConfigUi()
    {
        ConfigWindow.Toggle();
    }
}
