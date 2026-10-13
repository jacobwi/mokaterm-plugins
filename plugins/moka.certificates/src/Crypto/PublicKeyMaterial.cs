using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>A public key on its own (SubjectPublicKeyInfo, or a PKCS#1 RSA public key).</summary>
public sealed class PublicKeyMaterial : MaterialItem
{
	public PublicKeyMaterial(PublicKey key, string format)
		: base(MaterialKind.PublicKey, format)
	{
		ArgumentNullException.ThrowIfNull(key);
		Key = key;
	}

	public PublicKey Key { get; }
}
