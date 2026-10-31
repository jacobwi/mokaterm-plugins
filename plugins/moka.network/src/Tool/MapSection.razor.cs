using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Lookup;
using Moka.Network.Lookup.Mapping;
using Moka.Network.Settings;
using Moka.Network.Subnets;
using Moka.Network.Targets;

namespace Moka.Network.Tool;

/// <summary>
/// The DNS map: how a name resolves, drawn. The delegation is walked from the roots without recursion, the CNAMEs are
/// followed hop by hop, every resolver answers the same question side by side, and a prefix's addresses can be swept
/// for their names. Each view keeps what it found until the next run of its own kind.
/// </summary>
public sealed partial class MapSection : NetworkViewBase
{
	private static readonly IReadOnlyList<DnsRecordType> MapTypes =
	[
		DnsRecordType.A,
		DnsRecordType.AAAA,
		DnsRecordType.CNAME,
		DnsRecordType.MX,
		DnsRecordType.NS,
		DnsRecordType.TXT,
		DnsRecordType.SOA,
		DnsRecordType.SRV,
		DnsRecordType.CAA,
		DnsRecordType.PTR,
	];

	private string _name = "";
	private DnsRecordType _type = DnsRecordType.A;
	private string _prefix = "192.168.1.0/24";
	private string? _error;
	private string? _note;
	private string? _stage;
	private MapViews? _views;
	private int _sweepDone;
	private int _sweepTotal;

	private string Summary => Running
		? _sweepTotal > 0 ? $"asked {Count(_sweepDone)} of {Count(_sweepTotal)}" : _stage ?? "asking"
		: _views is { Any: true } views ? $"drawn for {Subject(views)}" : "how a name resolves, from the roots down";

	private static string Subject(MapViews views) => views.Name.Length > 0 ? views.Name : views.SweepPrefix ?? "";

	/// <summary>The classes a delegation step's outcome colors it by.</summary>
	private static string StepClass(DelegationStepKind kind) => kind switch
	{
		DelegationStepKind.Referral => "mn-map-level mn-map-level--referral",
		DelegationStepKind.Answer => "mn-map-level mn-map-level--answer",
		DelegationStepKind.NoData => "mn-map-level mn-map-level--nodata",
		_ => "mn-map-level mn-map-level--failed",
	};

	/// <summary>The classes a resolver row's code cell gets: plain for NOERROR, warn for NXDOMAIN, bad for the rest.</summary>
	private static string CodeClass(ResolverMatrixRow row) => row.Code switch
	{
		"NOERROR" => "mn-td mn-td--mono mn-c-code",
		"NXDOMAIN" => "mn-td mn-td--mono mn-td--warn mn-c-code",
		null => "mn-td mn-td--mono mn-td--dim mn-c-code",
		_ => "mn-td mn-td--mono mn-td--bad mn-c-code",
	};

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await MapAsync();
		}
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return MapAsync();
	}

	private async Task MapAsync()
	{
		_error = null;
		_note = null;
		string input = _name.Trim();
		if (AddressText.TryParse(input, out _))
		{
			_error = "The map follows a name. For an address, sweep its prefix below, or ask the DNS tab for its PTR record.";
			return;
		}

		if (!DnsName.TryNormalize(input, out string? name, out string? problem))
		{
			_error = problem;
			return;
		}

		int type = (int)_type;
		Recent.Remember(name);
		NetworkSettings settings = Settings;
		TimeSpan timeout = settings.DnsTimeout;
		string custom = settings.CustomResolver;
		CancellationTokenSource run = BeginRun();
		_views = new MapViews { Name = name, Type = type };
		_sweepDone = 0;
		_sweepTotal = 0;
		_stage = "asking the root servers";
		StateHasChanged();
		try
		{
			await Task.Run(
				async () =>
				{
					DelegationWalkResult walk = await DelegationWalk.RunAsync(
						(server, question, asked, token) => DnsClient.ExchangeAsync(server, question, asked, timeout, token),
						name, type, run.Token);
					Post(() => _views = _views! with { Walk = walk });

					Post(() => _stage = "following the CNAME chain");
					IReadOnlyList<IPEndPoint> chainServers = DnsResolvers.Servers(settings.Resolver, custom, out string? unusable);
					if (chainServers.Count > 0)
					{
						CnameChainResult chain = await CnameChain.RunAsync(
							(question, asked, token) => DnsLookup.RunAsync(chainServers, question, asked, timeout, token),
							name, type, run.Token);
						Post(() => _views = _views! with { Chain = chain });
					}
					else
					{
						Post(() => _note = $"The CNAME chain was skipped: {unusable}");
					}

					Post(() => _stage = "asking each resolver");
					List<DnsResolverKind> kinds = [DnsResolverKind.System, DnsResolverKind.Cloudflare, DnsResolverKind.Google, DnsResolverKind.Quad9];
					if (custom.Trim().Length > 0)
					{
						kinds.Add(DnsResolverKind.Custom);
					}

					ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(
						kinds, custom,
						(servers, question, asked, token) => DnsLookup.RunAsync(servers, question, asked, timeout, token),
						name, type, run.Token);
					Post(() =>
					{
						_views = _views! with { Matrix = matrix };
						_stage = null;
					});
				},
				run.Token);
		}
		catch (DnsLookupException ex)
		{
			_error = ex.Message;
			_stage = null;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_error = "Stopped before the map was drawn.";
			_stage = null;
		}
		finally
		{
			EndRun(run);
		}
	}

	private async Task SweepAsync()
	{
		_error = null;
		if (!SubnetMath.TryCalculate(_prefix, out SubnetInfo? subnet, out string? problem))
		{
			_error = problem;
			return;
		}

		if (!PtrSweep.TryAddresses(subnet, out IReadOnlyList<IPAddress>? addresses, out string? sweepProblem))
		{
			_error = sweepProblem;
			return;
		}

		NetworkSettings settings = Settings;
		IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(settings.Resolver, settings.CustomResolver, out string? unusable);
		if (servers.Count == 0)
		{
			_error = unusable ?? "There is no DNS server to ask.";
			return;
		}

		TimeSpan timeout = settings.DnsTimeout;
		string cidr = subnet.Cidr;
		CancellationTokenSource run = BeginRun();
		_views = (_views ?? new MapViews { Name = "", Type = 0 }) with { SweepPrefix = cidr, Sweep = null };
		_sweepDone = 0;
		_sweepTotal = addresses.Count;
		_stage = null;
		_note = null;
		StateHasChanged();
		try
		{
			PtrSweepResult sweep = await Task.Run(
				() => PtrSweep.RunAsync(
					(address, token) => DnsLookup.ReverseAsync(servers, address, timeout, token),
					addresses,
					done => Post(() => _sweepDone = done),
					run.Token),
				run.Token);
			Post(() => _views = _views! with { Sweep = sweep });
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_error = $"Stopped after {Count(_sweepDone)} of {Count(_sweepTotal)} addresses.";
		}
		finally
		{
			EndRun(run);
		}
	}
}
