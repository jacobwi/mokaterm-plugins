using Moka.DevTools.Text;

namespace Moka.DevTools.Data;

/// <summary>A document that cannot be read or converted, with where it failed when that is known.</summary>
public sealed class DataException : Exception
{
	public DataException()
	{
	}

	public DataException(string message)
		: base(message)
	{
	}

	public DataException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public DataException(string message, TextPosition position)
		: base($"{message} ({position})") => Position = position;

	public TextPosition? Position { get; }
}
