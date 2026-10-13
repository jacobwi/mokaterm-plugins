namespace Moka.Certificates.Crypto;

/// <summary>The NIST curves every TLS stack and every platform .NET runs on support.</summary>
public enum EllipticCurve
{
	P256,
	P384,
	P521,
}
