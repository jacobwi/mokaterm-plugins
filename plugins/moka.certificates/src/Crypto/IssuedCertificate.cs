using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>A certificate a CA signed, with what the generator changed on the way (validity cut to the CA's, names taken from the request).</summary>
public sealed class IssuedCertificate : IDisposable
{
	public IssuedCertificate(X509Certificate2 certificate, IReadOnlyList<string> notes)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		ArgumentNullException.ThrowIfNull(notes);
		Certificate = certificate;
		Notes = notes;
	}

	public X509Certificate2 Certificate { get; }

	public IReadOnlyList<string> Notes { get; }

	public void Dispose() => Certificate.Dispose();
}
