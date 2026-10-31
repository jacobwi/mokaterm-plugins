using System.Text;
using Mokaterm.Abstractions.Commands;

namespace Moka.Network.Tests.Server;

/// <summary>
/// A command feature that answers from a script the test writes: it keeps every request it was given and returns what
/// the script says, or a clean empty success when it says nothing. It honours cancellation the way the real one does,
/// which is what makes the runner's timeout honest to test.
/// </summary>
public sealed class FakeCommandFeature : IRemoteCommandFeature
{
	public Func<RemoteCommandRequest, CancellationToken, Task<RemoteCommandOutcome>>? Script { get; set; }

	public List<RemoteCommandRequest> Requests { get; } = [];

	/// <summary>What the last run heard through <c>OnOutput</c>, if the script sent anything that way.</summary>
	public List<(CommandStream Stream, string Text)> Heard { get; } = [];

	public Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		if (Script is { } script)
		{
			return script(request, cancellationToken);
		}

		return Task.FromResult(Outcome(""));
	}

	/// <summary>A run that ended with <paramref name="exitCode"/> and stdout <paramref name="output"/> on it.</summary>
	public static RemoteCommandOutcome Outcome(string output, int exitCode = 0, string error = "") => new()
	{
		ExitCode = exitCode,
		Output = Capture(output),
		Error = Capture(error),
		Duration = TimeSpan.FromMilliseconds(4),
	};

	/// <summary>A run that stays going until something cancels it, which is a hung command.</summary>
	public static async Task<RemoteCommandOutcome> Hang(RemoteCommandRequest request, CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		return Outcome("");
	}

	public static CapturedOutput Capture(string text) => new(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text));
}
