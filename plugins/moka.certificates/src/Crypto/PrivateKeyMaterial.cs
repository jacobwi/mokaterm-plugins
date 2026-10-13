using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// A private key the reader could open: RSA or ECDSA, held in memory only. The key goes away with the item, and nothing
/// that describes it prints the key.
/// </summary>
public sealed class PrivateKeyMaterial : MaterialItem
{
	public PrivateKeyMaterial(AsymmetricAlgorithm key, string format, bool wasEncrypted)
		: base(MaterialKind.PrivateKey, format)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (key is not RSA and not ECDsa)
		{
			throw new ArgumentException("Only RSA and ECDSA keys are held.", nameof(key));
		}

		Key = key;
		WasEncrypted = wasEncrypted;
		PublicKey = new PublicKey(key);
	}

	/// <summary>An <see cref="RSA"/> or an <see cref="ECDsa"/>.</summary>
	public AsymmetricAlgorithm Key { get; }

	/// <summary>The public half, for matching against certificates and for describing.</summary>
	public PublicKey PublicKey { get; }

	/// <summary>It was stored encrypted, in an encrypted PEM block or a PFX.</summary>
	public bool WasEncrypted { get; }

	public override string ToString() => $"{Kind} ({Format})";

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Key.Dispose();
		}

		base.Dispose(disposing);
	}
}
