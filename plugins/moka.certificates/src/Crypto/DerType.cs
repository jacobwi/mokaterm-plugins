namespace Moka.Certificates.Crypto;

/// <summary>What a DER structure is, as <see cref="DerDetector"/> tells from its shape.</summary>
public enum DerType
{
	Unknown,
	Certificate,
	CertificateRequest,
	PrivateKey,
	EncryptedPrivateKey,
	RsaPrivateKey,
	EcPrivateKey,
	PublicKey,
	RsaPublicKey,
	Pfx,
	Pkcs7,
}
