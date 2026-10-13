namespace Moka.DevTools.Hashing;

/// <summary>One algorithm's result, or why it has none here.</summary>
internal sealed record HashValue(HashAlgorithmInfo Algorithm, byte[]? Bytes, string? Unavailable);
