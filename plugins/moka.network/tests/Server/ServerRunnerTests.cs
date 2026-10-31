using System.Text;
using Moka.Network.Server;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Moka.Network.Tests.Server;

public sealed class ServerRunnerTests
{
	[Fact]
	public async Task RunAsync_SetsOneLocaleAndCarriesTheCommand()
	{
		FakeCommandFeature feature = new();
		ServerRunner runner = new(() => feature);

		ServerRun run = await runner.RunAsync("getent 'hosts' 'example.com'", TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(run.Ok);
		RemoteCommandRequest request = Assert.Single(feature.Requests);
		Assert.Equal("LC_ALL=C getent 'hosts' 'example.com'", request.Command);
		Assert.Equal(run.Command, request.Command);
		Assert.Equal(ServerRunner.QuickOutputBytes, request.OutputLimitBytes);
		Assert.False(request.RunsLong);
	}

	[Fact]
	public async Task RunAsync_PassesTheOutputCapThroughAndRaisesOneBelowTheFloor()
	{
		FakeCommandFeature feature = new();
		ServerRunner runner = new(() => feature);

		await runner.RunAsync("true", TimeSpan.FromSeconds(5), outputLimitBytes: 4096, cancellationToken: TestContext.Current.CancellationToken);
		await runner.RunAsync("true", TimeSpan.FromSeconds(5), outputLimitBytes: 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(4096, feature.Requests[0].OutputLimitBytes);
		Assert.Equal(RemoteCommandRequest.MinOutputLimitBytes, feature.Requests[1].OutputLimitBytes);
	}

	[Fact]
	public async Task RunAsync_MarksALongRunAndWiresTheOutputHandler()
	{
		FakeCommandFeature feature = new()
		{
			Script = (request, _) =>
			{
				request.OnOutput?.Invoke(CommandStream.Output, "one\n"u8);
				return Task.FromResult(FakeCommandFeature.Outcome("one\n"));
			},
		};
		List<(CommandStream Stream, string Text)> heard = [];
		ServerRunner runner = new(() => feature);

		ServerRun run = await runner.RunAsync("tcpdump 'tcp'", Timeout.InfiniteTimeSpan, runsLong: true, onOutput: (stream, bytes) => heard.Add((stream, Encoding.UTF8.GetString(bytes))), cancellationToken: TestContext.Current.CancellationToken);

		RemoteCommandRequest request = Assert.Single(feature.Requests);
		Assert.True(request.RunsLong);
		Assert.NotNull(request.OnOutput);
		Assert.Single(heard, item => item.Text == "one\n");
		Assert.Equal("one\n", run.Output);
	}

	[Fact]
	public async Task RunAsync_WithoutAFeatureToRunOn_SaysSoPlainly()
	{
		ServerRunner runner = new(() => null);

		ServerRun run = await runner.RunAsync("true", TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(run.NeverRan);
		Assert.False(run.Ok);
		Assert.Null(run.ExitCode);
		Assert.Contains("not connected", run.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WhenTheCommandHangs_GivesUpAndSaysItStoppedWaiting()
	{
		FakeCommandFeature feature = new() { Script = FakeCommandFeature.Hang };
		ServerRunner runner = new(() => feature);

		ServerRun run = await runner.RunAsync("sleep '60'", TimeSpan.FromMilliseconds(50), cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(run.TimedOut);
		Assert.False(run.Ok);
		Assert.Contains("stopped waiting", run.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WhenTheHostRefuses_CarriesItsWordsForTheUser()
	{
		FakeCommandFeature feature = new()
		{
			Script = (_, _) => Task.FromException<RemoteCommandOutcome>(new RemoteCommandException("The connection dropped while the command ran.")),
		};
		ServerRunner runner = new(() => feature);

		ServerRun run = await runner.RunAsync("true", TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(run.Broke);
		Assert.Equal("The connection dropped while the command ran.", run.Problem);
	}

	[Fact]
	public async Task RunAsync_StoppedByTheCaller_ComesBackAsCancellationNotAsATimeout()
	{
		using CancellationTokenSource stop = new();
		FakeCommandFeature feature = new() { Script = FakeCommandFeature.Hang };
		ServerRunner runner = new(() => feature);
		stop.CancelAfter(TimeSpan.FromMilliseconds(30));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync("sleep '60'", TimeSpan.FromSeconds(30), cancellationToken: stop.Token));
	}

	[Fact]
	public async Task RunAsync_AReadOfBothStreams_PrefersStdoutAndKeepsStderrForTheFailures()
	{
		FakeCommandFeature feature = new() { Script = (_, _) => Task.FromResult(FakeCommandFeature.Outcome("the answer", 1, "the complaint")) };
		ServerRunner runner = new(() => feature);

		ServerRun run = await runner.RunAsync("ss '-tulpn'", TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(1, run.ExitCode);
		Assert.Equal("the answer", run.Output);
		Assert.Equal("the complaint", run.Error);
		Assert.Equal("the answer", run.Text);
		Assert.Equal(FakeCommandFeature.Outcome("").Duration, run.Duration);
	}

	[Fact]
	public async Task ProgramsAsync_ReportsTheOnesTheServerHas()
	{
		FakeCommandFeature feature = new()
		{
			Script = (_, _) => Task.FromResult(FakeCommandFeature.Outcome("getent\nss\n")),
		};
		ServerRunner runner = new(() => feature);

		IReadOnlySet<string> present = await runner.ProgramsAsync(["getent", "ss", "tcpdump"], cancellationToken: TestContext.Current.CancellationToken);

		Assert.Contains("getent", present);
		Assert.Contains("ss", present);
		Assert.DoesNotContain("tcpdump", present);
		RemoteCommandRequest request = Assert.Single(feature.Requests);
		Assert.StartsWith("LC_ALL=C sh -c '", request.Command, StringComparison.Ordinal);
		Assert.DoesNotContain("getent hosts", request.Command, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ProgramsAsync_WhenNothingRan_ReportsNothingRatherThanFailing()
	{
		FakeCommandFeature feature = new() { Script = (_, _) => Task.FromResult(FakeCommandFeature.Outcome("", exitCode: 127)) };
		ServerRunner runner = new(() => feature);

		Assert.Empty(await runner.ProgramsAsync(["getent"], cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ProgramsAsync_RefusesANameThatIsNotAPlainWord()
	{
		ServerRunner runner = new(() => new FakeCommandFeature());

		await Assert.ThrowsAsync<ArgumentException>(() => runner.ProgramsAsync(["getent; rm"], cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void For_AsksTheSessionAgainOnEveryRun()
	{
		FakeSession session = new();
		ServerRunner runner = ServerRunner.For(session);
		Assert.True(runner.Ready);

		session.State = SessionState.Disconnected;
		Assert.False(runner.Ready);

		session.State = SessionState.Connected;
		Assert.True(runner.Ready);
	}

	/// <summary>The whole of ISessionHandle the runner reads: the live session, which is null unless connected.</summary>
	private sealed class FakeSession : ISessionHandle
	{
		public SessionState State { get; set; } = SessionState.Connected;

		public FakeCommandFeature Feature { get; init; } = new();

		public Guid Id => Guid.NewGuid();

		public string Title => "test@server";

		public HostProfile Host => new() { Id = Guid.NewGuid(), Address = "server" };

		public ConnectionProfile Connection => new() { Id = Guid.NewGuid(), HostId = Host.Id, ProtocolId = "ssh" };

		public ProtocolDescriptor Protocol => new()
		{
			Id = "ssh",
			DisplayName = "SSH",
			DefaultPort = 22,
			Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands,
		};

		public bool IsTransient => true;

		public string? StatusMessage => null;

		public ConnectFailure? Failure => null;

		public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? ConnectedAt => State == SessionState.Connected ? DateTimeOffset.UnixEpoch : null;

		public ITerminalStream? Terminal => null;

		public IProtocolSession? Session => State == SessionState.Connected ? Live : null;

		private LiveSession Live { get; } = new();

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public IRemoteCommandFeature FeatureForTests => Live.Feature;

		private sealed class LiveSession : IProtocolSession
		{
			public FakeCommandFeature Feature { get; } = new();

			public Task Completion => Task.CompletedTask;

			public TFeature? GetFeature<TFeature>()
				where TFeature : class =>
				typeof(TFeature) == typeof(IRemoteCommandFeature) ? (TFeature)(object)Feature : null;

			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}
}
