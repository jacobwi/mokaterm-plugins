namespace Moka.Certificates.Crypto;

/// <summary>The RSA sizes offered. Under 2048 bits is not offered, and over 4096 costs more than it buys.</summary>
public enum RsaKeySize
{
	Bits2048,
	Bits3072,
	Bits4096,
}
