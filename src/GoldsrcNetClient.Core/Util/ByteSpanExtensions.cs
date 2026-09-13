namespace GoldsrcNetClient.Core.Util;

/// <summary>
/// Bounded hex previews for logging (C# 14 extension members): keeps debug
/// dumps of large packets down to a fixed head without repeating the same
/// slice-and-convert dance at every call site.
/// </summary>
public static class ByteSpanExtensions
{
    extension(ReadOnlySpan<byte> source)
    {
        /// <summary>Uppercase hex of at most the first <paramref name="maxBytes"/> bytes.</summary>
        public string ToHexPreview(int maxBytes)
            => Convert.ToHexString(source[..Math.Min(source.Length, maxBytes)]);
    }
}
