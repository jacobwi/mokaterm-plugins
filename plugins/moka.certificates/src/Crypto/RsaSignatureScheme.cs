namespace Moka.Certificates.Crypto;

/// <summary>How an RSA signature is padded. ECDSA has no choice to make.</summary>
public enum RsaSignatureScheme
{
	Pkcs1,
	Pss,
}
