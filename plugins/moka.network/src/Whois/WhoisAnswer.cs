namespace Moka.Network.Whois;

/// <summary>One server's answer, already plain text, or why that server gave none.</summary>
public sealed record WhoisAnswer
{
	public required string Server { get; init; }

	public required string Text { get; init; }

	public required int Bytes { get; init; }

	public required TimeSpan Elapsed { get; init; }

	/// <summary>The answer was longer than what was left of the size cap and was cut there.</summary>
	public bool Cut { get; init; }

	/// <summary>The server stopped sending before it closed, so the answer may be incomplete.</summary>
	public bool TimedOut { get; init; }

	/// <summary>The server this answer refers to, whether or not it was asked.</summary>
	public string? Referral { get; init; }

	/// <summary>Why this server gave no answer; <see cref="Text"/> is empty then.</summary>
	public string? Error { get; init; }
}
