namespace Moka.Certificates.Crypto;

/// <summary>One object the reader found, in the order it was found. Keys and certificates are disposed with it.</summary>
public abstract class MaterialItem : IDisposable
{
	private bool _disposed;

	protected MaterialItem(MaterialKind kind, string format)
	{
		Kind = kind;
		Format = format;
	}

	public MaterialKind Kind { get; }

	/// <summary>How it was stored, for the user: "PEM", "DER", "PFX", "PEM, PKCS#1".</summary>
	public string Format { get; }

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
	}
}
