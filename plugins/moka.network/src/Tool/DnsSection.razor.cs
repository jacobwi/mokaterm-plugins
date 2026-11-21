using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Lookup;
using Moka.Network.Targets;
using Mokaterm.UI.Common.Components;

namespace Moka.Network.Tool;

/// <summary>
/// Asks a resolver one question with the plugin's own DNS client, so the answer shows exactly what that server said:
/// every section, the TTLs, the flags and the response code, rather than what the system resolver makes of it.
/// </summary>
public sealed partial class DnsSection : NetworkViewBase
{
	private static readonly IReadOnlyList<EnumOption<DnsResolverKind>> Resolvers =
	[
		new(DnsResolverKind.System, "This machine"),
		new(DnsResolverKind.Cloudflare, "Cloudflare"),
		new(DnsResolverKind.Google, "Google"),
		new(DnsResolverKind.Quad9, "Quad9"),
		new(DnsResolverKind.Custom, "Custom"),
	];

	/// <summary>The resolvers every race asks, beside whichever custom one is set.</summary>
	private static readonly IReadOnlyList<DnsResolverKind> RaceKinds =
	[
		DnsResolverKind.System,
		DnsResolverKind.Cloudflare,
		DnsResolverKind.Google,
		DnsResolverKind.Quad9,
	];

	private string _name = "";
	private DnsRecordType _type = DnsRecordType.A;
	private DnsResolverKind _resolver;
	private string _custom = "";
	private string? _error;
	private string? _note;
	private int _askedType;
	private DnsLookupResult? _result;
	private IReadOnlyList<ResolverRaceResult>? _race;
	private string _raceQuestion = "";
	private bool _racing;

	private string Summary => Running
		? "Asking"
		: _result is { } result ? $"{AnswerCount(result.Exchange.Message.Answers.Count)} from {DnsClient.Describe(result.Exchange.Server)}"
		: _race is { } race ? $"{race.Count(row => row.Answered)} of {race.Count} resolvers answered"
		: "Records from any resolver, as it answers";

	private static string AnswerCount(int count) => count == 1 ? "1 answer" : $"{count} answers";

	protected override void OnInitialized()
	{
		_resolver = Settings.Resolver;
		_custom = Settings.CustomResolver;
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await LookupAsync();
		}
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return LookupAsync();
	}

	private async Task LookupAsync()
	{
		_error = null;
		if (!TryReadQuestion(out string name, out int type, out string target, out string? note))
		{
			return;
		}

		Recent.Remember(target);
		DnsResolverKind resolver = _resolver;
		string custom = _custom;
		TimeSpan timeout = Settings.DnsTimeout;
		CancellationTokenSource run = BeginRun();
		_result = null;
		_race = null;
		_racing = false;
		StateHasChanged();
		try
		{
			// Listing this machine's servers walks every network interface, which is no work for the render thread.
			DnsLookupResult result = await Task.Run(
				async () =>
				{
					IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(resolver, custom, out string? unusable);
					if (servers.Count == 0)
					{
						throw new DnsLookupException(unusable ?? "There is no DNS server to ask.");
					}

					return await DnsLookup.RunAsync(servers, name, type, timeout, run.Token);
				},
				run.Token);
			_result = result;
			_note = note;
			_askedType = type;
		}
		catch (DnsLookupException ex)
		{
			_error = ex.Message;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_error = "Stopped before an answer came.";
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>
	/// The race action: the question in the fields to every resolver at once, the built-in services plus the custom one
	/// when it is set, so one slow server cannot hide the others' pace.
	/// </summary>
	private async Task RaceAsync()
	{
		_error = null;
		if (!TryReadQuestion(out string name, out int type, out string target, out _))
		{
			return;
		}

		Recent.Remember(target);
		DnsResolverKind resolver = _resolver;
		string custom = _custom;
		TimeSpan timeout = Settings.DnsTimeout;
		CancellationTokenSource run = BeginRun();
		_result = null;
		_race = null;
		_racing = true;
		StateHasChanged();
		try
		{
			// Listing this machine's servers walks every network interface, which is no work for the render thread.
			IReadOnlyList<ResolverRaceResult> results = await Task.Run(
				async () =>
				{
					List<ResolverRaceEntry> entries = [];
					foreach (DnsResolverKind kind in RaceKinds)
					{
						entries.Add(new(DnsResolvers.Label(kind), DnsResolvers.Servers(kind, null, out _)));
					}

					if (resolver == DnsResolverKind.Custom && DnsServerAddress.TryParse(custom, out IPEndPoint? endpoint, out _))
					{
						entries.Add(new($"Custom {DnsClient.Describe(endpoint)}", [endpoint]));
					}

					return await ResolverRace.RunAsync(entries, name, type, timeout, DnsLookup.RunAsync, Time, run.Token);
				},
				run.Token);
			_race = results;
			_raceQuestion = $"{name} {DnsNames.TypeName(type)}";
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_error = "Stopped before the race finished.";
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>Reads the question the fields stand for: a name and its type, or an address as its PTR record. False leaves the error set.</summary>
	private bool TryReadQuestion(out string name, out int type, out string target, out string? note)
	{
		name = "";
		type = (int)_type;
		target = "";
		note = null;
		string input = _name.Trim();
		string unbracketed = input.StartsWith('[') && input.EndsWith(']') ? input[1..^1] : input;
		if (AddressText.TryParse(unbracketed, out IPAddress? address))
		{
			name = ReverseName.For(address);
			type = (int)DnsRecordType.PTR;
			note = $"{address} is an address, so this asked for its PTR record at {name}.";
			target = address.ToString();
			return true;
		}

		if (DnsName.TryNormalize(input, out string? normalized, out string? problem))
		{
			name = normalized;
			target = normalized;
			return true;
		}

		_error = problem;
		return false;
	}
}
