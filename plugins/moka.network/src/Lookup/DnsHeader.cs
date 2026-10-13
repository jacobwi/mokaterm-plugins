namespace Moka.Network.Lookup;

/// <summary>The fixed twelve bytes at the start of every DNS message, read into its fields.</summary>
public sealed record DnsHeader
{
	public required ushort Id { get; init; }

	public required bool IsResponse { get; init; }

	public required int Opcode { get; init; }

	/// <summary>AA: the answer comes from a server for the zone, not from a cache.</summary>
	public required bool Authoritative { get; init; }

	/// <summary>TC: the reply did not fit and was cut; the question has to be asked again over TCP.</summary>
	public required bool Truncated { get; init; }

	public required bool RecursionDesired { get; init; }

	public required bool RecursionAvailable { get; init; }

	/// <summary>AD: the resolver says it validated the answer with DNSSEC.</summary>
	public required bool AuthenticData { get; init; }

	public required bool CheckingDisabled { get; init; }

	/// <summary>The four bit response code from the header alone; <see cref="DnsMessage.ResponseCode"/> adds the OPT record's bits.</summary>
	public required int ResponseCode { get; init; }

	public required int QuestionCount { get; init; }

	public required int AnswerCount { get; init; }

	public required int AuthorityCount { get; init; }

	public required int AdditionalCount { get; init; }

	/// <summary>The flags that are set, lower case and in dig's order: qr aa tc rd ra ad cd.</summary>
	public IReadOnlyList<string> FlagNames()
	{
		List<string> flags = [];
		if (IsResponse)
		{
			flags.Add("qr");
		}

		if (Authoritative)
		{
			flags.Add("aa");
		}

		if (Truncated)
		{
			flags.Add("tc");
		}

		if (RecursionDesired)
		{
			flags.Add("rd");
		}

		if (RecursionAvailable)
		{
			flags.Add("ra");
		}

		if (AuthenticData)
		{
			flags.Add("ad");
		}

		if (CheckingDisabled)
		{
			flags.Add("cd");
		}

		return flags;
	}
}
