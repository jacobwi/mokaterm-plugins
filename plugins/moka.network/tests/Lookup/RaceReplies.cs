using System.Net;
using Moka.Network.Lookup;

namespace Moka.Network.Tests.Lookup;

/// <summary>Builds lookup answers a race test needs, the way the message parser would have left them.</summary>
internal static class RaceReplies
{
	public static DnsLookupResult Address(string data, int type = (int)DnsRecordType.A) => Reply([Record(type, data)]);

	/// <summary>An answer with no records, so only its response code tells the story.</summary>
	public static DnsLookupResult Code(int code) => Reply([], code);

	private static DnsLookupResult Reply(IReadOnlyList<DnsRecord> answers, int code = 0) => new(
		new DnsExchange
		{
			Message = new DnsMessage
			{
				Header = new DnsHeader
				{
					Id = 1,
					IsResponse = true,
					Opcode = 0,
					Authoritative = false,
					Truncated = false,
					RecursionDesired = true,
					RecursionAvailable = true,
					AuthenticData = false,
					CheckingDisabled = false,
					ResponseCode = code,
					QuestionCount = 1,
					AnswerCount = answers.Count,
					AuthorityCount = 0,
					AdditionalCount = 0,
				},
				Questions = [new DnsQuestion("example.com", (int)DnsRecordType.A, 1)],
				Answers = answers,
				Authority = [],
				Additional = [],
				ResponseCode = code,
				Size = 64,
			},
			Server = new IPEndPoint(IPAddress.Parse("192.0.2.53"), 53),
			Elapsed = TimeSpan.FromMilliseconds(5),
		},
		[]);

	private static DnsRecord Record(int type, string data) => new()
	{
		Name = "example.com",
		Type = type,
		Class = 1,
		Ttl = 300,
		Data = data,
	};
}
