namespace Moka.Certificates.Crypto;

/// <summary>
/// The key types this tool generates and uses. Ed25519 is not among them: .NET's certificate APIs cannot make
/// Ed25519 certificates yet (<see cref="System.Security.Cryptography.X509Certificates.CertificateRequest"/> offers
/// RSA and the NIST curves only), so the runtime must add it first. An Ed25519 key or certificate that is opened is
/// still described.
/// </summary>
public enum KeyAlgorithm
{
	Rsa,
	Ecdsa,
}
