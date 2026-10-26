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
    private static readonly DBusMethod GlobalShortcutAvailable = Method("globalShortcutAvailable");
    private static readonly DBusMethod GlobalShortcutsByKey = Method("globalShortcutsByKey");
    private static readonly DBusMethod Unregister = Method("unregister");

    public Task SetGlobalShortcutsPausedAsync(bool paused, CancellationToken cancellationToken = default) =>
        BlockGlobalShortcuts
            .CallAsync(connection, "b", (ref MessageWriter writer) => writer.WriteBool(paused))
            .WaitAsync(cancellationToken);

    public async Task<KeyChord?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(KdeDesktopIntegration.PickEntryPath)) return null;

        var keys = await ReadKeysAsync(cancellationToken).ConfigureAwait(false);
        return keys.SelectMany(sequence => sequence.Take(1))
            .Select(code => KeyChord.TryFromQtKeyCombination(code, out var chord) ? chord : null)
            .FirstOrDefault(chord => chord is not null);
    }

    public async Task<ShortcutResult> SetAsync(KeyChord chord, CancellationToken cancellationToken = default)
    {
        if (!chord.IsValidGlobalShortcut)
        {
            throw new ArgumentException($"'{chord}' cannot be used as a global shortcut.", nameof(chord));
        }

        var combination = chord.ToQtKeyCombination();

        // setShortcutKeys does not reliably refuse taken keys (e.g. KWin's own Meta+G is accepted, then
        // never fires), so check first, like KDE's shortcut editor does.
        var availability = await CheckAvailabilityAsync(chord, cancellationToken).ConfigureAwait(false);
        if (!availability.Accepted)
        {
            logger.LogWarning("{Chord} is already used by {Owner}", chord, availability.ConflictingAction ?? "another action");
            return availability;
        }

        await desktopIntegration.InstallPickLauncherAsync(cancellationToken).ConfigureAwait(false);

        await DoRegister
            .CallAsync(connection, "as", (ref MessageWriter writer) => writer.WriteArray(ActionId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        var previous = await ReadKeysAsync(cancellationToken).ConfigureAwait(false);
        var assigned = await WriteKeysAsync([[combination]], cancellationToken).ConfigureAwait(false);

        // If kglobalaccel still drops the key, it stores what is left (nothing), which would wipe the
        // working shortcut. Put the previous keys back in that case.
        var accepted = assigned.Any(sequence => sequence.FirstOrDefault() == combination);
        if (accepted)
        {
            logger.LogInformation("Registered pick shortcut {Chord}", chord);
        }
        else
        {
            logger.LogWarning("kglobalaccel rejected {Chord}", chord);
            await WriteKeysAsync(previous, cancellationToken).ConfigureAwait(false);
        }

        return new ShortcutResult(accepted);
    }

    public async Task<ShortcutResult> CheckAvailabilityAsync(KeyChord chord, CancellationToken cancellationToken = default)
    {
        var combination = chord.ToQtKeyCombination();
        if (await IsAvailableAsync(combination, cancellationToken).ConfigureAwait(false)) return new ShortcutResult(Accepted: true);

        var owner = await FindOwnerAsync(combination, cancellationToken).ConfigureAwait(false);
        return new ShortcutResult(Accepted: false, owner);
    }

    /// <summary>True if no action of another component uses the combination.</summary>
    private Task<bool> IsAvailableAsync(int combination, CancellationToken cancellationToken) =>
        GlobalShortcutAvailable
            .CallAsync(
                connection,
                static (message, _) => message.GetBodyReader().ReadBool(),
                "(ai)s",
                (ref MessageWriter writer) =>
                {
                    writer.WriteStructureStart();
                    writer.WriteArray(new[] { combination });
                    writer.WriteString(ActionId[0]);
                })
            .WaitAsync(cancellationToken);

    /// <summary>"Action (Component)" of whoever owns the combination, e.g. "Übersicht umschalten (KWin)".</summary>
    private async Task<string?> FindOwnerAsync(int combination, CancellationToken cancellationToken)
    {
        // KGlobalAccel::MatchType::Equal
        const int EqualMatch = 0;

        var owners = await GlobalShortcutsByKey
            .CallAsync(
                connection,
                static (message, _) => ReadShortcutInfos(message),
                "(ai)(i)",
                (ref MessageWriter writer) =>
                {
                    writer.WriteStructureStart();
                    writer.WriteArray(new[] { combination });
                    writer.WriteStructureStart();
                    writer.WriteInt32(EqualMatch);
                })
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return owners
            .Where(owner => owner.Component != ActionId[0])
            .Select(owner => $"{owner.Action} ({owner.ComponentName})")
            .FirstOrDefault();
    }

    /// <summary>Reads a(ssssssaiai): KGlobalShortcutInfo list.</summary>
    private static List<(string Action, string Component, string ComponentName)> ReadShortcutInfos(Message message)
    {
        var reader = message.GetBodyReader();
        var infos = new List<(string, string, string)>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            reader.ReadString(); // action unique name
            var actionFriendly = reader.ReadString();
            var component = reader.ReadString();
            var componentFriendly = reader.ReadString();
            reader.ReadString(); // context unique name
            reader.ReadString(); // context friendly name
            reader.ReadArrayOfInt32(); // keys
            reader.ReadArrayOfInt32(); // default keys
            infos.Add((actionFriendly, component, componentFriendly));
        }

        return infos;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // Removing the action (not just emptying its keys) also drops it from System Settings → Shortcuts.
        var removed = await Unregister
            .CallAsync(
                connection,
                static (message, _) => message.GetBodyReader().ReadBool(),
                "ss",
                (ref MessageWriter writer) =>
                {
                    writer.WriteString(ActionId[0]);
                    writer.WriteString(ActionId[1]);
                })
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Unregistered pick shortcut (was registered: {Removed})", removed);

        await desktopIntegration.RemovePickLauncherAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<List<int[]>> ReadKeysAsync(CancellationToken cancellationToken) =>
        ShortcutKeys
            .CallAsync(connection, static (message, _) => ReadKeySequences(message), "as",
                (ref MessageWriter writer) => writer.WriteArray(ActionId))
            .WaitAsync(cancellationToken);

    /// <param name="sequences">QKeySequences, each holding up to four key combinations.</param>
    private Task<List<int[]>> WriteKeysAsync(IReadOnlyList<int[]> sequences, CancellationToken cancellationToken) =>
        SetShortcutKeys
            .CallAsync(
                connection,
                static (message, _) => ReadKeySequences(message),
                "asa(ai)u",
                (ref MessageWriter writer) =>
                {
                    writer.WriteArray(ActionId);
                    var array = writer.WriteArrayStart(DBusType.Struct);
                    foreach (var sequence in sequences)
                    {
                        // QKeySequence is marshalled as a struct holding an int array.
                        writer.WriteStructureStart();
                        writer.WriteArray(sequence);
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
