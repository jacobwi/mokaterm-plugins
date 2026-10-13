namespace Moka.Certificates.ServerFiles;

/// <summary>What the read script said about one file: its state (pem, der, missing, unreadable, folder, other) and what followed.</summary>
public sealed record FileRead(string State, string Content);
