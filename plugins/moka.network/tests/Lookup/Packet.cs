using System.Text;

namespace Moka.Network.Tests.Lookup;

/// <summary>Writes DNS messages byte by byte, so a test can build exactly the packet it means, broken ones included.</summary>
internal sealed class Packet
{
	private readonly List<byte> _bytes = [];

	public int Position => _bytes.Count;

	public Packet Header(int id, int flags, int questions, int answers, int authority = 0, int additional = 0) =>
		U16(id).U16(flags).U16(questions).U16(answers).U16(authority).U16(additional);

	public Packet U8(int value)
	{
		_bytes.Add((byte)value);
		return this;
	}

	public Packet U16(int value) => U8(value >> 8).U8(value);

	public Packet U32(uint value) => U16((int)(value >> 16)).U16((int)(value & 0xFFFF));

	public Packet Raw(params byte[] bytes)
	{
		_bytes.AddRange(bytes);
		return this;
	}

	/// <summary>Labels without the final zero, so a pointer or more labels can follow.</summary>
	public Packet Labels(string dotted)
	{
		foreach (string label in dotted.Split('.', StringSplitOptions.RemoveEmptyEntries))
		{
			byte[] bytes = Encoding.ASCII.GetBytes(label);
			U8(bytes.Length).Raw(bytes);
		}

		return this;
	}

	public Packet Name(string dotted) => Labels(dotted).U8(0);

	public Packet Pointer(int offset) => U8(0xC0 | (offset >> 8)).U8(offset & 0xFF);

	public Packet Question(string name, int type, int recordClass = 1) => Name(name).U16(type).U16(recordClass);

	/// <summary>The fixed part of a record after its name, then the data with its length in front.</summary>
	public Packet Fixed(int type, uint ttl, byte[] data, int recordClass = 1) => U16(type).U16(recordClass).U32(ttl).U16(data.Length).Raw(data);

	public byte[] ToArray() => [.. _bytes];

	/// <summary>Data for a record, built the same way.</summary>
	public static byte[] Data(Action<Packet> write)
	{
		Packet data = new();
		write(data);
		return data.ToArray();
	}
}
