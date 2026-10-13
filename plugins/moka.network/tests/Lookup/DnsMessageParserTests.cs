using Moka.Network.Lookup;

namespace Moka.Network.Tests.Lookup;

public sealed class DnsMessageParserTests
{
	private const int ResponseFlags = 0x8180;

	[Fact]
	public void Parse_ReadsAnAnswerWhoseNamePointsAtTheQuestion()
	{
		byte[] message = new Packet()
			.Header(0x1234, ResponseFlags, 1, 1)
			.Question("example.com", 1)
			.Pointer(12).Fixed(1, 300, [93, 184, 216, 34])
			.ToArray();

		DnsMessage parsed = DnsMessageParser.Parse(message);

		Assert.Equal(0x1234, parsed.Header.Id);
		Assert.True(parsed.Header.IsResponse);
		Assert.Equal(["qr", "rd", "ra"], parsed.Header.FlagNames());
		Assert.Equal(0, parsed.ResponseCode);
		Assert.Equal("NOERROR", parsed.ResponseCodeName);
		DnsQuestion question = Assert.Single(parsed.Questions);
		Assert.Equal(new DnsQuestion("example.com", 1, 1), question);
		DnsRecord answer = Assert.Single(parsed.Answers);
		Assert.Equal("example.com", answer.Name);
		Assert.Equal(300u, answer.Ttl);
		Assert.Equal("A", answer.TypeName);
		Assert.Equal("93.184.216.34", answer.Data);
		Assert.False(answer.Malformed);
		Assert.Equal(message.Length, parsed.Size);
	}

	[Fact]
	public void Parse_FormatsEveryOfferedType()
	{
		Packet packet = new Packet().Header(1, ResponseFlags, 1, 9).Question("example.com", 255);
		packet.Pointer(12).Fixed(28, 60, Convert.FromHexString("20010db8000000000000000000000001"));
		packet.Pointer(12).Fixed(5, 60, Packet.Data(data => data.Labels("www").Pointer(12)));
		packet.Pointer(12).Fixed(15, 60, Packet.Data(data => data.U16(10).Labels("mail").Pointer(12)));
		packet.Pointer(12).Fixed(2, 60, Packet.Data(data => data.Name("ns1.example.net")));
		packet.Pointer(12).Fixed(16, 60, Packet.Data(data => data.U8(10).Raw("v=spf1 -al"u8.ToArray()).U8(4).Raw("a\"b\\"u8.ToArray())));
		packet.Pointer(12).Fixed(6, 60, Packet.Data(data => data.Labels("ns1").Pointer(12).Labels("hostmaster").Pointer(12).U32(2024010101).U32(7200).U32(3600).U32(1209600).U32(300)));
		packet.Pointer(12).Fixed(33, 60, Packet.Data(data => data.U16(10).U16(5).U16(5060).Name("sip.example.com")));
		packet.Pointer(12).Fixed(257, 60, Packet.Data(data => data.U8(0).U8(5).Raw("issue"u8.ToArray()).Raw("letsencrypt.org"u8.ToArray())));
		packet.Pointer(12).Fixed(12, 60, Packet.Data(data => data.Name("host.example.org")));

		DnsMessage parsed = DnsMessageParser.Parse(packet.ToArray());

		Assert.Equal(
			[
				"2001:db8::1",
				"www.example.com",
				"10 mail.example.com",
				"ns1.example.net",
				"\"v=spf1 -al\" \"a\\\"b\\\\\"",
				"ns1.example.com hostmaster.example.com 2024010101 7200 3600 1209600 300",
				"10 5 5060 sip.example.com",
				"0 issue \"letsencrypt.org\"",
				"host.example.org",
			],
			parsed.Answers.Select(record => record.Data));
		Assert.All(parsed.Answers, record => Assert.False(record.Malformed));
	}

