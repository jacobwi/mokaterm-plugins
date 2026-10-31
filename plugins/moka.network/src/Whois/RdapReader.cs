using System.Text.Json;

namespace Moka.Network.Whois;

/// <summary>
/// Reads the parts of an RDAP answer this tool shows, out of the JSON the registry sent. Every field is read as
/// whatever it is; an answer without a field simply leaves that fact unset rather than failing the whole read.
/// </summary>
internal static class RdapReader
{
	/// <summary>
	/// Reads one answer: the domain's facts and, when the answer refers the question to the registrar's own RDAP
	/// server, that server's url. Throws <see cref="JsonException"/> when the body is not JSON.
	/// </summary>
	public static (RdapDomain Domain, string? Referral) Read(string json)
	{
		ArgumentNullException.ThrowIfNull(json);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new JsonException("The RDAP answer is not a JSON object.");
		}

		RdapDomain domain = new()
		{
			Name = String(root, "ldhName") ?? "",
			Handle = String(root, "handle"),
			Statuses = Array(root, "status"),
			Registered = EventDate(root, "registration"),
			Updated = EventDate(root, "last changed"),
			Expires = EventDate(root, "expiration"),
			Nameservers = Nameservers(root),
		};

		JsonElement registrar = Registrar(root);
		return (domain with
		{
			Registrar = VcardName(registrar),
			RegistrarId = Identifier(registrar),
		}, Referral(root));
	}

	/// <summary>
	/// The registrar's own RDAP url, when the answer names one: in the registrar entity's links first, where a thin
	/// registry puts it, or among the answer's own links. Only an HTTPS url that names a domain record is followed.
	/// </summary>
	private static string? Referral(JsonElement root)
	{
		JsonElement registrar = Registrar(root);
		if (registrar.ValueKind == JsonValueKind.Object && DomainLink(registrar) is { } fromEntity)
		{
			return fromEntity;
		}

		return DomainLink(root);
	}

	private static string? DomainLink(JsonElement element) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty("links", out JsonElement links)
			? links.EnumerateArray()
				.Select(link => (Url: String(link, "href"), Rel: String(link, "rel")))
				.Where(link => link.Url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true
					&& link.Rel == "related"
					&& link.Url.Contains("/domain/", StringComparison.OrdinalIgnoreCase))
				.Select(link => link.Url)
				.FirstOrDefault()
			: null;

	private static JsonElement Registrar(JsonElement root)
	{
		if (!root.TryGetProperty("entities", out JsonElement entities) || entities.ValueKind != JsonValueKind.Array)
		{
			return default;
		}

		foreach (JsonElement entity in entities.EnumerateArray())
		{
			if (Array(entity, "roles").Any(role => string.Equals(role, "registrar", StringComparison.OrdinalIgnoreCase)))
			{
				return entity;
			}
		}

		return default;
	}

	/// <summary>The fn of an entity's vCard: the name a registrar writes for itself.</summary>
	private static string? VcardName(JsonElement entity)
	{
		if (entity.ValueKind != JsonValueKind.Object
			|| !entity.TryGetProperty("vcardArray", out JsonElement vcard)
			|| vcard.ValueKind != JsonValueKind.Array
			|| vcard.GetArrayLength() < 2
			|| vcard[1].ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (JsonElement property in vcard[1].EnumerateArray())
		{
			if (property.ValueKind == JsonValueKind.Array && property.GetArrayLength() >= 4
				&& property[0].ValueKind == JsonValueKind.String && property[0].GetString() == "fn")
			{
				string? name = property[3].GetString();
				return string.IsNullOrWhiteSpace(name) ? null : name;
			}
		}

		return null;
	}

	private static string? Identifier(JsonElement entity)
	{
		if (entity.ValueKind != JsonValueKind.Object || !entity.TryGetProperty("publicIds", out JsonElement ids) || ids.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (JsonElement id in ids.EnumerateArray())
		{
			if (String(id, "identifier") is { Length: > 0 } identifier)
			{
				return identifier;
			}
		}

		return null;
	}

	private static string? EventDate(JsonElement root, string action)
	{
		if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (JsonElement happened in events.EnumerateArray())
		{
			if (string.Equals(String(happened, "eventAction"), action, StringComparison.OrdinalIgnoreCase)
				&& String(happened, "eventDate") is { Length: > 0 } date)
			{
				return date;
			}
		}

		return null;
	}

	private static IReadOnlyList<string> Nameservers(JsonElement root)
	{
		if (!root.TryGetProperty("nameservers", out JsonElement nameservers) || nameservers.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return [.. nameservers.EnumerateArray().Select(nameserver => String(nameserver, "ldhName")).OfType<string>()];
	}

	private static string? String(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object
			&& element.TryGetProperty(property, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static IReadOnlyList<string> Array(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty(property, out JsonElement values)
			|| values.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return [.. values.EnumerateArray().Select(value => value.GetString()).OfType<string>()];
	}
}
