using System;
using System.Runtime.InteropServices;

namespace MogHouseCompanion.Services;

/// <summary>
/// Whether the game is the window the player is actually using.
///
/// Dalamud exposes the game's own handle, so this is a comparison rather than a guess — alt-tabbed,
/// minimised, locked screen and second monitor all read the same way. Two features hang off it from
/// opposite directions: the duty push stays quiet while you are here, and the in-game notifications
/// only bother to arrive while you are.
/// </summary>
public static class GameWindow
{
    public static bool IsInForeground()
    {
        var game = Plugin.PluginInterface.UiBuilder.WindowHandlePtr;
        return game != IntPtr.Zero && GetForegroundWindow() == game;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
