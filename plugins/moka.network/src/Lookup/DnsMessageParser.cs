namespace Moka.Network.Lookup;

/// <summary>
/// Reads a whole DNS message. Everything in it came from the network, so the counts are capped before anything is
/// read, every field is bounds-checked by <see cref="DnsReader"/>, and a record's data is read inside its own length.
/// A message whose structure is broken throws <see cref="DnsFormatException"/>; one record whose data does not fit its
/// type is kept and marked instead.
/// </summary>
public static class DnsMessageParser
{
	public const int HeaderSize = 12;

	/// <summary>A reply echoes the one question it answers; more than this is not a reply to anything we send.</summary>
	public const int MaxQuestions = 4;

	/// <summary>Records across the three sections. A full 64 KiB message of the smallest records holds about 5,900.</summary>
	public const int MaxRecords = 1000;

	public static DnsMessage Parse(byte[] message) => Parse(message, message?.Length ?? 0);

	public static DnsMessage Parse(byte[] message, int length)
	{
		ArgumentNullException.ThrowIfNull(message);
		if (length < HeaderSize)
		{
			throw new DnsFormatException($"The message is {length} bytes, shorter than a DNS header.");
		}

		DnsReader reader = new(message, length);
		ushort id = reader.ReadUInt16();
		ushort flags = reader.ReadUInt16();
		int questionCount = reader.ReadUInt16();
		int answerCount = reader.ReadUInt16();
		int authorityCount = reader.ReadUInt16();
		int additionalCount = reader.ReadUInt16();

		if (questionCount > MaxQuestions)
		{
			throw new DnsFormatException($"The message claims {questionCount} questions.");
		}

		if (answerCount + authorityCount + additionalCount > MaxRecords)
		{
			throw new DnsFormatException($"The message claims {answerCount + authorityCount + additionalCount} records, more than the {MaxRecords} read here.");
		}

		DnsHeader header = new()
		{
			Id = id,
			IsResponse = (flags & 0x8000) != 0,
			Opcode = (flags >> 11) & 0xF,
			Authoritative = (flags & 0x0400) != 0,
			Truncated = (flags & 0x0200) != 0,
			RecursionDesired = (flags & 0x0100) != 0,
			RecursionAvailable = (flags & 0x0080) != 0,
			AuthenticData = (flags & 0x0020) != 0,
			CheckingDisabled = (flags & 0x0010) != 0,
			ResponseCode = flags & 0xF,
			QuestionCount = questionCount,
			AnswerCount = answerCount,
			AuthorityCount = authorityCount,
			AdditionalCount = additionalCount,
		};

		List<DnsQuestion> questions = new(questionCount);
		for (int index = 0; index < questionCount; index++)
		{
			string name = reader.ReadName();
			int type = reader.ReadUInt16();
			int recordClass = reader.ReadUInt16();
			questions.Add(new DnsQuestion(name, type, recordClass));
		}

		Edns edns = new();
		List<DnsRecord> answers = ReadSection(reader, answerCount, null);
		List<DnsRecord> authority = ReadSection(reader, authorityCount, null);
		List<DnsRecord> additional = ReadSection(reader, additionalCount, edns);

		return new DnsMessage
		{
			Header = header,
			Questions = questions,
			Answers = answers,
			Authority = authority,
			Additional = additional,
			EdnsPayloadSize = edns.PayloadSize,
			ResponseCode = (edns.ExtendedCode << 4) | header.ResponseCode,
			Size = length,
		};
	}

	/// <summary>
	/// True when <paramref name="message"/> answers the query that carried <paramref name="id"/>: a response, the same id
	/// and exactly the question asked. A reply that echoes another question is not taken, whatever its id says.
	/// </summary>
	public static bool Answers(DnsMessage message, ushort id, string name, int type)
	{
		ArgumentNullException.ThrowIfNull(message);
		if (!message.Header.IsResponse || message.Header.Id != id || message.Questions.Count != 1)
		{
			return false;
		}

		DnsQuestion question = message.Questions[0];
		return question.Type == type && question.Class == DnsQueryBuilder.ClassInternet && DnsName.SameName(question.Name, name);
	}

	private static List<DnsRecord> ReadSection(DnsReader reader, int count, Edns? edns)
	{
		List<DnsRecord> records = new(count);
		for (int index = 0; index < count; index++)
		{
			string name = reader.ReadName();
			int type = reader.ReadUInt16();
			int recordClass = reader.ReadUInt16();
			uint ttl = reader.ReadUInt32();
			int length = reader.ReadUInt16();
			if (length > reader.Remaining)
			{
				throw new DnsFormatException("A record's data runs past the end of the message.");
			}

			int end = reader.Position + length;
			if (type == (int)DnsRecordType.OPT && edns is not null)
			{
				// The pseudo record's class is the UDP size and its TTL carries the upper bits of the response code. Only
				// the first one counts; RFC 6891 makes a second one an error the server should never send.
				if (edns.PayloadSize is null)
				{
					edns.PayloadSize = recordClass;
					edns.ExtendedCode = (int)(ttl >> 24);
				}

				reader.Seek(end);
				continue;
			}

			(string data, bool malformed) = DnsRecordData.Format(reader, type, end);
			reader.Seek(end);
			records.Add(new DnsRecord
			{
				Name = name,
				Type = type,
				Class = recordClass,
				Ttl = ttl,
				Data = data,
				Malformed = malformed,
			});
		}

		return records;
	}

	private sealed class Edns
	{
		public int? PayloadSize { get; set; }

		public int ExtendedCode { get; set; }
	}
}
