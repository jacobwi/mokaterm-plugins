namespace Moka.DevTools.Codecs;

/// <summary>Input a codec cannot read, with the place it stopped at already in the message.</summary>
public sealed class CodecException : Exception
{
	public CodecException()
	{
	}

	public CodecException(string message)
		: base(message)
	{
	}

	public CodecException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public CodecException(string message, int position)
		: base(message) => Position = position;

	/// <summary>The UTF-16 offset of the character that stopped the codec, when there is one.</summary>
	public int? Position { get; }
}
