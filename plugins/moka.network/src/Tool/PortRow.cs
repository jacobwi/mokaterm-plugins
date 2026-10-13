using Moka.Network.Ports;

namespace Moka.Network.Tool;

/// <summary>One port in the list: its number, and its result once the check came back.</summary>
public sealed record PortRow(int Port, PortResult? Result = null);
