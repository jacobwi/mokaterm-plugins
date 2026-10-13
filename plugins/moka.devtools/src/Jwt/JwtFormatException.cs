namespace Moka.DevTools.Jwt;

/// <summary>Text that is not a token this tool can read, with the reason in words. Never quotes the token.</summary>
public sealed class JwtFormatException : Exception
{
	public JwtFormatException()
	{
	}

	public JwtFormatException(string message)
		: base(message)
	{
	}

	public JwtFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
