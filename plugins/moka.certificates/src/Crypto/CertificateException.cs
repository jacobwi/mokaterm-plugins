namespace Moka.Certificates.Crypto;

/// <summary>
/// A certificate or key operation that cannot go ahead, with a message written for the user: a wrong password, a key
/// that does not fit, data too long for the key. The message never carries key material or a password.
/// </summary>
public sealed class CertificateException : Exception
{
	public CertificateException()
	{
	}

	public CertificateException(string message)
		: base(message)
	{
	}

	public CertificateException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
