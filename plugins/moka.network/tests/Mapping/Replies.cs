using System.Net;
using Moka.Network.Lookup;

namespace Moka.Network.Tests.Mapping;

/// <summary>
/// Replies the mapping tests build themselves at the level the mapping logic reads: parsed messages and records, so a
/// test says what a referral or an answer is, not how it is encoded.
/// </summary>
internal static class Replies
{
	public static readonly IPEndPoint Root = new(IPAddress.Parse("198.41.0.4"), 53);

	public static readonly IPEndPoint Tld = new(IPAddress.Parse("192.5.6.30"), 53);

	public static readonly IPEndPoint Zone = new(IPAddress.Parse("93.184.216.34"), 53);

	public static DnsMessage Message(
		IEnumerable<DnsRecord>? answers = null,
		IEnumerable<DnsRecord>? authority = null,
		IEnumerable<DnsRecord>? additional = null,
		int rcode = 0,
		bool authoritative = false)
	{
		DnsRecord[] answerRecords = [.. (answers ?? [])];
		DnsRecord[] authorityRecords = [.. (authority ?? [])];
		DnsRecord[] additionalRecords = [.. (additional ?? [])];
		return new DnsMessage
		{
			Header = new DnsHeader
			{
				Id = 1,
				IsResponse = true,
				Opcode = 0,
				Authoritative = authoritative,
				Truncated = false,
				RecursionDesired = true,
				RecursionAvailable = false,
				AuthenticData = false,
				CheckingDisabled = false,
				ResponseCode = rcode,
				QuestionCount = 1,
				AnswerCount = answerRecords.Length,
				AuthorityCount = authorityRecords.Length,
				AdditionalCount = additionalRecords.Length,
			},
			Questions = [new DnsQuestion("example.com", 1, 1)],
			Answers = answerRecords,
			Authority = authorityRecords,
			Additional = additionalRecords,
			ResponseCode = rcode,
			Size = 64,
		};
	}

	public static DnsRecord Record(string name, DnsRecordType type, string data, bool malformed = false) => new()
	{
		Name = name,
		Type = (int)type,
		Class = 1,
		Ttl = 300,
		Data = data,
		Malformed = malformed,
	};

	public static DnsExchange Exchange(DnsMessage message, IPEndPoint? server = null, int milliseconds = 12) => new()
	{
		Message = message,
		Server = server ?? Root,
		Elapsed = TimeSpan.FromMilliseconds(milliseconds),
	};

	/// <summary>A lookup result with no earlier failures, which is what one working resolver gives.</summary>
	public static DnsLookupResult LookupResult(DnsMessage message, IPEndPoint? server = null, int milliseconds = 12) => new(Exchange(message, server, milliseconds), []);
}
