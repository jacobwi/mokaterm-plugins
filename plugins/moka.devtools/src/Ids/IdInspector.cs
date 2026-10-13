using System.Buffers.Binary;
using System.Globalization;
using Moka.DevTools.Codecs;
using Moka.DevTools.Text;

namespace Moka.DevTools.Ids;

/// <summary>
/// Reads a pasted UUID or ULID and says what it is: the UUID's variant and version (RFC 9562), and the time a v1, v6, v7
/// or ULID carries. A UUID is read as written, big-endian, never through .NET's Guid, whose byte order differs.
/// </summary>
public static class IdInspector
{
	private static readonly DateTimeOffset GregorianStart = new(1582, 10, 15, 0, 0, 0, TimeSpan.Zero);

	public static IdReport Inspect(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string value = text.Trim();
		if (value.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
		{
			value = value[9..];
		}

		if (value.Length >= 2 && ((value[0] == '{' && value[^1] == '}') || (value[0] == '(' && value[^1] == ')')))
		{
			value = value[1..^1].Trim();
		}

		if (value.Length == 0)
		{
			throw new FormatException("Paste a UUID or a ULID.");
		}

		return value.Length == Ulid.Length && !value.Contains('-', StringComparison.Ordinal)
			? InspectUlid(value)
			: InspectUuid(ParseUuid(value));
	}

	/// <summary>The 16 bytes of a UUID written with or without its dashes.</summary>
	public static byte[] ParseUuid(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		bool dashed = text.Length == 36;
		if (!dashed && text.Length != 32)
		{
			throw new FormatException(string.Create(CultureInfo.InvariantCulture,
				$"A UUID has 32 hex digits, with or without dashes after the 8th, 12th, 16th and 20th; this has {text.Length} characters."));
		}

		byte[] bytes = new byte[16];
		int nibble = 0;
		for (int index = 0; index < text.Length; index++)
		{
			char c = text[index];
			if (dashed && index is 8 or 13 or 18 or 23)
			{
				if (c != '-')
				{
					throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"A dash belongs at character {index + 1}, where there is '{c}'."));
				}

				continue;
			}

			int digit = UrlCodec.HexValue(c);
			if (digit < 0)
			{
				throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"'{c}' at character {index + 1} is not a hex digit."));
			}

			bytes[nibble / 2] |= (byte)(nibble % 2 == 0 ? digit << 4 : digit);
			nibble++;
		}

		return bytes;
	}

	/// <summary>16 bytes as a UUID is written: lower case, 8-4-4-4-12.</summary>
	public static string FormatUuid(ReadOnlySpan<byte> bytes)
	{
		string hex = HexText.Format(bytes);
		return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
	}

	private static IdReport InspectUlid(string text)
	{
		Ulid ulid = Ulid.Parse(text, out bool aliases);
		List<IdFact> facts =
		[
			new("Time", ulid.Time is { } time ? Iso(time) : "after the year 9999"),
			new("Milliseconds", ulid.Milliseconds.ToString(CultureInfo.InvariantCulture)),
			new("Random", HexText.Format(ulid.ToBytes().AsSpan(6))),
			new("As UUID", FormatUuid(ulid.ToBytes())),
		];

		List<string> notes = [];
		if (aliases)
		{
			notes.Add("Read I and L as 1 and O as 0, as Crockford's base32 does.");
		}

		return new IdReport
		{
			Kind = "ULID",
			Canonical = ulid.ToString(),
			Summary = "ULID, Unix time and random",
			Time = ulid.Time,
			Facts = facts,
			Notes = notes,
		};
	}

	private static IdReport InspectUuid(byte[] bytes)
	{
		string canonical = FormatUuid(bytes);
		List<IdFact> facts = [];
		if (bytes.All(value => value == 0) || bytes.All(value => value == 0xFF))
		{
			bool nil = bytes[0] == 0;
			facts.Add(new IdFact("As ULID", Ulid.FromBytes(bytes).ToString()));
			return new IdReport
			{
				Kind = "UUID",
				Canonical = canonical,
				Summary = nil ? "Nil UUID, all zeros" : "Max UUID, all ones",
				Variant = nil ? "none (nil)" : "none (max)",
				Facts = facts,
			};
		}

		string variant = (bytes[8] >> 5) switch
		{
			< 4 => "NCS, reserved for compatibility",
			< 6 => "RFC 9562",
			6 => "Microsoft, reserved",
			_ => "reserved for the future",
		};

		bool standard = (bytes[8] >> 6) == 2;
		int version = bytes[6] >> 4;
		DateTimeOffset? time = null;
		facts.Add(new IdFact("Variant", variant, Mono: false));
		if (standard)
		{
			facts.Add(new IdFact("Version", string.Create(CultureInfo.InvariantCulture, $"{version}, {VersionName(version)}"), Mono: false));
			time = AddVersionFacts(bytes, version, facts);
		}

		facts.Add(new IdFact("As ULID", Ulid.FromBytes(bytes).ToString()));
		List<string> notes = [];
		if (!standard)
		{
			notes.Add("Only the RFC 9562 variant gives the version bits a meaning, so no version is read.");
		}

		return new IdReport
		{
			Kind = "UUID",
			Canonical = canonical,
			Summary = standard ? string.Create(CultureInfo.InvariantCulture, $"v{version}, {VersionName(version)}") : $"{variant} variant",
			Version = standard ? version : null,
			Variant = variant,
			Time = time,
			Facts = facts,
			Notes = notes,
		};
	}

	private static DateTimeOffset? AddVersionFacts(byte[] bytes, int version, List<IdFact> facts)
	{
		switch (version)
		{
			case 1:
			{
				long timestamp = ((long)(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6)) & 0x0FFF) << 48)
					| ((long)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)) << 32)
					| BinaryPrimitives.ReadUInt32BigEndian(bytes);
				return GregorianTime(bytes, timestamp, facts);
			}
			case 6:
			{
				long timestamp = ((long)BinaryPrimitives.ReadUInt32BigEndian(bytes) << 28)
					| ((long)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)) << 12)
					| (BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6)) & 0x0FFFL);
				return GregorianTime(bytes, timestamp, facts);
			}
			case 7:
			{
				long milliseconds = (long)(BinaryPrimitives.ReadUInt64BigEndian(bytes) >> 16);
				DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
				facts.Add(new IdFact("Time", Iso(time)));
				facts.Add(new IdFact("Milliseconds", milliseconds.ToString(CultureInfo.InvariantCulture)));
				return time;
			}
			case 2:
				facts.Add(new IdFact("Domain", bytes[9] switch
				{
					0 => "person (0)",
					1 => "group (1)",
					2 => "organization (2)",
					_ => bytes[9].ToString(CultureInfo.InvariantCulture),
				}, Mono: false));
				facts.Add(new IdFact("Local id", BinaryPrimitives.ReadUInt32BigEndian(bytes).ToString(CultureInfo.InvariantCulture)));
				facts.Add(new IdFact("Node", Node(bytes)));
				return null;
			default:
				return null;
		}
	}

	private static DateTimeOffset GregorianTime(byte[] bytes, long timestamp, List<IdFact> facts)
	{
		// The count is of 100 ns steps since the Gregorian calendar began, which is exactly a .NET tick.
		DateTimeOffset time = GregorianStart.AddTicks(timestamp);
		facts.Add(new IdFact("Time", Iso(time)));
		facts.Add(new IdFact("Clock sequence", (((bytes[8] & 0x3F) << 8) | bytes[9]).ToString(CultureInfo.InvariantCulture)));
		facts.Add(new IdFact("Node", Node(bytes)));
		return time;
	}

	private static string Node(byte[] bytes)
	{
		string mac = HexText.Format(bytes.AsSpan(10, 6), separator: ":");
		return (bytes[10] & 1) != 0 ? $"{mac} (random, the multicast bit is set)" : $"{mac} (a MAC address)";
	}

	private static string VersionName(int version) => version switch
	{
		1 => "time and node",
		2 => "DCE security",
		3 => "name, MD5",
		4 => "random",
		5 => "name, SHA-1",
		6 => "reordered time",
		7 => "Unix time and random",
		8 => "custom",
		_ => "not a defined version",
	};

	private static string Iso(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
