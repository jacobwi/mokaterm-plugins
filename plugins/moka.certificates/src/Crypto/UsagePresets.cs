namespace Moka.Certificates.Crypto;

/// <summary>
/// What a new certificate is for, which decides its key usage and extended key usage. They combine: a certificate can
/// be both a TLS server and a TLS client.
/// </summary>
[Flags]
public enum UsagePresets
{
	None = 0,
	TlsServer = 1,
	TlsClient = 2,
	CodeSigning = 4,
	Email = 8,
	CertificateAuthority = 16,
}
