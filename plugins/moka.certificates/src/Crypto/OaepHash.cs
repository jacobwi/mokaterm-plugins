namespace Moka.Certificates.Crypto;

/// <summary>The hash RSA-OAEP uses. SHA-1 is what OpenSSL's pkeyutl uses by default, so it stays for reading its output.</summary>
public enum OaepHash
{
	Sha256,
	Sha1,
}
