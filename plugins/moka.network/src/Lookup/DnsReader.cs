using System.Buffers.Binary;
using System.Text;

namespace Moka.Network.Lookup;

/// <summary>
/// Reads a DNS message that came off the network, so every read is checked against the end of the message and a
/// failure is a <see cref="DnsFormatException"/>, never an index out of range. Names follow compression pointers only
/// backwards, each jump to before the run of labels it came from, so a pointer can neither loop nor point at itself,
/// and the hop count is capped on top of that.
/// </summary>
internal sealed class DnsReader
{
	/// <summary>More than any real name needs: a 255 byte name has at most 127 labels, and pointers come between them.</summary>
	public const int MaxPointerHops = 64;

	private readonly byte[] _message;
	private readonly int _length;

	public DnsReader(byte[] message, int length)
	{
		ArgumentNullException.ThrowIfNull(message);
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(length, message.Length);
		_message = message;
		_length = length;
	}

	public int Position { get; private set; }

	public int Length => _length;

	public int Remaining => _length - Position;

	public byte ReadByte()
	{
		Need(1);
		return _message[Position++];
	}

	public ushort ReadUInt16()
	{
		Need(2);
		ushort value = BinaryPrimitives.ReadUInt16BigEndian(_message.AsSpan(Position, 2));
		Position += 2;
		return value;
	}

	public uint ReadUInt32()
	{
		Need(4);
		uint value = BinaryPrimitives.ReadUInt32BigEndian(_message.AsSpan(Position, 4));
		Position += 4;
		return value;
	}

	public ReadOnlySpan<byte> ReadSpan(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		Need(count);
		ReadOnlySpan<byte> span = _message.AsSpan(Position, count);
		Position += count;
		return span;
	}

	/// <summary>Moves to <paramref name="position"/>, which must lie inside the message.</summary>
	public void Seek(int position)
	{
		if (position < 0 || position > _length)
		{
			throw new DnsFormatException("A record runs past the end of the message.");
		}

		Position = position;
	}

	/// <summary>Reads a name in presentation form (see <see cref="DnsName"/>), leaving the reader after its last byte here.</summary>
	public string ReadName()
	{
		StringBuilder text = new();
		int position = Position;
		int segmentStart = position;
		int resume = -1;
		int wireLength = 0;
		int hops = 0;
		while (true)
		{
			if (position >= _length)
			{
				throw new DnsFormatException("A name runs past the end of the message.");
			}

			byte length = _message[position];
			if (length == 0)
			{
				position++;
				if (resume < 0)
				{
					resume = position;
				}

				break;
			}

			switch (length & 0xC0)
			{
				case 0xC0:
					if (position + 1 >= _length)
					{
						throw new DnsFormatException("A name runs past the end of the message.");
					}

					int target = ((length & 0x3F) << 8) | _message[position + 1];
					if (resume < 0)
					{
						resume = position + 2;
					}

					if (target >= segmentStart)
					{
						throw new DnsFormatException("A name points forward or into itself, which no valid message does.");
					}

					if (++hops > MaxPointerHops)
					{
						throw new DnsFormatException("A name follows too many pointers.");
					}

					position = target;
					segmentStart = target;
					continue;
				case 0:
					break;
				default:
					throw new DnsFormatException("A name uses a label type that DNS has reserved.");
			}

			if (position + 1 + length > _length)
			{
				throw new DnsFormatException("A name runs past the end of the message.");
			}

			wireLength += 1 + length;
			if (wireLength + 1 > DnsName.MaxWireLength)
			{
				throw new DnsFormatException($"A name is longer than {DnsName.MaxWireLength} bytes.");
			}

			if (text.Length > 0)
			{
				text.Append('.');
			}

			DnsName.AppendLabel(text, _message.AsSpan(position + 1, length));
			position += 1 + length;
		}

		Position = resume;
		return text.Length == 0 ? DnsName.Root : text.ToString();
	}

	private void Need(int count)
	{
		if (count > _length - Position)
		{
			throw new DnsFormatException("The message ends in the middle of a field.");
		}
	}
}
