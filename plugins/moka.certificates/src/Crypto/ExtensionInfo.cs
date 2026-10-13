namespace Moka.Certificates.Crypto;

/// <summary>One certificate extension in words: its name, whether it is critical, and what it says, a line per fact.</summary>
public sealed record ExtensionInfo(string Name, string Oid, bool Critical, IReadOnlyList<string> Lines);
