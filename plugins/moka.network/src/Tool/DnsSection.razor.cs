using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Lookup;
using Moka.Network.Targets;
using Moka.Red.Core.Enums;
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

	private string _name = "";
	private DnsRecordType _type = DnsRecordType.A;
	private DnsResolverKind _resolver;
	private string _custom = "";
	private string? _error;
	private string? _note;
	private int _askedType;
	private DnsLookupResult? _result;

	private string Summary => Running
		? "asking"
		: _result is { } result ? $"{AnswerCount(result.Exchange.Message.Answers.Count)} from {DnsClient.Describe(result.Exchange.Server)}" : "records from any resolver, as it answers";

	protected override void OnInitialized()
	{
		_resolver = Settings.Resolver;
		_custom = Settings.CustomResolver;
	}

	private static MokaColor CodeColor(int code) => code switch
	{
		0 => MokaColor.Success,
		3 => MokaColor.Warning,
		_ => MokaColor.Error,
	};

	private static string TtlText(uint ttl) => ttl switch
	{
		< 60 => ttl.ToString(CultureInfo.InvariantCulture) + "s",
		< 3600 => string.Create(CultureInfo.InvariantCulture, $"{ttl / 60}m{(ttl % 60 == 0 ? "" : $"{ttl % 60}s")}"),
		< 86400 => string.Create(CultureInfo.InvariantCulture, $"{ttl / 3600}h{(ttl % 3600 == 0 ? "" : $"{ttl % 3600 / 60}m")}"),
		_ => string.Create(CultureInfo.InvariantCulture, $"{ttl / 86400}d{(ttl % 86400 == 0 ? "" : $"{ttl % 86400 / 3600}h")}"),
	};

	private static string AnswerCount(int count) => count == 1 ? "1 answer" : $"{count} answers";

	private static string TtlTitle(uint ttl) => ttl.ToString("N0", CultureInfo.CurrentCulture) + " seconds";

	private static string ServerLine(DnsLookupResult result)
	{
		DnsExchange exchange = result.Exchange;
		string flags = string.Join(' ', exchange.Message.Header.FlagNames());
		return $"{DnsClient.Describe(exchange.Server)}, {Milliseconds(exchange.Elapsed)}, flags {flags}";
	}

	/// <summary>The answer the way dig lays it out, for pasting into a ticket.</summary>
	private static string AsText(DnsLookupResult result)
	{
		DnsMessage message = result.Exchange.Message;
		StringBuilder text = new();
		DnsQuestion? question = message.Questions.Count > 0 ? message.Questions[0] : null;
		text.Append(CultureInfo.InvariantCulture, $";; {question?.Name} {DnsNames.TypeName(question?.Type ?? 0)} from {DnsClient.Describe(result.Exchange.Server)}");
		text.Append(CultureInfo.InvariantCulture, $" in {result.Exchange.Elapsed.TotalMilliseconds:0} ms, {message.ResponseCodeName}, flags {string.Join(' ', message.Header.FlagNames())}\n");
		Append(text, "ANSWER", message.Answers);
		Append(text, "AUTHORITY", message.Authority);
		Append(text, "ADDITIONAL", message.Additional);
		return text.ToString().TrimEnd();

		static void Append(StringBuilder text, string title, IReadOnlyList<DnsRecord> records)
		{
			if (records.Count == 0)
			{
				return;
			}

			text.Append(CultureInfo.InvariantCulture, $";; {title}\n");
			foreach (DnsRecord record in records)
			{
				text.Append(CultureInfo.InvariantCulture, $"{record.Name}\t{record.Ttl}\t{DnsNames.ClassName(record.Class)}\t{record.TypeName}\t{record.Data}\n");
			}
		}
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
		string input = _name.Trim();
		string unbracketed = input.StartsWith('[') && input.EndsWith(']') ? input[1..^1] : input;
		string name;
		int type = (int)_type;
		string? note = null;
		if (AddressText.TryParse(unbracketed, out IPAddress? address))
		{
			name = ReverseName.For(address);
			type = (int)DnsRecordType.PTR;
			note = $"{address} is an address, so this asked for its PTR record at {name}.";
		}
		else if (DnsName.TryNormalize(input, out string? normalized, out string? problem))
		{
			name = normalized;
		}
		else
		{
			_error = problem;
			return;
		}

		DnsResolverKind resolver = _resolver;
		string custom = _custom;
		TimeSpan timeout = Settings.DnsTimeout;
		CancellationTokenSource run = BeginRun();
		_result = null;
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
}
