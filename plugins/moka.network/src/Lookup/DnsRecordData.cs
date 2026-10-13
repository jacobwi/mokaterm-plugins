using System.Globalization;
using System.Net;
using System.Text;

namespace Moka.Network.Lookup;

/// <summary>
/// Turns a record's data into the text zone files use for it. The reader stands at the data and <c>end</c> is where the
/// record's own length says it stops; data that breaks its type's shape, runs past that end or stops short of it comes
/// back as the RFC 3597 generic form (<c>\# 3 0a0b0c</c>) and is marked malformed.
/// </summary>
internal static class DnsRecordData
{
	/// <summary>The most data bytes shown in the generic form; the rest is counted, not printed.</summary>
	public const int MaxHexBytes = 256;

	public static (string Text, bool Malformed) Format(DnsReader reader, int type, int end)
	{
		int start = reader.Position;
		if (!IsKnown(type))
		{
			return (Generic(reader, start, end), false);
		}

		try
		{
			string? text = type switch
			{
				(int)DnsRecordType.A => Address(reader, end, 4),
				(int)DnsRecordType.AAAA => Address(reader, end, 16),
				(int)DnsRecordType.MX => Mx(reader),
				(int)DnsRecordType.SOA => Soa(reader),
				(int)DnsRecordType.SRV => Srv(reader),
				(int)DnsRecordType.TXT => Txt(reader, end),
				(int)DnsRecordType.CAA => Caa(reader, end),
				_ => reader.ReadName(),
			};

			// A name that ran on past the record's own length read bytes that belong to the next record.
			if (text is not null && reader.Position == end)
			{
				return (text, false);
			}

			return (Generic(reader, start, end), true);
		}
		catch (DnsFormatException)
		{
			return (Generic(reader, start, end), true);
		}
	}

	/// <summary>Bytes as zone files write a character string: quoted, with <c>"</c> and <c>\</c> escaped and anything not printable ASCII as <c>\DDD</c>.</summary>
	public static void AppendQuoted(StringBuilder builder, ReadOnlySpan<byte> bytes)
	{
		builder.Append('"');
		foreach (byte b in bytes)
		{
			if (b is (byte)'"' or (byte)'\\')
			{
				builder.Append('\\').Append((char)b);
			}
			else if (b is >= 0x20 and < 0x7f)
			{
				builder.Append((char)b);
			}
			else
			{
				builder.Append('\\').Append(b.ToString("D3", CultureInfo.InvariantCulture));
			}
		}

		builder.Append('"');
	}

	/// <summary>The types with a shape of their own here; NS, CNAME, PTR and DNAME are one name each.</summary>
	private static bool IsKnown(int type) => type is (int)DnsRecordType.A or (int)DnsRecordType.AAAA or (int)DnsRecordType.NS
		or (int)DnsRecordType.CNAME or (int)DnsRecordType.PTR or 39 or (int)DnsRecordType.MX or (int)DnsRecordType.SOA
		or (int)DnsRecordType.SRV or (int)DnsRecordType.TXT or (int)DnsRecordType.CAA;

	private static string? Address(DnsReader reader, int end, int size)
	{
		if (end - reader.Position != size)
		{
			return null;
		}

		return new IPAddress(reader.ReadSpan(size)).ToString();
	}

	private static string Mx(DnsReader reader)
	{
		ushort preference = reader.ReadUInt16();
		string exchange = reader.ReadName();
		return string.Create(CultureInfo.InvariantCulture, $"{preference} {exchange}");
	}

	private static string Soa(DnsReader reader)
	{
		string primary = reader.ReadName();
		string mailbox = reader.ReadName();
		uint serial = reader.ReadUInt32();
		uint refresh = reader.ReadUInt32();
		uint retry = reader.ReadUInt32();
		uint expire = reader.ReadUInt32();
		uint minimum = reader.ReadUInt32();
		return string.Create(CultureInfo.InvariantCulture, $"{primary} {mailbox} {serial} {refresh} {retry} {expire} {minimum}");
	}

	private static string Srv(DnsReader reader)
	{
		ushort priority = reader.ReadUInt16();
		ushort weight = reader.ReadUInt16();
		ushort port = reader.ReadUInt16();
		string target = reader.ReadName();
		return string.Create(CultureInfo.InvariantCulture, $"{priority} {weight} {port} {target}");
	}

	private static string? Txt(DnsReader reader, int end)
	{
		if (reader.Position == end)
		{
			// A TXT record holds at least one string, even an empty one.
			return null;
		}

		StringBuilder text = new();
		while (reader.Position < end)
		{
			int length = reader.ReadByte();
			if (length > end - reader.Position)
			{
				return null;
			}

			if (text.Length > 0)
			{
				text.Append(' ');
			}

			AppendQuoted(text, reader.ReadSpan(length));
		}

		return text.ToString();
	}

	private static string? Caa(DnsReader reader, int end)
	{
		byte flags = reader.ReadByte();
		int tagLength = reader.ReadByte();
		if (tagLength is 0 or > 15 || tagLength > end - reader.Position)
		{
			return null;
		}

		ReadOnlySpan<byte> tag = reader.ReadSpan(tagLength);
		foreach (byte b in tag)
		{
			if (!char.IsAsciiLetterOrDigit((char)b))
			{
				return null;
			}
		}

		StringBuilder text = new();
		text.Append(flags.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Encoding.ASCII.GetString(tag)).Append(' ');
		AppendQuoted(text, reader.ReadSpan(end - reader.Position));
		return text.ToString();
	}

	private static string Generic(DnsReader reader, int start, int end)
	{
		reader.Seek(start);
		ReadOnlySpan<byte> data = reader.ReadSpan(end - start);
		if (data.IsEmpty)
		{
			return @"\# 0";
		}

		ReadOnlySpan<byte> shown = data.Length > MaxHexBytes ? data[..MaxHexBytes] : data;
		string hex = Convert.ToHexStringLower(shown);
		return data.Length > MaxHexBytes
			? string.Create(CultureInfo.InvariantCulture, $@"\# {data.Length} {hex} ... ({data.Length - MaxHexBytes} more bytes)")
			: string.Create(CultureInfo.InvariantCulture, $@"\# {data.Length} {hex}");
	}
}
