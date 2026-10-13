namespace Moka.Certificates.Crypto;

/// <summary>How a private key is written out as PEM.</summary>
public enum PrivateKeyFormat
{
	/// <summary>PKCS#8, <c>-----BEGIN PRIVATE KEY-----</c>, for any algorithm.</summary>
	Pkcs8,

	/// <summary>The algorithm's own form: PKCS#1 for RSA (<c>RSA PRIVATE KEY</c>), SEC1 for EC (<c>EC PRIVATE KEY</c>).</summary>
	Traditional,
}
