using Tmds.DBus.Protocol;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;

namespace Workspacey.Kde.DBus;

/// <summary>Reads virtual desktops from KWin's <c>/VirtualDesktopManager</c> object.</summary>
public sealed class KWinVirtualDesktopService(DBusConnection connection) : IVirtualDesktopService
{
    private const string ManagerInterface = "org.kde.KWin.VirtualDesktopManager";

    private static readonly DBusMethod GetProperty =
        new("org.kde.KWin", "/VirtualDesktopManager", "org.freedesktop.DBus.Properties", "Get");

    private static readonly DBusMethod CreateDesktop =
        new("org.kde.KWin", "/VirtualDesktopManager", ManagerInterface, "createDesktop");

    // createDesktop returns nothing; the new desktop shows up in the "desktops" property shortly after.
    private static readonly TimeSpan CreatePollInterval = TimeSpan.FromMilliseconds(50);
    private const int CreatePollAttempts = 40;

    public async Task<VirtualDesktop> CreateDesktopAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var before = await GetDesktopsAsync(cancellationToken).ConfigureAwait(false);
        var knownIds = before.Select(d => d.Id).ToHashSet();
        var position = (uint)before.Count;

        await CreateDesktop
            .CallAsync(connection, "us", (ref MessageWriter writer) =>
            {
                writer.WriteUInt32(position);
                writer.WriteString(name);
            })
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 0; attempt < CreatePollAttempts; attempt++)
        {
            var created = (await GetDesktopsAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(d => !knownIds.Contains(d.Id));
            if (created is not null) return created;

            await Task.Delay(CreatePollInterval, cancellationToken).ConfigureAwait(false);
        }

        // KWin ignores the request silently, e.g. when the maximum number of desktops is reached.
        throw new InvalidOperationException($"KWin did not create the workspace (there are already {before.Count}).");
    }

    public async Task<IReadOnlyList<VirtualDesktop>> GetDesktopsAsync(CancellationToken cancellationToken = default)
    {
        // Property type: a(uss) = [(position, id, name)]
        var value = await ReadPropertyAsync("desktops", cancellationToken).ConfigureAwait(false);

        var desktops = new List<VirtualDesktop>(value.Count);
        for (var i = 0; i < value.Count; i++)
        {
            var item = value.GetItem(i);
            desktops.Add(new VirtualDesktop(
                Id: item.GetItem(1).GetString(),
                Position: (int)item.GetItem(0).GetUInt32(),
                Name: item.GetItem(2).GetString()));
        }

        return [.. desktops.OrderBy(d => d.Position)];
    }

    public async Task<string> GetCurrentDesktopIdAsync(CancellationToken cancellationToken = default)
    {
        var value = await ReadPropertyAsync("current", cancellationToken).ConfigureAwait(false);
        return value.GetString();
    }

    private Task<VariantValue> ReadPropertyAsync(string property, CancellationToken cancellationToken) =>
        GetProperty
            .CallAsync(
                connection,
                static (message, _) => message.GetBodyReader().ReadVariantValue(),
                signature: "ss",
                writeArguments: (ref MessageWriter writer) =>
                {
                    writer.WriteString(ManagerInterface);
                    writer.WriteString(property);
                })
            .WaitAsync(cancellationToken);
}
