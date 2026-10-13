using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Everything <see cref="CertificateReader.Read"/> found in one input, in order, plus what it noticed and could not read.
/// Disposing it disposes every certificate and key it holds.
/// </summary>
public sealed class MaterialSet : IDisposable
{
	public MaterialSet(IReadOnlyList<MaterialItem> items, IReadOnlyList<ReadNote> notes)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(notes);
		Items = items;
		Notes = notes;
	}

	public static MaterialSet Empty => new([], []);

	public IReadOnlyList<MaterialItem> Items { get; }

	public IReadOnlyList<ReadNote> Notes { get; }

	/// <summary>Something is encrypted and the password given (if any) did not open it.</summary>
	public bool NeedsPassword => Items.Any(item => item is LockedKeyMaterial);

	/// <summary>A password was given and refused.</summary>
	public bool PasswordRefused => Items.OfType<LockedKeyMaterial>().Any(item => item.PasswordRefused);

	public IReadOnlyList<X509Certificate2> Certificates => [.. Items.OfType<CertificateMaterial>().Select(item => item.Certificate)];

	public IReadOnlyList<PrivateKeyMaterial> PrivateKeys => [.. Items.OfType<PrivateKeyMaterial>()];

	public IReadOnlyList<PublicKeyMaterial> PublicKeys => [.. Items.OfType<PublicKeyMaterial>()];

	public IReadOnlyList<RequestMaterial> Requests => [.. Items.OfType<RequestMaterial>()];

	/// <summary>The single private key, or a message for the user when there is none or more than one.</summary>
	public PrivateKeyMaterial RequireOnePrivateKey()
	{
		if (NeedsPassword && PrivateKeys.Count == 0)
		{
			throw new CertificateException(PasswordRefused ? "That password does not open the private key." : "The private key is encrypted: enter its password.");
		}

		return PrivateKeys.Count switch
		{
			1 => PrivateKeys[0],
			0 => throw new CertificateException("No private key was found. Paste a PEM private key, or open a PFX with its password."),
			_ => throw new CertificateException("More than one private key was found. Keep only the one to use."),
		};
	}

	/// <summary>
	/// The certificate the private key belongs to, when there is a key; otherwise the first certificate. Null when there
	/// is no certificate at all.
	/// </summary>
	public X509Certificate2? CertificateFor(PrivateKeyMaterial? key)
	{
		if (key is not null && Certificates.FirstOrDefault(certificate => KeyIdentity.Matches(certificate.PublicKey, key.PublicKey)) is { } match)
		{
			return match;
		}

		IReadOnlyList<X509Certificate2> certificates = Certificates;
		return certificates.Count > 0 ? certificates[0] : null;
	}

	public void Dispose()
	{
		foreach (MaterialItem item in Items)
		{
			item.Dispose();
		}
	}
}
