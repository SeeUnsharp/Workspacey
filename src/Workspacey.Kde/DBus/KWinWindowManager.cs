using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Workspacey.Core.Abstractions;

namespace Workspacey.Kde.DBus;

/// <summary>
/// Live KWin operations. KWin has no D-Bus call to move a window, so moving is done by running a
/// one-shot KWin script.
/// </summary>
public sealed class KWinWindowManager(DBusConnection connection, ILogger<KWinWindowManager> logger) : IWindowManager
{
    private const string ScriptPluginName = "workspacey-move-window";

    // KWin evaluates the script asynchronously after run(); unloading must wait for that.
    private static readonly TimeSpan ScriptRunGracePeriod = TimeSpan.FromMilliseconds(500);

    private static readonly DBusMethod Reconfigure = new("org.kde.KWin", "/KWin", "org.kde.KWin", "reconfigure");
    private static readonly DBusMethod LoadScript = new("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "loadScript");
    private static readonly DBusMethod UnloadScript = new("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript");

    public Task ReloadRulesAsync(CancellationToken cancellationToken = default) =>
        Reconfigure.CallAsync(connection).WaitAsync(cancellationToken);

    public async Task MoveWindowToDesktopAsync(string windowId, string desktopId, CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(XdgPaths.RuntimeDir, $"{ScriptPluginName}-{Environment.ProcessId}.js");
        await File.WriteAllTextAsync(scriptPath, BuildMoveScript(windowId, desktopId), cancellationToken).ConfigureAwait(false);

        try
        {
            await UnloadAsync().ConfigureAwait(false); // a previous run may have crashed before unloading

            var scriptId = await LoadScript.CallAsync(
                connection,
                static (message, _) => message.GetBodyReader().ReadInt32(),
                signature: "ss",
                writeArguments: (ref MessageWriter writer) =>
                {
                    writer.WriteString(scriptPath);
                    writer.WriteString(ScriptPluginName);
                }).ConfigureAwait(false);

            if (scriptId < 0) throw new InvalidOperationException("KWin refused to load the move-window script.");

            await new DBusMethod("org.kde.KWin", $"/Scripting/Script{scriptId}", "org.kde.kwin.Script", "run")
                .CallAsync(connection).ConfigureAwait(false);
            await Task.Delay(ScriptRunGracePeriod, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnloadAsync().ConfigureAwait(false);
            File.Delete(scriptPath);
        }
    }

    internal static string BuildMoveScript(string windowId, string desktopId) =>
        $$"""
        (function () {
            const windowId = {{JsonSerializer.Serialize(windowId)}};
            const desktopId = {{JsonSerializer.Serialize(desktopId)}};
            const normalize = id => String(id).replace(/[{}]/g, "").toLowerCase();

            const desktop = workspace.desktops.find(d => d.id === desktopId);
            if (!desktop) {
                print("workspacey: unknown desktop " + desktopId);
                return;
            }

            const window = workspace.windowList().find(w => normalize(w.internalId) === normalize(windowId));
            if (!window) {
                print("workspacey: window " + windowId + " is gone");
                return;
            }

            window.desktops = [desktop];
        })();
        """;

    private async Task UnloadAsync()
    {
        try
        {
            await UnloadScript.CallAsync(
                connection,
                static (message, _) => message.GetBodyReader().ReadBool(),
                signature: "s",
                writeArguments: (ref MessageWriter writer) => writer.WriteString(ScriptPluginName)).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException e)
        {
            logger.LogWarning("Could not unload KWin script {Script}: {Error}", ScriptPluginName, e.ErrorMessage);
        }
    }
}
