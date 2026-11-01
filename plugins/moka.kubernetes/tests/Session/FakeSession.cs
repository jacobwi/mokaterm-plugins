using Moka.Kubernetes.Tests.Server;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Moka.Kubernetes.Tests.Session;

/// <summary>
/// A session handle as the static render needs it: a state, a title, a host, and a live protocol session whenever
/// the state says connected, holding a <see cref="FakeCommandFeature"/> the test scripts. Nothing else on the handle
/// is real, and nothing the tabs read asks for more.
/// </summary>
public sealed class FakeSessionHandle : ISessionHandle, IDisposable
{
	private readonly LiveSession _live = new();

	public SessionState State { get; set; } = SessionState.Connected;

	public string Title { get; set; } = "deploy@server.example";

	public FakeCommandFeature Command { get; } = new();

	public FakeSessionHandle() => _live.Feature = Command;

	public Guid Id => Guid.Empty;

	public HostProfile Host => new() { Id = Guid.Empty, Address = "203.0.113.10", Name = "server" };

	public ConnectionProfile Connection => new() { Id = Guid.Empty, HostId = Guid.Empty, ProtocolId = "ssh" };

	public ProtocolDescriptor Protocol => new()
	{
		Id = "ssh",
		DisplayName = "SSH",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands,
	};

	public bool IsTransient => false;

	public string? StatusMessage => null;

	public ConnectFailure? Failure => null;

	public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

	public DateTimeOffset? ConnectedAt => State == SessionState.Connected ? DateTimeOffset.UnixEpoch : null;

	public ITerminalStream? Terminal => null;

	public IProtocolSession? Session => State == SessionState.Connected ? _live : null;

	public event Action? Changed
	{
		add { }
		remove { }
	}

	public void Dispose() => _live.DisposeAsync().AsTask().GetAwaiter().GetResult();

	/// <summary>
	/// Answers every kubectl ask the way a small healthy cluster would: versions, a context, cluster-info, nodes, pods,
	/// deployments, one pod's JSON record and a log tail. The static render never asks; this is for the tests that do.
	/// </summary>
	public static FakeSessionHandle FullyEquipped()
	{
		FakeSessionHandle session = new();
		session.Command.Script = (request, _) => Task.FromResult(FakeCommandFeature.Outcome(
			request.Command.Contains("command -v", StringComparison.Ordinal) ? "/usr/bin/kubectl\n"
			: request.Command.Contains("'version'", StringComparison.Ordinal) ? Fixtures.VersionWithServer
			: request.Command.Contains("'cluster-info'", StringComparison.Ordinal) ? Fixtures.ClusterInfo
			: request.Command.Contains("'current-context'", StringComparison.Ordinal) ? "production\n"
			: request.Command.Contains("'nodes'", StringComparison.Ordinal) ? Fixtures.Nodes
			: request.Command.Contains("'pods'", StringComparison.Ordinal) ? Fixtures.PodsWide
			: request.Command.Contains("'deployments'", StringComparison.Ordinal) ? Fixtures.Deployments
			: request.Command.Contains("-o' 'json", StringComparison.Ordinal) ? Fixtures.PodJson
			: request.Command.Contains("'logs'", StringComparison.Ordinal) ? Fixtures.LogTail
			: ""));
		return session;
	}

	private sealed class LiveSession : IProtocolSession
	{
		public FakeCommandFeature? Feature { get; set; }

		public Task Completion => Task.CompletedTask;

		public TFeature? GetFeature<TFeature>()
			where TFeature : class =>
			typeof(TFeature) == typeof(IRemoteCommandFeature) && Feature is not null ? (TFeature)(object)Feature : null;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
