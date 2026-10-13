using Moka.Network.Echo;

namespace Moka.Network.Tool;

/// <summary>One line of a ping run: its sequence number and what came back.</summary>
public sealed record PingRow(int Sequence, EchoReply Reply);
