namespace Moka.Certificates.Crypto;

/// <summary>The kinds of name a subject alternative name (or any GeneralName) can be.</summary>
public enum GeneralNameKind
{
	Dns,
	IpAddress,
	Email,
	Uri,
	DirectoryName,
	RegisteredId,
	Other,
}
