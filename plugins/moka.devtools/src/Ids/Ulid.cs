using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Moka.DevTools.Ids;

/// <summary>
/// A ULID: 48 bits of Unix time in milliseconds and 80 random bits, written as 26 characters of Crockford's base32 so
/// that sorting the text sorts by time.
/// </summary>
public readonly record struct Ulid(UInt128 Value)
{
	public const int Length = 26;

	/// <summary>The latest time a ULID can carry: 2^48 - 1 milliseconds after 1970, in the year 10889.</summary>
	public const long MaxMilliseconds = (1L << 48) - 1;

	private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

	private static readonly UInt128 RandomMask = (UInt128.One << 80) - 1;

	/// <summary>Milliseconds since 1970-01-01 UTC.</summary>
	public long Milliseconds => (long)(ulong)(Value >> 80);

	/// <summary>
	/// The time inside, which is only as good as the clock of whatever made it. Null past the year 9999, which a ULID can
	/// carry and a date here cannot.
	/// </summary>
	public DateTimeOffset? Time => Milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
		? DateTimeOffset.FromUnixTimeMilliseconds(Milliseconds)
		: null;

	/// <summary>The 80 random bits.</summary>
	public UInt128 Random => Value & RandomMask;

	/// <summary>The same 128 bits in the order a UUID is written, which is how databases often store a ULID.</summary>
	public byte[] ToBytes()
	{
		byte[] bytes = new byte[16];
		BinaryPrimitives.WriteUInt128BigEndian(bytes, Value);
		return bytes;
	}

	public static Ulid FromBytes(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadUInt128BigEndian(bytes));

	public static Ulid Create(long milliseconds, ReadOnlySpan<byte> random)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, MaxMilliseconds);
		if (random.Length != 10)
		{
			throw new ArgumentException("A ULID takes exactly 10 random bytes.", nameof(random));
		}

		Span<byte> bytes = stackalloc byte[16];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)milliseconds << 16);
		random.CopyTo(bytes[6..]);
		return new Ulid(BinaryPrimitives.ReadUInt128BigEndian(bytes));
	}

	/// <summary>
	/// <paramref name="count"/> ULIDs for one moment. Within a millisecond each one is the one before plus one, the
	/// spec's monotonic rule, so a batch sorts in the order it was made.
	/// </summary>
	public static IReadOnlyList<Ulid> CreateMany(int count, DateTimeOffset now)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
		long milliseconds = Math.Clamp(now.ToUnixTimeMilliseconds(), 0, MaxMilliseconds);
		Span<byte> random = stackalloc byte[10];
		RandomNumberGenerator.Fill(random);
		Ulid current = Create(milliseconds, random);
		List<Ulid> list = new(count) { current };
		for (int index = 1; index < count; index++)
		{
			if ((current.Value & RandomMask) == RandomMask)
			{
				// 80 bits ran out within one millisecond: move to the next one with new randomness, which still sorts after.
				milliseconds = Math.Min(milliseconds + 1, MaxMilliseconds);
				RandomNumberGenerator.Fill(random);
				current = Create(milliseconds, random);
			}
			else
			{
				current = new Ulid(current.Value + 1);
			}

			list.Add(current);
		}

		return list;
	}

	public override string ToString()
	{
		Span<char> text = stackalloc char[Length];
		for (int index = 0; index < Length; index++)
		{
			int shift = (Length - 1 - index) * 5;
			text[index] = Alphabet[(int)(uint)((Value >> shift) & 31)];
		}

		return new string(text);
	}

	/// <summary>
	/// Reads a ULID in any case. Crockford's base32 reads I and L as 1 and O as 0, so those are taken; U and anything else
	/// are refused with their place, and so is a first character above 7, which would need more than 128 bits.
	/// </summary>
	public static Ulid Parse(string text, out bool readAliases)
	{
		ArgumentNullException.ThrowIfNull(text);
		readAliases = false;
		if (text.Length != Length)
		{
			throw new FormatException($"A ULID has 26 characters; this has {text.Length}.");
		}

		UInt128 value = UInt128.Zero;
		for (int index = 0; index < Length; index++)
		{
			char c = char.ToUpperInvariant(text[index]);
			int digit = c switch
			{
				'I' or 'L' => 1,
				'O' => 0,
				_ => Alphabet.IndexOf(c, StringComparison.Ordinal),
			};

			if (c is 'I' or 'L' or 'O')
			{
				readAliases = true;
			}

			if (digit < 0)
			{
				throw new FormatException($"'{text[index]}' at character {index + 1} is not in Crockford's base32 alphabet.");
			}

			if (index == 0 && digit > 7)
			{
				throw new FormatException("A ULID starts with 0 to 7; a larger first character would need more than 128 bits.");
			}

			value = (value << 5) | (uint)digit;
		}

		return new Ulid(value);
	}
}
