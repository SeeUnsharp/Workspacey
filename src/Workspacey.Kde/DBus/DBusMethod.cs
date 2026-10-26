using Tmds.DBus.Protocol;

namespace Workspacey.Kde.DBus;

internal delegate void ArgumentWriter(ref MessageWriter writer);

/// <summary>Small helper over Tmds.DBus.Protocol's low-level message API.</summary>
internal sealed record DBusMethod(string Destination, string Path, string Interface, string Member)
{
    public Task<T> CallAsync<T>(
        DBusConnection connection,
        MessageValueReader<T> reader,
        string? signature = null,
        ArgumentWriter? writeArguments = null) =>
        connection.CallMethodAsync(CreateMessage(connection, signature, writeArguments), reader);

    /// <remarks>
    /// Also used for Qt methods declared <c>Q_NOREPLY</c> (e.g. KWin's <c>reconfigure</c>): Qt still sends an
    /// empty reply when the caller expects one. Fire-and-forget sending (<c>TrySendMessage</c>) is not an
    /// option because the shared <see cref="DBusConnection.Session"/> connection does not support it.
    /// </remarks>
    public Task CallAsync(DBusConnection connection, string? signature = null, ArgumentWriter? writeArguments = null) =>
        connection.CallMethodAsync(CreateMessage(connection, signature, writeArguments));

    private MessageBuffer CreateMessage(DBusConnection connection, string? signature, ArgumentWriter? writeArguments)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Destination, Path, Interface, Member, signature);
            writeArguments?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
