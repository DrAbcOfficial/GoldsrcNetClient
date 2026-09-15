namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// A user-registered message with no parser (not part of the active game
/// profile, or a message the mod added after the profile was written). The raw
/// payload is exposed verbatim for custom parsing.
/// </summary>
public sealed record RawUserMessage(byte Index, string? Name, byte[] Data) : IServerMessage;
