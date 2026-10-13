using System.Globalization;
using System.Text;

namespace Moka.DevTools.Text;

/// <summary>Bytes as hex, one line or as a dump with offsets and the printable characters beside them.</summary>
public static class HexText
{
	/// <summary>The most bytes a dump writes; past it the dump says how many more there are.</summary>
	public const int MaxDumpBytes = 64 * 1024;

	public static string Format(ReadOnlySpan<byte> bytes, bool upper = false, string separator = "")
	{
		ArgumentNullException.ThrowIfNull(separator);
		if (separator.Length == 0)
		{
			return upper ? Convert.ToHexString(bytes) : Convert.ToHexStringLower(bytes);
		}

		StringBuilder text = new(bytes.Length * (2 + separator.Length));
		string format = upper ? "X2" : "x2";
		for (int index = 0; index < bytes.Length; index++)
		{
			if (index > 0)
			{
				text.Append(separator);
			}

			text.Append(bytes[index].ToString(format, CultureInfo.InvariantCulture));
		}

		return text.ToString();
	}

	/// <summary>
	/// Sixteen bytes a line: the offset, the bytes in two groups of eight, and the bytes as ASCII with a dot for anything
	/// that does not print, the way <c>hexdump -C</c> shows them.
	/// </summary>
	public static string Dump(ReadOnlySpan<byte> bytes)
	{
		int shown = Math.Min(bytes.Length, MaxDumpBytes);
		StringBuilder text = new((shown / 16 + 2) * 80);
		for (int offset = 0; offset < shown; offset += 16)
		{
			int count = Math.Min(16, shown - offset);
			text.Append(offset.ToString("x8", CultureInfo.InvariantCulture)).Append("  ");
			for (int index = 0; index < 16; index++)
			{
				text.Append(index < count ? bytes[offset + index].ToString("x2", CultureInfo.InvariantCulture) : "  ");
				text.Append(index == 7 ? "  " : " ");
			}

			text.Append(" |");
			for (int index = 0; index < count; index++)
			{
				byte value = bytes[offset + index];
				text.Append(value is >= 0x20 and < 0x7f ? (char)value : '.');
			}

			text.Append('|').Append('\n');
		}

		if (bytes.Length > shown)
		{
			text.Append(string.Create(CultureInfo.InvariantCulture, $"{bytes.Length - shown:N0} more bytes not shown\n"));
		}

		return text.ToString();
	}
}
