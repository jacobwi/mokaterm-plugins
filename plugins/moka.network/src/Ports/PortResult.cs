using System.Net;

namespace Moka.Network.Ports;

/// <summary>One port checked: the outcome, the endpoint that was dialled, how long it took and what the server said first.</summary>
public sealed record PortResult
{
	public required int Port { get; init; }

	public required PortState State { get; init; }

	public required IPEndPoint Endpoint { get; init; }

	public required TimeSpan Elapsed { get; init; }

	/// <summary>The greeting as plain text, or null when none was asked for or none came.</summary>
	public string? Greeting { get; init; }

	/// <summary>How many bytes of greeting arrived, before any were dropped as control characters.</summary>
	public int GreetingBytes { get; init; }

	/// <summary>A sentence for anything other than open, refused or timed out.</summary>
	public string? Detail { get; init; }
}
