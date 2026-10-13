namespace Moka.Network.Targets;

/// <summary>A probe that could not run or could not finish, with a message the view shows as it is.</summary>
public sealed class ProbeException : Exception
{
	public ProbeException()
	{
	}

	public ProbeException(string message)
		: base(message)
	{
	}

	public ProbeException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
