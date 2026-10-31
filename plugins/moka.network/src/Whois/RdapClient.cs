using System.Text.Json;
using Moka.Network.Lookup;
using Moka.Network.Targets;

namespace Moka.Network.Whois;

/// <summary>
/// Whois's other half: the same registry facts asked over RDAP (RFC 9082), which is plain HTTPS and so works where TCP
/// port 43 is blocked. One bootstrap list, fetched from IANA once per app run, names the registry for a domain's top
/// level domain; that registry is asked for the domain, and when its answer refers the question to the registrar's own
/// RDAP server, that is asked too. Like the DNS tab, every request leaves from wherever Mokaterm runs.
/// </summary>
public sealed class RdapClient : IDisposable
{
	private readonly IRdapTransport _transport;
	private readonly SemaphoreSlim _bootstrapLock = new(1, 1);
	private IReadOnlyList<RdapService>? _bootstrap;

	public void Dispose() => _bootstrapLock.Dispose();

	public RdapClient(IRdapTransport transport)
	{
		ArgumentNullException.ThrowIfNull(transport);
		_transport = transport;
	}

	/// <summary>Asks the registries for <paramref name="domain"/> (the normalized name, see <see cref="DnsName.TryNormalize"/>).</summary>
	public async Task<RdapResult> LookupDomainAsync(string domain, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(domain);
		string tld = domain.Split('.')[^1];
		RdapService? service = RdapBootstrap.Find(await BootstrapAsync(timeout, cancellationToken), tld);
		if (service?.HttpsUrl is not { } baseUrl)
		{
			throw new ProbeException($"The RDAP bootstrap list from data.iana.org names no HTTPS server for .{tld} domains.");
		}

		string url = RdapBootstrap.DomainRequest(baseUrl, domain);
		(RdapDomain record, string? referral, string json) = await AskAsync(url, domain, timeout, cancellationToken);
		RdapResult result = new()
		{
			Query = domain,
			Server = HostOf(baseUrl),
			Domain = record,
			RegistryJson = json,
		};

		if (referral is not { } next || !RefersElsewhere(baseUrl, next))
		{
			return result;
		}

		// The registrar's answer adds what a thin registry leaves out; when it fails, the registry's stands.
		try
		{
			(RdapDomain registrarRecord, _, string registrarJson) = await AskAsync(next, domain, timeout, cancellationToken);
			return result with
			{
				RegistrarServer = HostOf(next),
				Domain = registrarRecord.Registrar is { } registrar ? record with { Registrar = registrar } : record,
				RegistrarJson = registrarJson,
			};
		}
		catch (ProbeException ex)
		{
			return result with { Note = $"{HostOf(next)} could not be asked: {ex.Message}" };
		}
	}

	private static bool RefersElsewhere(string baseUrl, string referral) =>
		RdapBootstrap.Host(referral) is { } host && !string.Equals(host, HostOf(baseUrl), StringComparison.OrdinalIgnoreCase);

	private static string HostOf(string url) => RdapBootstrap.Host(url) ?? url;

	private async Task<(RdapDomain Domain, string? Referral, string Json)> AskAsync(string url, string domain, TimeSpan timeout, CancellationToken cancellationToken)
	{
		RdapReply reply = await _transport.GetAsync(url, timeout, cancellationToken);
		if (reply.Status == 404)
		{
			throw new ProbeException($"{HostOf(url)} has no RDAP record for {domain}.");
		}

		if (reply.Status != 200)
		{
			throw new ProbeException($"{HostOf(url)} answered {reply.Status}.");
		}

		try
		{
			(RdapDomain record, string? referral) = RdapReader.Read(reply.Json);
			return (record, referral, reply.Json);
		}
		catch (JsonException ex)
		{
			throw new ProbeException($"The RDAP answer from {HostOf(url)} is not the JSON it should be.", ex);
		}
	}

	/// <summary>The bootstrap list, fetched once and kept for the rest of the app run; every lookup shares the one copy.</summary>
	private async Task<IReadOnlyList<RdapService>> BootstrapAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (_bootstrap is { } cached)
		{
			return cached;
		}

		await _bootstrapLock.WaitAsync(cancellationToken);
		try
		{
			if (_bootstrap is { } held)
			{
				return held;
			}

			RdapReply reply;
			try
			{
				reply = await _transport.GetAsync(RdapBootstrap.Url, timeout, cancellationToken);
			}
			catch (ProbeException ex)
			{
				throw new ProbeException($"The RDAP bootstrap list from data.iana.org could not be fetched: {ex.Message}", ex);
			}

			if (reply.Status != 200)
			{
				throw new ProbeException($"data.iana.org answered {reply.Status} for the RDAP bootstrap list.");
			}

			try
			{
				return _bootstrap = RdapBootstrap.Parse(reply.Json);
			}
			catch (JsonException ex)
			{
				throw new ProbeException("The RDAP bootstrap list from data.iana.org could not be read: it is not the JSON it should be.", ex);
			}
		}
		finally
		{
			_bootstrapLock.Release();
		}
	}
}
