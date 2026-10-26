using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;
using Workspacey.Kde.DBus;
using Workspacey.Kde.Desktop;

namespace Workspacey.Kde.Shortcuts;

/// <summary>
/// Registers the pick shortcut with kglobalaccel, KDE's global shortcut daemon. The shortcut is bound
/// to a hidden .desktop launcher, exactly like a "command" shortcut created in System Settings, so it
/// works without Workspacey running in the background and shows up under Settings → Shortcuts.
/// </summary>
public sealed class KGlobalAccelPickShortcutService(
    DBusConnection connection,
    KdeDesktopIntegration desktopIntegration,
    ILogger<KGlobalAccelPickShortcutService> logger) : IPickShortcutService
{
    // kglobalaccel identifies an action by [componentUnique, actionUnique, componentFriendly, actionFriendly].
    // Components named "<file>.desktop" are services; their "_launch" action starts the desktop file.
    private static readonly string[] ActionId = [AppLauncher.PickDesktopFileName, "_launch", "Workspacey", "Pick window"];

    // KGlobalAccel::SetShortcutFlag
    private const uint SetPresent = 2;
    private const uint NoAutoloading = 4;

    private static readonly DBusMethod DoRegister = Method("doRegister");
    private static readonly DBusMethod ShortcutKeys = Method("shortcutKeys");
    private static readonly DBusMethod SetShortcutKeys = Method("setShortcutKeys");
    private static readonly DBusMethod BlockGlobalShortcuts = Method("blockGlobalShortcuts");

    public Task SetGlobalShortcutsPausedAsync(bool paused, CancellationToken cancellationToken = default) =>
        BlockGlobalShortcuts
            .CallAsync(connection, "b", (ref MessageWriter writer) => writer.WriteBool(paused))
            .WaitAsync(cancellationToken);

    public async Task<KeyChord?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(KdeDesktopIntegration.PickEntryPath)) return null;

        var keys = await ShortcutKeys
            .CallAsync(connection, static (message, _) => ReadKeySequences(message), "as",
                (ref MessageWriter writer) => writer.WriteArray(ActionId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return keys.SelectMany(sequence => sequence.Take(1))
            .Select(code => KeyChord.TryFromQtKeyCombination(code, out var chord) ? chord : null)
            .FirstOrDefault(chord => chord is not null);
    }

    public async Task<bool> SetAsync(KeyChord chord, CancellationToken cancellationToken = default)
    {
        if (!chord.IsValidGlobalShortcut)
        {
            throw new ArgumentException($"'{chord}' cannot be used as a global shortcut.", nameof(chord));
        }

        await desktopIntegration.InstallPickLauncherAsync(cancellationToken).ConfigureAwait(false);

        await DoRegister
            .CallAsync(connection, "as", (ref MessageWriter writer) => writer.WriteArray(ActionId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        var combination = chord.ToQtKeyCombination();
        var assigned = await WriteKeysAsync([combination], cancellationToken).ConfigureAwait(false);

        // kglobalaccel silently drops keys that another action already owns.
        var accepted = assigned.Any(sequence => sequence.FirstOrDefault() == combination);
        if (accepted) logger.LogInformation("Registered pick shortcut {Chord}", chord);
        else logger.LogWarning("kglobalaccel rejected {Chord}; it is probably used by another action", chord);
        return accepted;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(KdeDesktopIntegration.PickEntryPath)) return;
        await WriteKeysAsync([], cancellationToken).ConfigureAwait(false);
    }

    private Task<List<int[]>> WriteKeysAsync(IReadOnlyList<int> combinations, CancellationToken cancellationToken) =>
        SetShortcutKeys
            .CallAsync(
                connection,
                static (message, _) => ReadKeySequences(message),
                "asa(ai)u",
                (ref MessageWriter writer) =>
                {
                    writer.WriteArray(ActionId);
                    var array = writer.WriteArrayStart(DBusType.Struct);
                    foreach (var combination in combinations)
                    {
                        // QKeySequence is marshalled as a struct holding up to four key combinations.
                        writer.WriteStructureStart();
                        writer.WriteArray(new[] { combination });
                    }

                    writer.WriteArrayEnd(array);
                    writer.WriteUInt32(SetPresent | NoAutoloading);
                })
            .WaitAsync(cancellationToken);

    /// <summary>Reads a(ai): a list of QKeySequences, each a list of key combinations.</summary>
    private static List<int[]> ReadKeySequences(Message message)
    {
        var reader = message.GetBodyReader();
        var sequences = new List<int[]>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            sequences.Add(reader.ReadArrayOfInt32());
        }

        return sequences;
    }

    private static DBusMethod Method(string member) =>
        new("org.kde.kglobalaccel", "/kglobalaccel", "org.kde.KGlobalAccel", member);
}