	[Fact]
	public void Parse_ShowsAnUnknownTypeInTheGenericForm()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 1).Question("example.com", 65).Pointer(12).Fixed(65, 60, [0x00, 0x01, 0xff]).ToArray();

		DnsRecord record = Assert.Single(DnsMessageParser.Parse(message).Answers);

		Assert.Equal("HTTPS", record.TypeName);
		Assert.Equal(@"\# 3 0001ff", record.Data);
		Assert.False(record.Malformed);
	}

	[Fact]
	public void Parse_CapsTheHexOfLongUnknownData()
	{
		byte[] data = new byte[DnsRecordData.MaxHexBytes + 44];
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 1).Question("example.com", 99).Pointer(12).Fixed(99, 60, data).ToArray();

		DnsRecord record = Assert.Single(DnsMessageParser.Parse(message).Answers);

		Assert.StartsWith($@"\# {data.Length} 0000", record.Data, StringComparison.Ordinal);
		Assert.EndsWith("... (44 more bytes)", record.Data, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_ReadsTheOptRecordIntoThePayloadSizeAndTheExtendedCode()
	{
		// Extended code 1 above the header's 0 is 16, BADVERS.
		byte[] message = new Packet()
			.Header(7, ResponseFlags, 1, 0, 0, 1)
			.Question("example.com", 1)
			.U8(0).U16(41).U16(4096).U32(0x01000000).U16(0)
			.ToArray();

		DnsMessage parsed = DnsMessageParser.Parse(message);

		Assert.Empty(parsed.Additional);
		Assert.Equal(4096, parsed.EdnsPayloadSize);
		Assert.Equal(16, parsed.ResponseCode);
		Assert.Equal("BADVERS", parsed.ResponseCodeName);
	}

	[Fact]
	public void Parse_EscapesControlCharactersInNamesAndText()
	{
		byte[] message = new Packet()
			.Header(1, ResponseFlags, 1, 1)
			.Question("example.com", 16)
			.U8(5).Raw((byte)'a', 0x1b, (byte)'.', (byte)'\\', 0x07).Pointer(12)
			.Fixed(16, 60, Packet.Data(data => data.U8(4).Raw(0x1b, (byte)'[', (byte)'2', (byte)'J')))
			.ToArray();

		DnsRecord record = Assert.Single(DnsMessageParser.Parse(message).Answers);

		Assert.Equal(@"a\027\.\\\007.example.com", record.Name);
		Assert.Equal("\"\\027[2J\"", record.Data);
		Assert.DoesNotContain(record.Name + record.Data, char.IsControl);
	}

	[Fact]
	public void Parse_RefusesAMessageShorterThanAHeader() =>
		Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(new byte[11]));

	[Fact]
	public void Parse_RefusesAPointerThatPointsForward()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).Pointer(20).U16(1).U16(1).ToArray();

		DnsFormatException error = Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
		Assert.Contains("points forward", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_RefusesAPointerToItself()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).Pointer(12).U16(1).U16(1).ToArray();

		Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
	}

	[Fact]
	public void Parse_RefusesAPointerBackIntoTheNameItBelongsTo()
	{
		// "a" then a pointer to the "a": each pass reads the label and jumps back again, a loop with every pointer
		// pointing backwards.
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).Labels("a").Pointer(12).U16(1).U16(1).ToArray();

		Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
	}

	[Fact]
	public void Parse_FollowsAChainOfPointersThatEachGoFurtherBack()
	{
		Packet packet = new Packet().Header(1, ResponseFlags, 1, 1).Question("example.com", 1);
		int www = packet.Position;
		packet.Labels("www").Pointer(12).Fixed(5, 60, Packet.Data(data => data.Labels("cdn").Pointer(www)));

		DnsRecord record = Assert.Single(DnsMessageParser.Parse(packet.ToArray()).Answers);

		Assert.Equal("www.example.com", record.Name);
		Assert.Equal("cdn.www.example.com", record.Data);
	}

	[Fact]
	public void Parse_RefusesTheReservedLabelTypes()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).U8(0x41).Raw(new byte[2]).U16(1).U16(1).ToArray();

		DnsFormatException error = Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
		Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_RefusesALabelThatRunsPastTheEnd()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).U8(20).Raw("abc"u8.ToArray()).ToArray();

		Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
	}

	[Fact]
	public void Parse_RefusesANameLongerThan255Bytes()
	{
		string label = new('a', 63);
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).Question(string.Join('.', label, label, label, label), 1).ToArray();

		DnsFormatException error = Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
		Assert.Contains("255", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_CountsPointedToLabelsAgainstTheNameLimit()
	{
		string label = new('b', 63);
		byte[] message = new Packet()
			.Header(1, ResponseFlags, 1, 1)
			.Question(string.Join('.', label, label, label), 1)
			.Labels(label + "." + label).Pointer(12).Fixed(1, 60, [1, 2, 3, 4])
			.ToArray();

		Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
	}

	[Fact]
	public void Parse_RefusesRecordDataThatRunsPastTheEnd()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 1).Question("example.com", 1).Pointer(12).U16(1).U16(1).U32(60).U16(40).Raw(1, 2, 3, 4).ToArray();

		DnsFormatException error = Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
		Assert.Contains("past the end", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_RefusesCountsThatClaimMoreRecordsThanThereAre()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 3).Question("example.com", 1).Pointer(12).Fixed(1, 60, [1, 2, 3, 4]).ToArray();

		Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(message));
	}

	[Fact]
	public void Parse_RefusesCountsAboveTheCapBeforeReadingAnything()
	{
		byte[] manyRecords = new Packet().Header(1, ResponseFlags, 1, 60000).ToArray();
		byte[] manyQuestions = new Packet().Header(1, ResponseFlags, 5, 0).ToArray();

		Assert.Contains("claims", Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(manyRecords)).Message, StringComparison.Ordinal);
		Assert.Contains("questions", Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(manyQuestions)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_MarksRecordDataOfTheWrongShapeAndKeepsReading()
	{
		byte[] message = new Packet()
			.Header(1, ResponseFlags, 1, 4)
			.Question("example.com", 1)
			.Pointer(12).Fixed(1, 60, [10, 0, 0])
			.Pointer(12).Fixed(16, 60, [9, (byte)'s', (byte)'h', (byte)'o'])
			.Pointer(12).Fixed(5, 60, Packet.Data(data => data.Labels("runs").Labels("on")))
			.Pointer(12).Fixed(1, 60, [10, 0, 0, 1])
			.ToArray();

		DnsMessage parsed = DnsMessageParser.Parse(message);

		Assert.Equal(4, parsed.Answers.Count);
		Assert.True(parsed.Answers[0].Malformed);
		Assert.Equal(@"\# 3 0a0000", parsed.Answers[0].Data);
		Assert.True(parsed.Answers[1].Malformed);
		Assert.True(parsed.Answers[2].Malformed);
		Assert.False(parsed.Answers[3].Malformed);
		Assert.Equal("10.0.0.1", parsed.Answers[3].Data);
	}

	[Fact]
	public void Parse_RefusesAnEmptyTxtRecordAsMalformed()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 1).Question("example.com", 16).Pointer(12).Fixed(16, 60, []).ToArray();

		DnsRecord record = Assert.Single(DnsMessageParser.Parse(message).Answers);

		Assert.True(record.Malformed);
		Assert.Equal(@"\# 0", record.Data);
	}

	[Fact]
	public void Parse_ReadsTheRootName()
	{
		byte[] message = new Packet().Header(1, ResponseFlags, 1, 0).U8(0).U16(2).U16(1).ToArray();

		Assert.Equal(DnsName.Root, Assert.Single(DnsMessageParser.Parse(message).Questions).Name);
	}

	[Fact]
	public void Parse_SurvivesRandomBytes()
	{
		Random random = new(20261012);
		for (int round = 0; round < 2000; round++)
		{
			byte[] message = new byte[random.Next(0, 300)];
			random.NextBytes(message);
			try
			{
				DnsMessageParser.Parse(message);
			}
			catch (DnsFormatException)
			{
				// Refusing is fine; anything else escaping is not.
			}
		}
	}

	[Fact]
	public void Parse_SurvivesEveryTruncationOfAValidMessage()
	{
		byte[] message = new Packet()
			.Header(1, ResponseFlags, 1, 2)
			.Question("example.com", 15)
			.Pointer(12).Fixed(15, 60, Packet.Data(data => data.U16(10).Labels("mail").Pointer(12)))
			.Pointer(12).Fixed(6, 60, Packet.Data(data => data.Labels("ns1").Pointer(12).Labels("hostmaster").Pointer(12).U32(1).U32(2).U32(3).U32(4).U32(5)))
			.ToArray();

		for (int length = 0; length < message.Length; length++)
		{
			try
			{
				DnsMessageParser.Parse(message, length);
			}
			catch (DnsFormatException)
			{
				// Expected for every cut that loses a needed byte.
			}
		}

		Assert.Equal(2, DnsMessageParser.Parse(message).Answers.Count);
	}

	[Theory]
	[InlineData(0x1234, 0x8180, "example.com", 1, 1, true)]
	[InlineData(0x1234, 0x8180, "EXAMPLE.com", 1, 1, true)]
	[InlineData(0x1235, 0x8180, "example.com", 1, 1, false)]
	[InlineData(0x1234, 0x0100, "example.com", 1, 1, false)]
	[InlineData(0x1234, 0x8180, "example.org", 1, 1, false)]
	[InlineData(0x1234, 0x8180, "example.com", 28, 1, false)]
	[InlineData(0x1234, 0x8180, "example.com", 1, 3, false)]
	public void Answers_TakesOnlyTheSameIdAndTheExactQuestion(int id, int flags, string name, int type, int recordClass, bool expected)
	{
		DnsMessage reply = DnsMessageParser.Parse(new Packet().Header(id, flags, 1, 0).Question(name, type, recordClass).ToArray());

		Assert.Equal(expected, DnsMessageParser.Answers(reply, 0x1234, "example.com", 1));
	}

	[Fact]
	public void Answers_RefusesAReplyWithTwoQuestions()
	{
		DnsMessage reply = DnsMessageParser.Parse(new Packet().Header(1, ResponseFlags, 2, 0).Question("example.com", 1).Question("example.com", 1).ToArray());

		Assert.False(DnsMessageParser.Answers(reply, 1, "example.com", 1));
	}

	[Fact]
	public void Build_WritesAQueryTheParserReadsBack()
	{
		byte[] query = DnsQueryBuilder.Build(0xBEEF, "_sip._tcp.example.com", (int)DnsRecordType.SRV);

		DnsMessage parsed = DnsMessageParser.Parse(query);

		Assert.Equal(0xBEEF, parsed.Header.Id);
		Assert.False(parsed.Header.IsResponse);
		Assert.True(parsed.Header.RecursionDesired);
		Assert.True(parsed.Header.AuthenticData);
		Assert.Equal(new DnsQuestion("_sip._tcp.example.com", 33, 1), Assert.Single(parsed.Questions));
		Assert.Equal(DnsQueryBuilder.EdnsPayloadSize, parsed.EdnsPayloadSize);
		Assert.Empty(parsed.Additional);
	}

	[Fact]
	public void Build_WritesTheRootQuestion()
	{
		DnsMessage parsed = DnsMessageParser.Parse(DnsQueryBuilder.Build(1, DnsName.Root, (int)DnsRecordType.NS));

		Assert.Equal(DnsName.Root, Assert.Single(parsed.Questions).Name);
	}
}
