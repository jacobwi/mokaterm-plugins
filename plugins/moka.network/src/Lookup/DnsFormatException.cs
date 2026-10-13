namespace Moka.Network.Lookup;

/// <summary>A DNS message that cannot be read: cut short, a pointer that does not point back, a name too long.</summary>
public sealed class DnsFormatException : Exception
{
	public DnsFormatException()
	{
	}

	public DnsFormatException(string message)
		: base(message)
	{
	}

	public DnsFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
