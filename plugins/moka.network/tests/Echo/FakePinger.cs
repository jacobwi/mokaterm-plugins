using System.Net;
using Moka.Network.Echo;

namespace Moka.Network.Tests.Echo;

/// <summary>Answers echo requests from a script: what the n-th probe at a given TTL gets back.</summary>
internal sealed class FakePinger(Func<int, int, EchoReply> answer) : IPinger
{
	private readonly Dictionary<int, int> _attempts = [];

	public List<EchoRequest> Requests { get; } = [];

	public Action? OnSend { get; set; }

	public Task<EchoReply> SendAsync(IPAddress address, EchoRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Requests.Add(request);
		OnSend?.Invoke();
		int attempt = _attempts.TryGetValue(request.Ttl, out int sent) ? sent : 0;
		_attempts[request.Ttl] = attempt + 1;
		return Task.FromResult(answer(request.Ttl, attempt));
	}

	public static EchoReply Router(string address, double milliseconds = 5) => new(EchoStatus.TtlExpired, IPAddress.Parse(address), milliseconds);

	public static EchoReply Reply(string address, double milliseconds = 9) => new(EchoStatus.Reply, IPAddress.Parse(address), milliseconds);
}
