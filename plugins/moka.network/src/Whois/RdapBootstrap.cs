using System.Text.Json;

namespace Moka.Network.Whois;

/// <summary>
/// IANA's RDAP bootstrap list for DNS, fetched from <see cref="Url"/>: for every top level domain, which registry
/// servers speak RDAP. The list is a JSON object whose <c>services</c> array holds one entry per service, itself an
/// array of the domains served and the urls that serve them.
/// </summary>
public static class RdapBootstrap
{
	public const string Url = "https://data.iana.org/rdap/dns.json";

	/// <summary>
	/// Reads the list. An entry without both a domains array and a urls array of strings is skipped, the way a server's
	/// stray bytes are; anything that is not this JSON at all throws <see cref="JsonException"/> for the caller to word.
	/// </summary>
	public static IReadOnlyList<RdapService> Parse(string json)
	{
		ArgumentNullException.ThrowIfNull(json);
		using JsonDocument document = JsonDocument.Parse(json);
		if (!document.RootElement.TryGetProperty("services", out JsonElement services) || services.ValueKind != JsonValueKind.Array)
		{
			throw new JsonException("The bootstrap list has no services array.");
		}

		List<RdapService> parsed = [];
		foreach (JsonElement entry in services.EnumerateArray())
		{
			if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
			{
				continue;
			}

			IReadOnlyList<string>? tlds = Strings(entry[0]);
			IReadOnlyList<string>? urls = Strings(entry[1]);
			if (tlds is null || urls is null)
			{
				continue;
			}

			parsed.Add(new RdapService(tlds, urls));
		}

		return parsed;
	}

	/// <summary>The service the list names for <paramref name="tld"/>, or null when it names none.</summary>
	public static RdapService? Find(IReadOnlyList<RdapService> services, string tld)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(tld);
		return services.FirstOrDefault(service => service.Serves(tld));
	}

	/// <summary>
	/// The request for <paramref name="domain"/> to send to a service's base url. Most registries end their url before
	/// the object type, some name it already; both shapes get the one path RFC 9082 asks for.
	/// </summary>
	public static string DomainRequest(string baseUrl, string domain)
	{
		ArgumentNullException.ThrowIfNull(baseUrl);
		ArgumentNullException.ThrowIfNull(domain);
		string trimmed = baseUrl.TrimEnd('/');
		return trimmed.EndsWith("/domain", StringComparison.OrdinalIgnoreCase)
			? $"{trimmed}/{domain}"
			: $"{trimmed}/domain/{domain}";
	}

	/// <summary>The host an RDAP url names, or null when the value is no url this client would follow.</summary>
	public static string? Host(string url) =>
		Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.Host : null;

	private static List<string>? Strings(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		List<string> values = [];
		foreach (JsonElement value in element.EnumerateArray())
		{
			if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text)
			{
				return null;
			}

			values.Add(text);
		}

		return values;
	}
}
