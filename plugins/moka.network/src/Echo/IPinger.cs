using System.Net;

namespace Moka.Network.Echo;

/// <summary>Sends one ICMP echo request. Ping and traceroute go through this, so their logic runs against a fake in tests.</summary>
public interface IPinger
{
	/// <summary>Sends one request and waits for its outcome. Throws <see cref="OperationCanceledException"/> only for <paramref name="cancellationToken"/>.</summary>
	Task<EchoReply> SendAsync(IPAddress address, EchoRequest request, CancellationToken cancellationToken);
}
