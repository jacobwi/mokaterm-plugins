using System.Globalization;

namespace Moka.DevTools.Text;

/// <summary>A place in a text as people read it: line and column from 1, a surrogate pair counting as one character.</summary>
public readonly record struct TextPosition(int Line, int Column)
{
	/// <summary>The line and column of <paramref name="index"/>, a UTF-16 offset into <paramref name="text"/>.</summary>
	public static TextPosition Of(string text, int index)
	{
		ArgumentNullException.ThrowIfNull(text);
		index = Math.Clamp(index, 0, text.Length);
		int line = 1;
		int column = 1;
		for (int at = 0; at < index; at++)
		{
			char c = text[at];
			if (c == '\n')
			{
				line++;
				column = 1;
			}
			else if (c == '\r' && (at + 1 >= text.Length || text[at + 1] != '\n'))
			{
				line++;
				column = 1;
			}
			else if (!char.IsLowSurrogate(c) && c != '\r')
			{
				column++;
			}
		}

		return new TextPosition(line, column);
	}

	public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"line {Line}, column {Column}");
}
