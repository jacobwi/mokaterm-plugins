namespace Moka.Certificates.Crypto;

/// <summary>Where one PEM block sits in a text, by its label; <see cref="Problem"/> is set when the block is cut off.</summary>
internal readonly record struct PemBlock(string Label, int Start, int Length, string? Problem);
