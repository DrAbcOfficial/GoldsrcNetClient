namespace GoldsrcNetClient.Core.Io;

/// <summary>
/// Thrown when a read would advance past the end of the buffer. The message
/// pipeline catches it and discards the remainder of the packet — the engine's
/// response to a malformed stream.
/// </summary>
public sealed class EndOfBufferException(string? message = null) : Exception(message ?? "Read past the end of the buffer.");
