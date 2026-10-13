namespace Moka.Network.Lookup;

/// <summary>A DNS message read in full: the header, the question and the three record sections.</summary>
public sealed record DnsMessage
{
	public required DnsHeader Header { get; init; }

	public required IReadOnlyList<DnsQuestion> Questions { get; init; }

	public required IReadOnlyList<DnsRecord> Answers { get; init; }

	public required IReadOnlyList<DnsRecord> Authority { get; init; }

	/// <summary>The additional section without the OPT pseudo record, which is read into <see cref="EdnsPayloadSize"/> and the response code.</summary>
	public required IReadOnlyList<DnsRecord> Additional { get; init; }

	/// <summary>The UDP size the other side's OPT record offers, or null when it sent none.</summary>
	public int? EdnsPayloadSize { get; init; }

	/// <summary>The full response code: the header's four bits plus the eight the OPT record carries.</summary>
	public required int ResponseCode { get; init; }

	/// <summary>How many bytes the message was.</summary>
	public required int Size { get; init; }

	public string ResponseCodeName => DnsNames.ResponseCodeName(ResponseCode);
}
