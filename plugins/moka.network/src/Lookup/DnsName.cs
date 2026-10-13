using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Moka.Network.Lookup;

/// <summary>
/// Domain names as typed, on the wire and on screen. A name is shown in its ASCII form without the final dot, the root
/// as a lone dot, and any byte that is not a plain printable character as <c>\DDD</c>, so whatever a reply carries in a
/// label reaches the page as text and never as a control character.
/// </summary>
public static class DnsName
{
	/// <summary>The longest name on the wire, length bytes and the final zero included (RFC 1035).</summary>
	public const int MaxWireLength = 255;

	public const int MaxLabelLength = 63;

	/// <summary>The root, the only name with no labels.</summary>
	public const string Root = ".";

	/// <summary>
	/// Turns what was typed into the ASCII name a query carries: trimmed, without the final dot, lower case, a name in
	/// another script through IDNA (<c>bücher.de</c> asks for <c>xn--bcher-kva.de</c>). Letters, digits, hyphens and
	/// underscores (<c>_sip._tcp</c>), and <c>*</c> as a whole label.
	/// </summary>
	public static bool TryNormalize(string? text, [NotNullWhen(true)] out string? name, [NotNullWhen(false)] out string? problem)
	{
		name = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type a name or an address.";
			return false;
		}

		if (input == Root)
		{
			name = Root;
			problem = null;
			return true;
		}

		if (input.EndsWith('.'))
		{
			input = input[..^1];
		}

		if (input.Length > 1000)
		{
			problem = "That is far longer than any domain name.";
			return false;
		}

		foreach (char c in input)
		{
			if (char.IsWhiteSpace(c) || char.IsControl(c))
			{
				problem = "A name has no spaces or control characters in it.";
				return false;
			}
		}

		string ascii = input;
		if (!Ascii.IsValid(input))
		{
			try
			{
				ascii = new IdnMapping().GetAscii(input);
			}
			catch (ArgumentException)
			{
				problem = $"{input} is not a name IDNA can turn into ASCII.";
				return false;
			}
		}

		ascii = ascii.ToLowerInvariant();
		int wire = 1;
		foreach (string label in ascii.Split('.'))
		{
			if (label.Length == 0)
			{
				problem = "A name cannot have two dots in a row or start with a dot.";
				return false;
			}

			if (label.Length > MaxLabelLength)
			{
				problem = $"Each part of a name is at most {MaxLabelLength} characters; {Shorten(label)} is {label.Length}.";
				return false;
			}

			if (label != "*" && !label.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
			{
				problem = $"{Shorten(label)} has a character DNS names do not use. Letters, digits, hyphens and underscores are fine.";
				return false;
			}

			wire += 1 + label.Length;
		}

		if (wire > MaxWireLength)
		{
			problem = $"The name is {wire} bytes on the wire; DNS allows {MaxWireLength}.";
			return false;
		}

		name = ascii;
		problem = null;
		return true;
	}

	/// <summary>A name from <see cref="TryNormalize"/> in wire form: each label after its length, then a zero.</summary>
	public static byte[] Encode(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (name == Root)
		{
			return [0];
		}

		using MemoryStream wire = new();
		foreach (string label in name.Split('.'))
		{
			byte[] bytes = Encoding.ASCII.GetBytes(label);
			if (bytes.Length is 0 or > MaxLabelLength)
			{
				throw new ArgumentException($"{name} is not a name in presentation form.", nameof(name));
			}

			wire.WriteByte((byte)bytes.Length);
			wire.Write(bytes);
		}

		wire.WriteByte(0);
		if (wire.Length > MaxWireLength)
		{
			throw new ArgumentException($"{name} is longer than {MaxWireLength} bytes on the wire.", nameof(name));
		}

		return wire.ToArray();
	}

	/// <summary>True when two names in presentation form are the same name: DNS compares ASCII letters without case.</summary>
	public static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Appends one label as text. A dot or backslash inside a label is escaped with a backslash, and anything outside
	/// printable ASCII as <c>\DDD</c>, which is how zone files write the same bytes (RFC 1035 section 5.1).
	/// </summary>
	internal static void AppendLabel(StringBuilder builder, ReadOnlySpan<byte> label)
	{
		foreach (byte b in label)
		{
			if (b is (byte)'.' or (byte)'\\')
			{
				builder.Append('\\').Append((char)b);
			}
			else if (b is > 0x20 and < 0x7f)
			{
				builder.Append((char)b);
			}
			else
			{
				builder.Append('\\').Append(b.ToString("D3", CultureInfo.InvariantCulture));
			}
		}
	}

	private static string Shorten(string label) => label.Length > 24 ? label[..24] + "..." : label;
}
