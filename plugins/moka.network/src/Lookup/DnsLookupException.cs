namespace Moka.Network.Lookup;

/// <summary>A lookup that got no usable answer, with a message the view shows as it is.</summary>
public sealed class DnsLookupException : Exception
{
	public DnsLookupException()
	{
	}

	public DnsLookupException(string message)
		: base(message)
	{
	}

	public DnsLookupException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
