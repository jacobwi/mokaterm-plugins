namespace Moka.Network.Echo;

/// <summary>How a trace runs: how far it goes, how many probes per hop, how long each waits and how big it is.</summary>
public sealed record TraceOptions(int MaxHops, int ProbesPerHop, TimeSpan Timeout, int PayloadSize = EchoRequest.DefaultPayload)
{
	public const int MaxMaxHops = 30;

	public const int DefaultProbesPerHop = 3;
}
