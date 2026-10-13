namespace Moka.Certificates.Crypto;

/// <summary>One attribute of a distinguished name, such as <c>CN=example.com</c>.</summary>
public sealed record NamePart(string Label, string Value);
