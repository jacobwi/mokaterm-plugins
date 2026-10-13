namespace Moka.Certificates.Crypto;

/// <summary>An encrypted private key the reader could not open, because no password or the wrong one was given.</summary>
public sealed class LockedKeyMaterial : MaterialItem
{
	public LockedKeyMaterial(string format, string protection, bool passwordRefused)
		: base(MaterialKind.PrivateKey, format)
	{
		Protection = protection;
		PasswordRefused = passwordRefused;
	}

	/// <summary>How it is encrypted, such as "AES-256-CBC, PBKDF2 with HMAC-SHA256, 2,048 rounds".</summary>
	public string Protection { get; }

	/// <summary>A password was given and did not open it.</summary>
	public bool PasswordRefused { get; }
}
