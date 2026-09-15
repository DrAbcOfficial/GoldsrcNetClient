namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Marker interface implemented by every parsed server message — engine
/// (<c>svc_*</c>) and user-registered alike. Messages are immutable records;
/// consumers receive them through <see cref="MessageHub"/> subscriptions
/// (<c>connection.Subscribe&lt;TPrint&gt;(...)</c> style).
/// </summary>
/// <remarks>
/// <para>
/// Adding a message for a custom mod needs no library changes: define a record
/// implementing this interface, register a parser for its user-message name in
/// the game profile, and subscribe to it anywhere.
/// </para>
/// </remarks>
public interface IServerMessage;
