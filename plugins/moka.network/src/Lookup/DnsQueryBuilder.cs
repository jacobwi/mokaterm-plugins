using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Moka.Network.Lookup;

/// <summary>Builds the one kind of query the lookup sends: one question, recursion asked for, and an EDNS OPT record.</summary>
public static class DnsQueryBuilder
{
	public const int ClassInternet = 1;

	/// <summary>
	/// The UDP size offered in the OPT record: the DNS Flag Day 2020 figure, small enough that a reply is not fragmented
	/// on any usual path. A bigger answer comes back cut and is asked for again over TCP.
	/// </summary>
	public const int EdnsPayloadSize = 1232;

	/// <summary>
	/// The query for <paramref name="name"/> (presentation form, see <see cref="DnsName.TryNormalize"/>). AD is set as
	/// RFC 6840 suggests, so a validating resolver says whether it checked the answer with DNSSEC.
	/// </summary>
	public static byte[] Build(ushort id, string name, int type)
	{
		byte[] question = DnsName.Encode(name);
		byte[] query = new byte[DnsMessageParser.HeaderSize + question.Length + 4 + 11];
		Span<byte> span = query;
		BinaryPrimitives.WriteUInt16BigEndian(span, id);
		BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0x0100 | 0x0020);
		BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1);
		BinaryPrimitives.WriteUInt16BigEndian(span[10..], 1);

		int position = DnsMessageParser.HeaderSize;
		question.CopyTo(span[position..]);
		position += question.Length;
		BinaryPrimitives.WriteUInt16BigEndian(span[position..], (ushort)type);
		BinaryPrimitives.WriteUInt16BigEndian(span[(position + 2)..], ClassInternet);
		position += 4;

		// OPT: the root name, type 41, the UDP size in the class field, a zero TTL (version 0, no DO bit) and no options.
		span[position] = 0;
		BinaryPrimitives.WriteUInt16BigEndian(span[(position + 1)..], (ushort)DnsRecordType.OPT);
		BinaryPrimitives.WriteUInt16BigEndian(span[(position + 3)..], EdnsPayloadSize);
		return query;
	}

	/// <summary>A transaction id from the system's cryptographic generator: a guessable id is half of a spoofed answer.</summary>
	public static ushort NewId() => (ushort)RandomNumberGenerator.GetInt32(0, 0x10000);
}
