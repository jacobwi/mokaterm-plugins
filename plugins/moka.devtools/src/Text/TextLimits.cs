namespace Moka.DevTools.Text;

/// <summary>The sizes every section holds to, so a pasted log or an opened file cannot hold the page or the circuit.</summary>
public static class TextLimits
{
	/// <summary>The longest text any section works on, in characters.</summary>
	public const int MaxTextChars = 4 * 1024 * 1024;

	/// <summary>The largest text file opened into a box, in bytes; the same as <see cref="MaxTextChars"/> for ASCII.</summary>
	public const int MaxTextFileBytes = 4 * 1024 * 1024;

	/// <summary>The largest file hashed. It is streamed, so this only bounds the time it takes.</summary>
	public const long MaxHashFileBytes = 4L * 1024 * 1024 * 1024;

	/// <summary>
	/// The most characters an output box draws. Longer results are copied or saved whole; a text node of many megabytes
	/// makes the page slow to scroll and select in.
	/// </summary>
	public const int DisplayChars = 256 * 1024;

	/// <summary>
	/// The most characters a text box holds on the web host. Every keystroke sends the whole box to the server, and a
	/// message over the host's 1 MiB receive limit would end the circuit, so a longer file stays a file.
	/// </summary>
	public const int WebBoxChars = 256 * 1024;

	/// <summary>The largest box a paste may produce on the web host, measured as the message the page would send.</summary>
	public const int WebPasteBytes = 900 * 1024;

	/// <summary>Text this long or longer is worked on off the render thread.</summary>
	public const int BackgroundChars = 32 * 1024;
}
