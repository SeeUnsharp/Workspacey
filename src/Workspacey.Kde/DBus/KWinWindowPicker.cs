using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;

namespace Workspacey.Kde.DBus;

/// <summary>
/// Uses KWin's <c>queryWindowInfo</c>, which turns the cursor into a crosshair until the user clicks a
/// window. Works on Wayland, where applications cannot grab the pointer themselves.
/// </summary>
public sealed class KWinWindowPicker(DBusConnection connection, ILogger<KWinWindowPicker> logger) : IWindowPicker
{
    private static readonly DBusMethod QueryWindowInfo = new("org.kde.KWin", "/KWin", "org.kde.KWin", "queryWindowInfo");

    public async Task<WindowInfo?> PickWindowAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, VariantValue> info;
        try
        {
            info = await QueryWindowInfo
                .CallAsync(connection, static (message, _) => message.GetBodyReader().ReadDictionaryOfStringToVariantValue())
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DBusErrorReplyException e)
        {
            // KWin replies with an error when the selection is cancelled (Esc / right click).
            logger.LogInformation("Window selection ended without a window: {Error}", e.ErrorMessage);
            return null;
        }

        if (info.Count == 0) return null;

        return new WindowInfo(
            Id: GetString(info, "uuid"),
            Caption: GetString(info, "caption"),
            ResourceClass: GetString(info, "resourceClass"),
            ResourceName: GetString(info, "resourceName"),
            DesktopFile: GetString(info, "desktopFile"));
    }

    private static string GetString(Dictionary<string, VariantValue> info, string key) =>
        info.TryGetValue(key, out var value) && value.Type == VariantValueType.String
            ? value.GetString()
            : string.Empty;
}
