namespace Moka.Network.Tool;

/// <summary>What the last send put on the wire, and where and when.</summary>
public sealed record WakeSent(string Mac, string Endpoint, int Bytes, string Hex, DateTimeOffset At);
